using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using MCServer.Cli;
using MCServer.Plugins;
using MCServer.Server;

namespace MCServer.Services;

/// <summary>
/// Local named-pipe endpoint used by one-off CLI invocations to talk to an
/// already-running MCServer host. The pipe is never exposed over the network.
/// </summary>
public sealed class CliPipeServer : BackgroundService
{
    private readonly GameServers _servers;
    private readonly SettingsService _settings;
    private readonly IServerHost _host;
    private readonly ILogger<CliPipeServer> _logger;

    public CliPipeServer(
        GameServers servers,
        SettingsService settings,
        IServerHost host,
        ILogger<CliPipeServer> logger)
    {
        _servers = servers;
        _settings = settings;
        _host = host;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        await using var pipe = CreatePipe();

        while (!stop.IsCancellationRequested)
        {
            try
            {
                await pipe.WaitForConnectionAsync(stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CLI pipe unavailable; disabling CLI forwarding for this host");
                return;
            }

            try
            {
                await HandleConnectionAsync(pipe, stop);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CLI pipe connection failed");
            }
            finally
            {
                if (pipe.IsConnected)
                {
                    try { pipe.Disconnect(); } catch { }
                }
            }
        }
    }

    private static NamedPipeServerStream CreatePipe() =>
        new(CliProtocol.PipeName, PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken stop)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
            bufferSize: 4096, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
        {
            AutoFlush = true,
        };

        var line = await reader.ReadLineAsync(stop);
        if (string.IsNullOrWhiteSpace(line))
            return;

        if (line.Length > CliProtocol.MaxMessageBytes)
        {
            await WriteResultAsync(writer, false, error: "The request was too large.");
            return;
        }

        CliRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CliRequest>(line, CliProtocol.JsonOptions);
        }
        catch (JsonException ex)
        {
            await WriteResultAsync(writer, false, error: $"Invalid request: {ex.Message}");
            return;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Command))
        {
            await WriteResultAsync(writer, false, error: "Empty request.");
            return;
        }

        CliPipeMessage response = await DispatchAsync(request, pipe, writer, stop);
        await WriteMessageAsync(writer, response, stop);
    }

    private async Task<CliPipeMessage> DispatchAsync(
        CliRequest request,
        NamedPipeServerStream pipe,
        StreamWriter writer,
        CancellationToken stop)
    {
        try
        {
            return request.Command.ToLowerInvariant() switch
            {
                "status" => Status(request),
                "run" or "exec" => RunServer(request),
                "start" => Power(request, PowerAction.Start),
                "stop" => Power(request, PowerAction.Stop),
                "restart" => Power(request, PowerAction.Restart),
                "create-server" => await CreateServerAsync(request, pipe, writer, stop),
                "delete-server" => DeleteServer(request),
                _ => CliPipeMessage.Result(false, error: $"Unknown pipe command '{request.Command}'."),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CLI pipe command {Command} failed", request.Command);
            return CliPipeMessage.Result(false, error: ex.Message);
        }
    }

    private CliPipeMessage Status(CliRequest request)
    {
        var selected = Select(request.Server);
        if (selected.Count == 0)
            return CliPipeMessage.Result(false, error: "No servers are configured.");

        if (request.Json)
        {
            var json = JsonSerializer.Serialize(selected.Select(s => new
            {
                name = s.Settings.Name,
                id = s.Settings.ID,
                type = s.Settings.Type,
                path = s.ServerPath,
                running = s.ServerRunningStatus,
                maintenance = s.CommandRunning,
                status = s.ServerStatus.Status,
                players = s.PlayerCount,
                startup = s.StartupDate,
                version = s.Settings.Version,
            }), CliProtocol.JsonOptions);

            return CliPipeMessage.Result(true, [json]);
        }

        var lines = new List<string>
        {
            "Name                 ID                                   Status      Players  Startup",
            "-------------------- ------------------------------------ ----------- ------- --------------------",
        };

        foreach (var server in selected.OrderBy(s => s.Settings.Name, StringComparer.OrdinalIgnoreCase))
        {
            var name = Truncate(server.Settings.Name, 20).PadRight(20);
            var id = Truncate(server.Settings.ID, 36).PadRight(36);
            var state = (server.ServerStatus.Status ?? "Unknown").PadRight(11);
            var players = server.PlayerCount.ToString().PadRight(7);
            var startup = server.StartupDate?.ToString("g") ?? "—";
            lines.Add($"{name} {id} {state} {players} {startup}");
        }

        return CliPipeMessage.Result(true, lines);
    }

    private CliPipeMessage RunServer(CliRequest request)
    {
        var server = Find(request.Server);
        if (server is null) return CliPipeMessage.Result(false, error: $"Server '{request.Server}' not found.");

        var command = string.Join(' ', request.Arguments ?? []).Trim();
        if (command.Length == 0)
            return CliPipeMessage.Result(false, error: "A command is required, e.g. --run test say hello");

        server.RunCommand(command);
        return CliPipeMessage.Result(true, [$"Command '{command}' sent to '{server.Settings.Name}'."]);
    }

    private CliPipeMessage Power(CliRequest request, PowerAction action)
    {
        var server = Find(request.Server);
        if (server is null) return CliPipeMessage.Result(false, error: $"Server '{request.Server}' not found.");

        switch (action)
        {
            case PowerAction.Start:
                server.StartServer();
                return CliPipeMessage.Result(true, [$"Start requested for '{server.Settings.Name}'."]);
            case PowerAction.Stop:
                server.StopServer(TimeSpan.FromSeconds(10));
                return CliPipeMessage.Result(true, [$"Stop requested for '{server.Settings.Name}'."]);
            case PowerAction.Restart:
                server.RestartServer(TimeSpan.FromSeconds(10));
                return CliPipeMessage.Result(true, [$"Restart requested for '{server.Settings.Name}'."]);
            default:
                return CliPipeMessage.Result(false, error: "Unknown power action.");
        }
    }

    private async Task<CliPipeMessage> CreateServerAsync(
        CliRequest request,
        NamedPipeServerStream pipe,
        StreamWriter writer,
        CancellationToken stop)
    {
        var existing = _settings.Settings.ServerSettings.ToList();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop);

        var progress = new PipeProgress(pipe, writer, () => linked.Cancel(), !request.Download);

        var result = await CliServerCreation.CreateAsync(
            Program.ServerPath,
            request.Name,
            request.Type,
            request.Preview,
            request.AutoStart,
            !request.Download,
            existing,
            progress,
            linked.Token);

        if (!result.Ok)
            return CliPipeMessage.Result(false, error: result.Error);

        var settings = result.Settings!;

        try
        {
            _settings.Settings.ServerSettings.Add(settings);
            _settings.Save();
            _servers.Register(settings, _host);
        }
        catch (Exception ex)
        {
            ServerProvisioning.CleanupFailedCreate(result.Folder ?? Path.Combine(Program.ServerPath, settings.ID));
            return CliPipeMessage.Result(false, error: $"Failed to register the new server: {ex.Message}");
        }

        return CliPipeMessage.Result(true,
        [
            $"Server '{settings.Name}' created.",
            $"ID: {settings.ID}",
            $"Path: {result.Folder}",
            request.Download
                ? $"Version: {settings.Version}"
                : "Files were not downloaded; create them before starting the server.",
        ]);
    }

    private CliPipeMessage DeleteServer(CliRequest request)
    {
        var query = request.Server ?? request.Arguments?.FirstOrDefault();
        var server = Find(query);
        if (server is null) return CliPipeMessage.Result(false, error: $"Server '{query}' not found.");

        if (!request.Confirmed)
            return CliPipeMessage.Result(
                false,
                lines: [$"This will permanently delete '{server.Settings.Name}' and all files under:", server.ServerPath],
                requiresConfirmation: true,
                confirmationName: server.Settings.ID);

        try
        {
            ServerProvisioning.DeleteServerDirectory(server);
        }
        catch (Exception ex)
        {
            return CliPipeMessage.Result(false, error: $"Failed to delete server files: {ex.Message}");
        }

        _servers.Unregister(server);
        _settings.RemoveServer(server.Settings.ID);

        return CliPipeMessage.Result(true, [$"Server '{server.Settings.Name}' deleted."]);
    }

    private List<IGameServer> Select(string? query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Equals("all", StringComparison.OrdinalIgnoreCase))
            return [.. _servers];

        var server = Find(query);
        return server is null ? [] : [server];
    }

    private IGameServer? Find(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;

        return _servers.FirstOrDefault(s =>
            string.Equals(s.Settings.ID, query, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(s.Settings.Name, query, StringComparison.OrdinalIgnoreCase));
    }

    private static string Truncate(string value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..(max - 1)] + "…";

    private static Task WriteResultAsync(StreamWriter writer, bool ok, string? error = null) =>
        WriteMessageAsync(writer, CliPipeMessage.Result(ok, error: error), CancellationToken.None);

    private static async Task WriteMessageAsync(StreamWriter writer, CliPipeMessage message, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message, CliProtocol.JsonOptions);
        await writer.WriteLineAsync(json.AsMemory(), ct);
        await writer.FlushAsync(ct);
    }

    private sealed class PipeProgress : IProgress<double>
    {
        private readonly NamedPipeServerStream _pipe;
        private readonly StreamWriter _writer;
        private readonly Action _disconnected;
        private readonly object _gate = new();
        private readonly bool _noDownload;
        private DateTime _last = DateTime.MinValue;

        public PipeProgress(NamedPipeServerStream pipe, StreamWriter writer, Action disconnected, bool noDownload)
        {
            _pipe = pipe;
            _writer = writer;
            _disconnected = disconnected;
            _noDownload = noDownload;
        }

        public void Report(double value)
        {
            if (!_pipe.IsConnected)
            {
                _disconnected();
                return;
            }

            var fraction = Math.Clamp(value, 0, 1);
            if (DateTime.UtcNow - _last < TimeSpan.FromMilliseconds(30) && fraction < 1)
                return;

            _last = DateTime.UtcNow;
            var phase = _noDownload ? "Creating folder..." : fraction < 0.8 ? "Downloading..." : "Extracting...";
            var message = $"{phase} {fraction * 100:F0}%";

            lock (_gate)
            {
                try
                {
                    _writer.WriteLine(JsonSerializer.Serialize(CliPipeMessage.Progress(fraction, message), CliProtocol.JsonOptions));
                    _writer.Flush();
                }
                catch
                {
                    _disconnected();
                }
            }
        }
    }

    private enum PowerAction
    {
        Start,
        Stop,
        Restart,
    }
}
