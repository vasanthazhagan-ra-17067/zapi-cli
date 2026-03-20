namespace ZapiCli.Core.Trace;

/// <summary>
/// Appends trace entries to the active session's export file with security header exclusion.
/// All exceptions are caught and logged internally — failures never propagate to the caller.
/// </summary>
public interface ITraceWriter
{
    /// <summary>
    /// Appends an API call trace entry to the active session's export file.
    /// Session, SessionId, and Seq are populated from the active session; all
    /// other fields must be set by the caller (ApiClient).
    /// If no session is active, returns silently without writing.
    /// </summary>
    Task AppendApiEntryAsync(ApiTraceEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Appends a Pex event trace entry to the active session's export file.
    /// If no session is active, returns silently without writing.
    /// </summary>
    Task AppendPexEntryAsync(PexTraceEntry entry, CancellationToken ct = default);
}
