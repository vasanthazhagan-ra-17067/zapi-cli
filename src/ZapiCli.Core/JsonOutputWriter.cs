using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapiCli.Core;

/// <summary>
/// Shared JSON serializer options for all output writers.
/// Uses SnakeCaseLower naming policy per the output contract (ADR-0008).
/// </summary>
internal static class OutputJsonOptions
{
    internal static readonly JsonSerializerOptions Shared = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };
}

/// <summary>
/// Error envelope. <c>exitCode</c> uses explicit JsonPropertyName to preserve
/// camelCase as required by the output contract (ADR-0008).
/// </summary>
internal sealed record ErrorEnvelope
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("exitCode")]
    public required int ExitCode { get; init; }
}

/// <summary>
/// Production <see cref="IOutputWriter"/> implementation.
/// Writes serialized JSON to stdout and error envelopes to stderr using System.Text.Json.
/// </summary>
public sealed class JsonOutputWriter : IOutputWriter
{
    /// <inheritdoc/>
    public void WriteJson(object data)
    {
        Console.WriteLine(JsonSerializer.Serialize(data, OutputJsonOptions.Shared));
    }

    /// <inheritdoc/>
    public void WriteRaw(string rawJson)
    {
        Console.WriteLine(rawJson);
    }

    /// <inheritdoc/>
    public void WriteError(string message, string code, int exitCode = 1)
    {
        var envelope = new ErrorEnvelope
        {
            Error = message,
            Code = code,
            ExitCode = exitCode,
        };
        Console.Error.WriteLine(JsonSerializer.Serialize(envelope, OutputJsonOptions.Shared));
    }
}
