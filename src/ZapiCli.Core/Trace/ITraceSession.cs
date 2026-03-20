namespace ZapiCli.Core.Trace;

/// <summary>
/// Represents the persistent index entry for a named trace session stored in sessions.json.
/// UUID (<see cref="UniqueId"/>) is the primary key. <see cref="Name"/> is non-unique.
/// </summary>
public sealed record TraceSessionEntry
{
    /// <summary>UUID v4 — primary key. Unique across all sessions.</summary>
    public required string UniqueId { get; init; }

    /// <summary>Human-readable label. Non-unique — two sessions may share a name.</summary>
    public required string Name { get; init; }

    public required DateTimeOffset StartTime { get; init; }

    /// <summary>Absolute path to the JSONL trace file written by <see cref="TraceWriter"/>.</summary>
    public required string ExportPath { get; init; }

    /// <summary>Number of trace entries written so far. Also used as the next Seq base.</summary>
    public int EntryCount { get; init; }

    /// <summary>One of: active | closing | closed.</summary>
    public required string Status { get; init; }
}

/// <summary>
/// Manages the trace session index (<c>traces/sessions.json</c>) and provides
/// the mutex-protected entry-count increment used for monotonic Seq assignment.
/// </summary>
public interface ITraceSession
{
    /// <summary>
    /// Returns the most recently started session whose status is <c>active</c>, or null
    /// if no active session exists.
    /// </summary>
    Task<TraceSessionEntry?> GetActiveSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new session entry. Validates <paramref name="name"/> against
    /// [a-zA-Z0-9_.-], max 64 chars. Resolves <paramref name="requestedExportPath"/>
    /// (null = use configured default). Throws <see cref="ZapiCliException"/> on validation
    /// failures or missing export path.
    /// </summary>
    Task<TraceSessionEntry> StartSessionAsync(string name, string? requestedExportPath, CancellationToken ct = default);

    /// <summary>
    /// Sets session status to <c>closing</c> (under Mutex), waits <paramref name="waitMs"/> ms,
    /// then sets it to <c>closed</c> (under Mutex).
    /// </summary>
    Task CloseSessionAsync(string sessionId, int waitMs, CancellationToken ct = default);

    /// <summary>Sets session status back to <c>active</c> (under Mutex). EntryCount is preserved.</summary>
    Task ReopenSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Removes the sessions.json entry. The trace file at ExportPath is preserved.</summary>
    Task RemoveSessionAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Returns all sessions (active, closing, and closed).</summary>
    Task<IReadOnlyList<TraceSessionEntry>> ListSessionsAsync(CancellationToken ct = default);

    /// <summary>
    /// Under the UUID-keyed Mutex, atomically increments EntryCount in sessions.json
    /// and returns the new value (used as the entry's Seq number).
    /// Returns -1 on Mutex timeout or if the session is not found — caller must drop the entry.
    /// </summary>
    Task<int> IncrementEntryCountAsync(string sessionId, CancellationToken ct = default);

    Task<TraceSessionEntry?> FindByIdAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Returns all sessions with the given name (may be more than one).</summary>
    Task<IReadOnlyList<TraceSessionEntry>> FindByNameAsync(string name, CancellationToken ct = default);
}
