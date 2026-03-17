using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ZapiCli.Core.Trace;

// ─── Data types for sessions.json ─────────────────────────────────────────────

public sealed record TraceSessionEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("start_time")]
    public DateTimeOffset StartTime { get; init; }

    [JsonPropertyName("entry_count")]
    public int EntryCount { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "active";
}

internal sealed class SessionsRoot
{
    [JsonPropertyName("active_session")]
    public string? ActiveSession { get; set; }

    [JsonPropertyName("sessions")]
    public List<TraceSessionEntry> Sessions { get; set; } = [];
}

// ─── TraceSession ──────────────────────────────────────────────────────────────

/// <summary>
/// Manages the named-session index at <c>&lt;configDir&gt;/traces/sessions.json</c>.
/// </summary>
public sealed class TraceSession
{
    // Session names are restricted to safe filesystem characters (OQ-006).
    private static readonly Regex ValidNameRegex =
        new(@"^[a-zA-Z0-9_.\-]{1,64}$", RegexOptions.Compiled);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<TraceSession> _logger;

    /// <summary>Root zapi-cli config directory (e.g. ~/.config/zapi-cli).</summary>
    internal string ConfigDir { get; }

    /// <summary>Directory that holds sessions.json and per-session sub-dirs.</summary>
    internal string TracesDir => Path.Combine(ConfigDir, "traces");

    private string SessionsFilePath => Path.Combine(TracesDir, "sessions.json");

    public TraceSession(ILogger<TraceSession> logger)
        : this(logger, ResolveConfigDir()) { }

    /// <summary>Internal constructor that accepts an explicit config dir for unit testing.</summary>
    internal TraceSession(ILogger<TraceSession> logger, string configDir)
    {
        _logger = logger;
        ConfigDir = configDir;
    }

    private static string ResolveConfigDir() =>
        OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "zapi-cli")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "zapi-cli");

    private static void ValidateName(string name)
    {
        if (!ValidNameRegex.IsMatch(name))
            throw new ZapiCliException(
                $"Session name '{name}' is invalid. Only [a-zA-Z0-9_.-] characters are allowed, max 64 characters.",
                ErrorCodes.InvalidArgs,
                exitCode: 1);
    }

    // ─── Mutex helper ─────────────────────────────────────────────────────────

    /// <summary>
    /// Derives a short, deterministic, cross-process Mutex name from a session name (OQ-008).
    /// The name is kept short (≤ 17 chars) to stay within the macOS sem_open limit of 31 chars.
    /// </summary>
    private static string MakeMutexName(string sessionName)
    {
        // Use a simple deterministic checksum (not GetHashCode — that is not stable across processes).
        uint checksum = 5381;
        foreach (char c in sessionName)
            checksum = checksum * 33 + c;
        return $"zapi-{checksum:x8}"; // exactly 13 chars
    }

    // ─── Sync helpers used inside Task.Run (for Mutex section) ───────────────

    private SessionsRoot LoadSync()
    {
        if (!File.Exists(SessionsFilePath))
            return new SessionsRoot();
        var json = File.ReadAllText(SessionsFilePath);
        return JsonSerializer.Deserialize<SessionsRoot>(json, JsonOptions) ?? new SessionsRoot();
    }

    private void SaveSync(SessionsRoot root)
    {
        Directory.CreateDirectory(TracesDir);
        File.WriteAllText(SessionsFilePath, JsonSerializer.Serialize(root, JsonOptions));
    }

    // ─── Async I/O helpers ────────────────────────────────────────────────────

    private async Task<SessionsRoot> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(SessionsFilePath))
            return new SessionsRoot();
        var json = await File.ReadAllTextAsync(SessionsFilePath, ct);
        return JsonSerializer.Deserialize<SessionsRoot>(json, JsonOptions) ?? new SessionsRoot();
    }

    private async Task SaveAsync(SessionsRoot root, CancellationToken ct)
    {
        Directory.CreateDirectory(TracesDir);
        await File.WriteAllTextAsync(SessionsFilePath, JsonSerializer.Serialize(root, JsonOptions), ct);
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>Returns the currently active session, or <c>null</c> if no session is active.</summary>
    public async Task<TraceSessionEntry?> GetActiveSessionAsync(CancellationToken ct = default)
    {
        var root = await LoadAsync(ct);
        if (root.ActiveSession is null) return null;
        return root.Sessions.Find(s => s.Name == root.ActiveSession);
    }

    /// <summary>
    /// Starts a new named session, replacing the active-session pointer.
    /// If a session with the same name already exists its entry is reset.
    /// Old session data (JSONL) is preserved on disk.
    /// </summary>
    public async Task<TraceSessionEntry> StartSessionAsync(string name, CancellationToken ct = default)
    {
        ValidateName(name);

        var root = await LoadAsync(ct);

        var entry = new TraceSessionEntry
        {
            Name = name,
            StartTime = DateTimeOffset.UtcNow,
            EntryCount = 0,
            Status = "active",
        };

        var idx = root.Sessions.FindIndex(s => s.Name == name);
        if (idx >= 0)
            root.Sessions[idx] = entry;
        else
            root.Sessions.Add(entry);

        root.ActiveSession = name;
        await SaveAsync(root, ct);

        // Ensure the trace sub-directory and an empty trace.jsonl exist.
        var sessionDir = Path.Combine(TracesDir, name);
        Directory.CreateDirectory(sessionDir);
        var traceFile = Path.Combine(sessionDir, "trace.jsonl");
        if (!File.Exists(traceFile))
            await File.WriteAllTextAsync(traceFile, string.Empty, ct);

        _logger.LogInformation("Trace session '{Name}' started.", name);
        return entry;
    }

    /// <summary>Marks the named session as closed. JSONL is preserved.</summary>
    public async Task<TraceSessionEntry> CloseSessionAsync(string name, CancellationToken ct = default)
    {
        var root = await LoadAsync(ct);
        var idx = root.Sessions.FindIndex(s => s.Name == name);
        if (idx < 0)
            throw new ZapiCliException(
                $"Trace session '{name}' not found.",
                ErrorCodes.SessionNotFound,
                exitCode: 1);

        var updated = root.Sessions[idx] with { Status = "closed" };
        root.Sessions[idx] = updated;

        if (root.ActiveSession == name)
            root.ActiveSession = null;

        await SaveAsync(root, ct);
        return updated;
    }

    /// <summary>Removes the session entry from sessions.json and deletes the trace directory.</summary>
    public async Task RemoveSessionAsync(string name, CancellationToken ct = default)
    {
        var root = await LoadAsync(ct);
        var idx = root.Sessions.FindIndex(s => s.Name == name);
        if (idx < 0)
            throw new ZapiCliException(
                $"Trace session '{name}' not found.",
                ErrorCodes.SessionNotFound,
                exitCode: 1);

        root.Sessions.RemoveAt(idx);
        if (root.ActiveSession == name)
            root.ActiveSession = null;

        await SaveAsync(root, ct);

        var sessionDir = Path.Combine(TracesDir, name);
        if (Directory.Exists(sessionDir))
            Directory.Delete(sessionDir, recursive: true);
    }

    /// <summary>Returns all sessions in the index (empty list if none).</summary>
    public async Task<IReadOnlyList<TraceSessionEntry>> ListSessionsAsync(CancellationToken ct = default)
    {
        var root = await LoadAsync(ct);
        return root.Sessions.AsReadOnly();
    }

    /// <summary>
    /// Atomically increments the entry count for <paramref name="name"/> and returns the new
    /// (1-based) sequence number. Uses a named Mutex to ensure correctness under concurrent
    /// processes (OQ-008). Returns ‑1 on Mutex timeout (2 000 ms) or if the session is not found.
    /// </summary>
    public Task<int> IncrementEntryCountAsync(string name, CancellationToken ct = default)
    {
        var mutexName = MakeMutexName(name);
        return Task.Run(() =>
        {
            using var mutex = new Mutex(initiallyOwned: false, mutexName);
            if (!mutex.WaitOne(millisecondsTimeout: 2000))
            {
                _logger.LogWarning(
                    "Mutex wait timeout for session '{Name}'; trace entry dropped.", name);
                return -1;
            }
            try
            {
                var root = LoadSync();
                var idx = root.Sessions.FindIndex(s => s.Name == name);
                if (idx < 0) return -1;

                var newCount = root.Sessions[idx].EntryCount + 1;
                root.Sessions[idx] = root.Sessions[idx] with { EntryCount = newCount };
                SaveSync(root);
                return newCount;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }, ct);
    }
}
