using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace MCServer.Cli;

/// <summary>Client half of the one-off CLI <-> running-host protocol.</summary>
public static class CliPipeClient
{
    /// <summary>
    /// Attempts to send a command to a running host. The result distinguishes
    /// an absent pipe (the host is not running) from an error response.
    /// </summary>
    public static async Task<CliPipeCall> ExecuteAsync(
        CliRequest request,
        TerminalProgress? progress = null,
        CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(".", CliProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipe.ConnectAsync(CliProtocol.ConnectTimeoutMilliseconds, cancellationToken);
        }
        catch (TimeoutException)
        {
            return CliPipeCall.Unavailable();
        }
        catch (IOException ex)
        {
            return CliPipeCall.Unavailable(ex.Message);
        }
        catch (UnauthorizedAccessException)
        {
            return CliPipeCall.Unavailable("Permission denied while opening the MCServer pipe.");
        }

        try
        {
            using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096, leaveOpen: true);
            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true,
            };

            await writer.WriteLineAsync(JsonSerializer.Serialize(request, CliProtocol.JsonOptions).AsMemory(), cancellationToken);
            await writer.FlushAsync(cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken);
                if (line is null)
                    return CliPipeCall.BrokenPipe();

                if (line.Length > CliProtocol.MaxMessageBytes)
                    return CliPipeCall.Failed("The host sent an invalid, oversized response.");

                CliPipeMessage message;
                try
                {
                    message = JsonSerializer.Deserialize<CliPipeMessage>(line, CliProtocol.JsonOptions)
                        ?? throw new JsonException("Empty response.");
                }
                catch (JsonException ex)
                {
                    return CliPipeCall.Failed($"Invalid response from MCServer: {ex.Message}");
                }

                if (string.Equals(message.Kind, "progress", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(message.Value ?? 0, message.Message);
                    continue;
                }

                if (!string.Equals(message.Kind, "result", StringComparison.OrdinalIgnoreCase))
                    return CliPipeCall.Failed($"Unknown response type '{message.Kind}' from MCServer.");

                progress?.Complete();
                return CliPipeCall.Finished(message);
            }

            return CliPipeCall.Failed("The operation was cancelled.");
        }
        catch (OperationCanceledException)
        {
            progress?.Cancel();
            return CliPipeCall.Failed("The operation was cancelled.");
        }
        catch (IOException ex)
        {
            return CliPipeCall.Failed($"Lost connection to MCServer: {ex.Message}");
        }
    }
}

/// <summary>Outcome of a named-pipe request.</summary>
public sealed class CliPipeCall
{
    private CliPipeCall(bool connected, CliPipeMessage? response, string? error)
    {
        Connected = connected;
        Response = response;
        Error = error;
    }

    public bool Connected { get; }
    public CliPipeMessage? Response { get; }
    public string? Error { get; }

    public bool Succeeded => Connected && Response?.Ok == true;
    public bool NotRunning => !Connected;
    public bool RequiresConfirmation => Response?.RequiresConfirmation == true;
    public string? ConfirmationName => Response?.ConfirmationName;

    public static CliPipeCall Unavailable(string? detail = null) => new(false, null, detail);
    public static CliPipeCall BrokenPipe() => new(true, null, "MCServer closed the connection without a response.");
    public static CliPipeCall Failed(string error) => new(true, null, error);
    public static CliPipeCall Finished(CliPipeMessage response) => new(true, response, null);
}
