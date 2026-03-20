using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ZapiCli.Core.Trace;

/// <summary>
/// Manages the trace session index at <c>&lt;configDir&gt;/traces/sessions.json</c>.
/// UUID is the primary key for all internal operations.
/// </summary>
public sealed class TraceSession : ITraceSession
{
    // Regex for valid session names: [a-zA-Z0-9_.-], max 64 chars.
    private static readonly Regex ValidNameRegex =
        new(@"^[a-zA-Z0-9_.\\-]{1,64}$", RegexOptions.Compiled);

    private readonly string _sessionsFilePath;
    private readonly ITraceConfigStore _traceConfigStore;
    private readonly ILogger<TraceSession> _logger;

    public TraceSession(string configDir, ITraceConfigStore traceConfigStore, ILogger<TraceSession> logger)
    {
        _sessionsFilePath = Path.Combine(configDir, "traces", "sessions.json");
        _traceConfigStore = traceConfigStore;
        _logger = logger;
    }

    // ─── ITraceSession ──────────────────────────────────────────────────────

    public async Task<TraceSessionEntry?> GetActiveSessionAsync(CancellationToken ct = default)
    {
        var sessions = await LoadSessionsAsync(ct).ConfigureAwait(false);
        return sessions
            .Where(s => s.Status == "active")
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefault();
    }

    public async Task<TraceSessionEntry> StartSessionAsync(
        string name,
        string? requestedExportPath,
        CancellationToken ct = default)
    {
        // Validate session name.
        if (!ValidNameRegex.IsMatch(name))
            throw new ZapiCliException(
                $"Session name '{name}' is invalid. Only [a-zA-Z0-9_.-] are allowed, max 64 chars.",
                ErrorCodes.INVALID_ARGS);

        var sessionId = Guid.NewGuid().ToString();
        var exportPath = await ResolveExportPathAsync(name, sessionId, requestedExportPath, ct)
            .ConfigureAwait(false);

        var entry = new TraceSessionEntry
        {
            UniqueId = sessionId,
            Name = name,
            StartTime = DateTimeOffset.UtcNow,
            ExportPath = exportPath,
            EntryCount = 0,
            Status = "active",
        };

        var sessions = await LoadSessionsAsync(ct).ConfigureAwait(false);
        sessions.Add(entry);
        await SaveSessionsAsync(sessions, ct).ConfigureAwait(false);

        return entry;
    }

    public async Task CloseSessionAsync(string sessionId, int waitMs, CancellationToken ct = default)
    {
        // Step 1: Under Mutex, set status to 'closing'.
        await Task.Run(() => UpdateStatusSync(sessionId, "closing"), ct).ConfigureAwait(false);

        // Step 2: Wait for in-flight trace writes.
        await Task.Delay(waitMs, ct).ConfigureAwait(false);

        // Step 3: Under Mutex, set status to 'closed'.
        await Task.Run(() => UpdateStatusSync(sessionId, "closed"), ct).ConfigureAwait(false);
    }

    public Task ReopenSessionAsync(string sessionId, CancellationToken ct = default)
    {
        // Reopen: set status back to 'active'; EntryCount is not reset — seq continues.
        return Task.Run(() => UpdateStatusSync(sessionId, "active"), ct);
    }

    public async Task RemoveSessionAsync(string sessionId, CancellationToken ct = default)
    {
        // Remove only removes the sessions.json entry. The trace file is preserved.
        var sessions = await LoadSessionsAsync(ct).ConfigureAwait(false);
        var idx = sessions.FindIndex(s => s.UniqueId == sessionId);
        if (idx < 0)
            throw new ZapiCliException(
                $"Session '{sessionId}' not found.",
                ErrorCodes.SESSION_NOT_FOUND);

        sessions.RemoveAt(idx);
        await SaveSessionsAsync(sessions, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TraceSessionEntry>> ListSessionsAsync(CancellationToken ct = default)
    {
        return await LoadSessionsAsync(ct).ConfigureAwait(false);
    }

    public Task<int> IncrementEntryCountAsync(string sessionId, CancellationToken ct = default)
    {
        return Task.Run(() => IncrementSync(sessionId), ct);
    }

    public async Task<TraceSessionEntry?> FindByIdAsync(string sessionId, CancellationToken ct = default)
    {
        var sessions = await LoadSessionsAsync(ct).ConfigureAwait(false);
        return sessions.FirstOrDefault(s => s.UniqueId == sessionId);
    }

    public async Task<IReadOnlyList<TraceSessionEntry>> FindByNameAsync(string name, CancellationToken ct = default)
    {
        var sessions = await LoadSessionsAsync(ct).ConfigureAwait(false);
        return sessions
            .Where(s => s.Name.Equals(name, StringComparison.Ordinal))
            .ToList();
    }

    // ─── Private helpers ────────────────────────────────────────────────────

    private async Task<string> ResolveExportPathAsync(
        string sessionName,
        string sessionId,
        string? requestedExportPath,
        CancellationToken ct)
    {
        // First 8 chars of UUID sans hyphens (e.g. "c1a2b3d4").
        var shortId = sessionId.Replace("-", "")[..8];
        var fileName = $"{sessionName}-{shortId}.json";

        if (requestedExportPath != null)
        {
            if (requestedExportPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                return requestedExportPath;

            // Treat as directory.
            return Path.Combine(requestedExportPath, fileName);
        }

        // No --export-path supplied: read default from trace-config.json.
        var config = await _traceConfigStore.LoadAsync(ct).ConfigureAwait(false);
        if (config.DefaultExportPath != null)
            return Path.Combine(config.DefaultExportPath, fileName);

        throw new ZapiCliException(
            "No export path specified. Set a default with 'trace config set --default-export-path' or use --export-path.",
            ErrorCodes.EXPORT_PATH_NOT_SET);
    }

    /// <summary>
    /// Synchronous status update wrapped in the UUID-keyed Mutex.
    /// Used by CloseSessionAsync and ReopenSessionAsync via Task.Run.
    /// </summary>
    private void UpdateStatusSync(string sessionId, string newStatus)
    {
        var mutexName = GetMutexName(sessionId);
        bool acquired = false;
        using var mutex = new Mutex(false, mutexName, out _);
        try
        {
            try { acquired = mutex.WaitOne(2000); }
            catch (AbandonedMutexException) { acquired = true; }

            if (!acquired)
            {
                _logger.LogWarning(
                    "Mutex timeout while setting session {SessionId} status to {Status}.",
                    sessionId, newStatus);
                return;
            }

            var sessions = LoadSessionsSync();
            var idx = sessions.FindIndex(s => s.UniqueId == sessionId);
            if (idx >= 0)
            {
                sessions[idx] = sessions[idx] with { Status = newStatus };
                SaveSessionsSync(sessions);
            }
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    /// <summary>
    /// Synchronous entry-count increment wrapped in the UUID-keyed Mutex.
    /// Returns the new count (used as Seq), or -1 on timeout/not-found.
    /// </summary>
    private int IncrementSync(string sessionId)
    {
        var mutexName = GetMutexName(sessionId);
        bool acquired = false;
        using var mutex = new Mutex(false, mutexName, out _);
        try
        {
            try { acquired = mutex.WaitOne(2000); }
            catch (AbandonedMutexException) { acquired = true; }

            if (!acquired)
            {
                _logger.LogWarning(
                    "Mutex timeout while incrementing entry count for session {SessionId}.",
                    sessionId);
                return -1;
            }

            var sessions = LoadSessionsSync();
            var idx = sessions.FindIndex(s => s.UniqueId == sessionId);
            if (idx < 0)
            {
                _logger.LogWarning("Session {SessionId} not found during IncrementEntryCount.", sessionId);
                return -1;
            }

            var newCount = sessions[idx].EntryCount + 1;
            sessions[idx] = sessions[idx] with { EntryCount = newCount };
            SaveSessionsSync(sessions);
            return newCount;
        }
        finally
        {
            if (acquired) mutex.ReleaseMutex();
        }
    }

    // ─── Async I/O (for non-Mutex paths) ─────────────────────────────────

    private async Task<List<TraceSessionEntry>> LoadSessionsAsync(CancellationToken ct)
    {
        if (!File.Exists(_sessionsFilePath))
            return [];

        var json = await File.ReadAllTextAsync(_sessionsFilePath, ct).ConfigureAwait(false);
        var root = JsonSerializer.Deserialize<TraceSessionsRoot>(json, OutputJsonOptions.Shared)
                   ?? new TraceSessionsRoot();
        return root.Sessions;
    }

    private async Task SaveSessionsAsync(List<TraceSessionEntry> sessions, CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_sessionsFilePath)!;
        Directory.CreateDirectory(dir);

        var root = new TraceSessionsRoot { Sessions = sessions };
        var json = JsonSerializer.Serialize(root, OutputJsonOptions.Shared);
        await File.WriteAllTextAsync(_sessionsFilePath, json, ct).ConfigureAwait(false);
    }

    // ─── Sync I/O (used inside Task.Run / Mutex sections) ─────────────────

    private List<TraceSessionEntry> LoadSessionsSync()
    {
        if (!File.Exists(_sessionsFilePath))
            return [];

        var json = File.ReadAllText(_sessionsFilePath);
        var root = JsonSerializer.Deserialize<TraceSessionsRoot>(json, OutputJsonOptions.Shared)
                   ?? new TraceSessionsRoot();
        return root.Sessions;
    }

    private void SaveSessionsSync(List<TraceSessionEntry> sessions)
    {
        var dir = Path.GetDirectoryName(_sessionsFilePath)!;
        Directory.CreateDirectory(dir);

        var root = new TraceSessionsRoot { Sessions = sessions };
        var json = JsonSerializer.Serialize(root, OutputJsonOptions.Shared);
        File.WriteAllText(_sessionsFilePath, json);
    }

    // ─── Mutex naming ──────────────────────────────────────────────────────

    private static string GetMutexName(string sessionId) =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? $@"Global\zapi-cli-trace-{sessionId}"
            : $"zapi-cli-trace-{sessionId}";

    // ─── Inner models ──────────────────────────────────────────────────────

    private sealed class TraceSessionsRoot
    {
        public List<TraceSessionEntry> Sessions { get; set; } = [];
    }
}
