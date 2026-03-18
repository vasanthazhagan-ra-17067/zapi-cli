using System.Text.Json;

namespace ZapiCli.Core;

/// <summary>
/// In-memory <see cref="IOutputWriter"/> for use in unit tests.
/// Thread-safe. Captures all output as serialized JSON strings for assertion.
/// </summary>
public sealed class InMemoryOutputWriter : IOutputWriter
{
    /// <summary>Captures a structured error written via <see cref="WriteError"/>.</summary>
    public sealed record ErrorRecord(string Message, string Code, int ExitCode);

    private readonly object _lock = new();

    /// <summary>All JSON strings emitted via <see cref="WriteJson"/> or <see cref="WriteRaw"/>.</summary>
    public List<string> SuccessOutput { get; } = new();

    /// <summary>All error records emitted via <see cref="WriteError"/>.</summary>
    public List<ErrorRecord> Errors { get; } = new();

    /// <summary>The last JSON string written to success output, or <c>null</c> if none.</summary>
    public string? LastSuccessJson
    {
        get { lock (_lock) { return SuccessOutput.Count > 0 ? SuccessOutput[^1] : null; } }
    }

    /// <summary>A snapshot of all success JSON strings.</summary>
    public IReadOnlyList<string> AllSuccessJson
    {
        get { lock (_lock) { return SuccessOutput.ToList(); } }
    }

    /// <summary>The last error record written, or <c>null</c> if none.</summary>
    public ErrorRecord? LastError
    {
        get { lock (_lock) { return Errors.Count > 0 ? Errors[^1] : null; } }
    }

    /// <summary>A snapshot of all error records.</summary>
    public IReadOnlyList<ErrorRecord> AllErrors
    {
        get { lock (_lock) { return Errors.ToList(); } }
    }

    /// <inheritdoc/>
    public void WriteJson(object data)
    {
        var json = JsonSerializer.Serialize(data, OutputJsonOptions.Shared);
        lock (_lock) { SuccessOutput.Add(json); }
    }

    /// <inheritdoc/>
    public void WriteRaw(string rawJson)
    {
        lock (_lock) { SuccessOutput.Add(rawJson); }
    }

    /// <inheritdoc/>
    public void WriteError(string message, string code, int exitCode = 1)
    {
        lock (_lock) { Errors.Add(new ErrorRecord(message, code, exitCode)); }
    }
}
