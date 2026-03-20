namespace ZapiCli.Core.Trace;

/// <summary>
/// Persistent configuration for the trace system (<c>trace-config.json</c>).
/// </summary>
public sealed record TraceConfig
{
    /// <summary>
    /// Default directory (or .json file path) used for trace file output when
    /// <c>--export-path</c> is not supplied to <c>trace session start</c>.
    /// Null means no default is configured.
    /// </summary>
    public string? DefaultExportPath { get; init; }
}

/// <summary>
/// Manages the <c>trace-config.json</c> file in the platform config directory.
/// </summary>
public interface ITraceConfigStore
{
    /// <summary>Loads the trace config, returning defaults if the file does not exist.</summary>
    Task<TraceConfig> LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the trace config to disk.</summary>
    Task SaveAsync(TraceConfig config, CancellationToken ct = default);
}
