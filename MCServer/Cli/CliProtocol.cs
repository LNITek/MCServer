using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCServer.Cli;

/// <summary>Command request sent by the one-shot CLI to a running host.</summary>
public sealed record CliRequest(
    string Command,
    IReadOnlyList<string>? Arguments = null,
    bool Confirmed = false,
    string? Server = null,
    string? Name = null,
    string? Type = null,
    bool Preview = false,
    bool AutoStart = false,
    bool Download = true,
    bool Json = false);

/// <summary>A progress event emitted by a running host.</summary>
public sealed record CliProgressMessage(double Value, string? Message);

/// <summary>Terminal response emitted by a running host.</summary>
public sealed record CliResultMessage(
    bool Ok,
    IReadOnlyList<string>? Lines = null,
    string? Error = null,
    bool RequiresConfirmation = false,
    string? ConfirmationName = null);

/// <summary>Wire message used for both progress and terminal responses.</summary>
public sealed record CliPipeMessage(
    string Kind,
    double? Value = null,
    string? Message = null,
    bool? Ok = null,
    IReadOnlyList<string>? Lines = null,
    string? Error = null,
    bool? RequiresConfirmation = null,
    string? ConfirmationName = null)
{
    public static CliPipeMessage Progress(double value, string? message) =>
        new("progress", value, message);

    public static CliPipeMessage Result(bool ok, IReadOnlyList<string>? lines = null, string? error = null,
        bool requiresConfirmation = false, string? confirmationName = null) =>
        new("result", Ok: ok, Lines: lines, Error: error, RequiresConfirmation: requiresConfirmation,
            ConfirmationName: confirmationName);
}

public static class CliProtocol
{
    public const string PipeName = "MCServer.Cli";
    public const int ConnectTimeoutMilliseconds = 2000;
    public const int MaxMessageBytes = 16 * 1024 * 1024;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        MaxDepth = 32,
    };
}
