using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapiCli.Core;

public sealed class JsonOutputWriter : IOutputWriter
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public void WriteSuccess(object data)
    {
        var envelope = new SuccessEnvelope { Data = data };
        Console.Out.WriteLine(JsonSerializer.Serialize(envelope, Options));
    }

    public void WriteError(string message, string code, int exitCode, object? detail = null)
    {
        var envelope = new ErrorEnvelope
        {
            Error = message,
            Code = code,
            ExitCode = exitCode,
            Detail = detail,
        };
        Console.Error.WriteLine(JsonSerializer.Serialize(envelope, Options));
    }

    // Internal envelope types — [JsonPropertyName] takes precedence over naming policy
    internal sealed class SuccessEnvelope
    {
        [JsonPropertyName("status")]
        public string Status { get; } = "ok";

        [JsonPropertyName("data")]
        public required object Data { get; init; }
    }

    internal sealed class ErrorEnvelope
    {
        [JsonPropertyName("error")]
        public required string Error { get; init; }

        [JsonPropertyName("code")]
        public required string Code { get; init; }

        [JsonPropertyName("exitCode")]
        public required int ExitCode { get; init; }

        [JsonPropertyName("detail")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Detail { get; init; }
    }
}
