using System.Text.Json;

namespace ZapiCli.Core;

/// <summary>
/// In-memory IOutputWriter for use in tests. Captures serialized JSON envelopes
/// in SuccessOutputs and ErrorOutputs for assertion.
/// </summary>
public sealed class InMemoryOutputWriter : IOutputWriter
{
    public List<string> SuccessOutputs { get; } = [];
    public List<string> ErrorOutputs { get; } = [];

    /// <summary>Last success envelope written, for convenience in single-assertion tests.</summary>
    public string? LastSuccess => SuccessOutputs.Count > 0 ? SuccessOutputs[^1] : null;

    /// <summary>Last error envelope written, for convenience in single-assertion tests.</summary>
    public string? LastError => ErrorOutputs.Count > 0 ? ErrorOutputs[^1] : null;

    public void WriteSuccess(object data)
    {
        var envelope = new JsonOutputWriter.SuccessEnvelope { Data = data };
        SuccessOutputs.Add(JsonSerializer.Serialize(envelope, JsonOutputWriter.Options));
    }

    public void WriteError(string message, string code, int exitCode, object? detail = null)
    {
        var envelope = new JsonOutputWriter.ErrorEnvelope
        {
            Error = message,
            Code = code,
            ExitCode = exitCode,
            Detail = detail,
        };
        ErrorOutputs.Add(JsonSerializer.Serialize(envelope, JsonOutputWriter.Options));
    }
}
