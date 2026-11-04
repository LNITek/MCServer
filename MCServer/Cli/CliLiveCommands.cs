using MCServer.Plugins;
using Newtonsoft.Json.Linq;

namespace MCServer.Cli;

/// <summary>
/// Commands that need a running host. The CLI first tries the named pipe;
/// if the host is absent, file-backed commands fall back to offline mode.
/// </summary>
public static class CliLiveCommands
{
    public static async Task<int> RunAsync(CliOptions o, string? server, IReadOnlyList<string> arguments, CancellationToken ct) =>
        await ForwardAsync("run", server, arguments, ct, o.Json, progress: null);

    public static async Task<int> ExecAsync(CliOptions o, string? server, IReadOnlyList<string> arguments, CancellationToken ct) =>
        await ForwardAsync("exec", server, arguments, ct, o.Json, progress: null);

    public static async Task<int> StartAsync(string? server, CancellationToken ct) =>
        await ForwardAsync("start", server, [], ct, json: false, progress: null);

    public static async Task<int> StopAsync(string? server, CancellationToken ct) =>
        await ForwardAsync("stop", server, [], ct, json: false, progress: null);

    public static async Task<int> RestartAsync(string? server, CancellationToken ct) =>
        await ForwardAsync("restart", server, [], ct, json: false, progress: null);

    public static async Task<int> StatusAsync(string? server, bool json, CancellationToken ct)
    {
        var call = await CliPipeClient.ExecuteAsync(
            new CliRequest("status", Json: json, Server: string.IsNullOrWhiteSpace(server) ? "all" : server),
            progress: null,
            cancellationToken: ct);

        if (call.NotRunning)
            return CliInfoHandlers.PrintServerStatusUnknown(server, json);

        return PrintCall(call, json);
    }

    public static async Task<int> CreateServerAsync(CliOptions o, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(o.Name))
        {
            Console.Error.WriteLine("--create-server requires --name \"Server Name\".");
            return 2;
        }

        var progress = new TerminalProgress();
        progress.Status("Contacting MCServer...");

        var call = await CliPipeClient.ExecuteAsync(
            new CliRequest(
                "create-server",
                Arguments: [],
                Server: null,
                Name: o.Name.Trim(),
                Type: string.IsNullOrWhiteSpace(o.Type) ? "BDS" : o.Type.Trim(),
                Preview: o.Preview,
                AutoStart: o.AutoStart,
                Download: !o.NoDownload,
                Json: o.Json),
            progress,
            ct);

        if (call.NotRunning)
        {
            // Offline fallback: build the folder + update settings.json without the host.
            progress.Status("MCServer is not running; creating server offline...");
            return await CreateServerOfflineAsync(o, progress, ct);
        }

        return PrintCall(call, o.Json);
    }

    public static async Task<int> DeleteServerAsync(CliOptions o, string? server, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            Console.Error.WriteLine("--delete-server requires a server name or ID.");
            return 2;
        }

        // Fast path: host running -> confirmation + deletion over the pipe.
        var call = await CliPipeClient.ExecuteAsync(
            new CliRequest("delete-server", Arguments: [server], Server: server, Confirmed: o.Yes),
            progress: null,
            cancellationToken: ct);

        if (call.NotRunning)
            return await DeleteServerOfflineAsync(o, server, ct);

        if (call.Response?.RequiresConfirmation == true)
        {
            var confirmation = call.ConfirmationName;
            var id = confirmation ?? CliOutput.FindServerEntry(server)?.Id ?? server;
            if (!ConfirmDelete(server!, id))
                return 1;

            call = await CliPipeClient.ExecuteAsync(
                new CliRequest("delete-server", Arguments: [server], Server: server, Confirmed: true),
                progress: null,
                cancellationToken: ct);
        }

        return PrintCall(call, o.Json);
    }

    // ---- offline implementations -----------------------------------------

    private static async Task<int> CreateServerOfflineAsync(CliOptions o, TerminalProgress progress, CancellationToken ct)
    {
        var entries = CliOutput.ReadServerEntries();
        var settingsEntries = LoadServerSettings();
        var name = o.Name!.Trim();
        var type = string.IsNullOrWhiteSpace(o.Type) ? "BDS" : o.Type.Trim();

        var result = await CliServerCreation.CreateAsync(
            Program.ServerPath,
            name,
            type,
            o.Preview,
            o.AutoStart,
            o.NoDownload,
            settingsEntries,
            new Progress<double>(p =>
            {
                var phase = p < 0.8 ? "Downloading..." : "Extracting...";
                progress.Report(p, o.NoDownload ? "Creating folder..." : $"{phase} {p * 100:F0}%");
            }),
            ct);

        if (!result.Ok)
        {
            progress.Fail(result.Error ?? "unknown error");
            return 1;
        }

        progress.Complete("Server files ready");

        var settings = result.Settings!;
        var serializer = Newtonsoft.Json.JsonSerializer.Create(JsonOptions.SerializerSettings);
        var entry = JObject.FromObject(settings, serializer);

        try
        {
            CliOutput.AddServerEntry(entry);
        }
        catch (Exception ex)
        {
            ServerProvisioning.CleanupFailedCreate(result.Folder ?? Path.Combine(Program.ServerPath, settings.ID));
            Console.Error.WriteLine($"Failed to persist the new server: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Server '{settings.Name}' created.");
        Console.WriteLine($"ID: {settings.ID}");
        Console.WriteLine($"Path: {result.Folder}");
        Console.WriteLine(o.NoDownload
            ? "Files were not downloaded; create them before starting the server."
            : $"Version: {settings.Version}");
        return 0;
    }

    private static async Task<int> DeleteServerOfflineAsync(CliOptions o, string server, CancellationToken ct)
    {
        var entry = CliOutput.FindServerEntry(server);
        if (entry is null)
        {
            Console.Error.WriteLine($"Server '{server}' not found. Run --list-servers to see configured servers.");
            return 1;
        }

        ServerProvisioning.ValidateDeletePath(Program.ServerPath, entry.Path);

        if (!o.Yes && !ConfirmDelete(entry.Name, entry.Id))
            return 1;

        try
        {
            if (Directory.Exists(entry.Path))
                Directory.Delete(entry.Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Failed to delete '{entry.Path}': {ex.Message}");
            return 1;
        }

        CliOutput.RemoveServerEntry(entry.Id);
        Console.WriteLine($"Server '{entry.Name}' deleted.");
        return 0;
    }

    // ---- pipe helpers ---------------------------------------------------

    private static async Task<int> ForwardAsync(
        string command,
        string? server,
        IReadOnlyList<string> arguments,
        CancellationToken ct,
        bool json,
        TerminalProgress? progress)
    {
        if (string.IsNullOrWhiteSpace(server))
        {
            Console.Error.WriteLine($"A server name or ID is required for --{command}.");
            return 2;
        }

        var call = await CliPipeClient.ExecuteAsync(
            new CliRequest(command, arguments, Server: server, Json: json),
            progress,
            ct);

        if (call.NotRunning)
        {
            Console.Error.WriteLine("MCServer is not running. Start MCServer and try again.");
            return 1;
        }

        return PrintCall(call, json);
    }

    private static int PrintCall(CliPipeCall call, bool json)
    {
        if (!call.Connected)
        {
            Console.Error.WriteLine("MCServer is not running. Start MCServer and try again.");
            return 1;
        }

        if (call.Response?.Ok == true)
        {
            if (call.Response.Lines is { Count: > 0 } lines)
            {
                foreach (var line in lines)
                {
                    if (json && line.TrimStart().StartsWith('{') || json && line.TrimStart().StartsWith('['))
                        Console.WriteLine(line);
                    else
                        Console.WriteLine(line);
                }
            }

            return 0;
        }

        Console.Error.WriteLine(call.Response?.Error ?? call.Error ?? "MCServer returned an unknown error.");
        return 1;
    }

    private static bool ConfirmDelete(string name, string id)
    {
        if (Console.IsInputRedirected)
        {
            Console.Error.WriteLine("Delete requires --yes when stdin is not interactive.");
            return false;
        }

        Console.WriteLine();
        Console.WriteLine($"This will permanently delete '{name}' and all files under its folder.");
        Console.WriteLine($"Type the server ID '{id}' to confirm:");
        var answer = Console.ReadLine()?.Trim();
        return string.Equals(answer, id, StringComparison.OrdinalIgnoreCase);
    }

    private static List<ServerSettings> LoadServerSettings()
    {
        var root = CliOutput.LoadSettingsRoot();
        var section = CliOutput.GetSection(root);
        var array = section["ServerSettings"] as Newtonsoft.Json.Linq.JArray;
        if (array is null) return [];

        return [.. array
            .OfType<Newtonsoft.Json.Linq.JObject>()
            .Select(item =>
            {
                try
                {
                    return item.ToObject<ServerSettings>(
                        Newtonsoft.Json.JsonSerializer.Create(JsonOptions.SerializerSettings))!;
                }
                catch
                {
                    return null!;
                }
            })
            .Where(s => s is not null)];
    }
}
