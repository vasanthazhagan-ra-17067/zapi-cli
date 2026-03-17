using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapiCli.Core.Trace;

/// <summary>
/// Reads and filters entries from a session's <c>trace.jsonl</c> file.
/// <para>
/// Export is <strong>non-destructive</strong> — the JSONL file is never modified.
/// </para>
/// </summary>
public sealed class TraceExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly TraceSession _session;

    public TraceExporter(TraceSession session)
    {
        _session = session;
    }

    /// <summary>
    /// Exports all entries for the named session, applying optional filters.
    /// </summary>
    /// <param name="name">Session name.</param>
    /// <param name="truncateBodyBytes">
    ///   When set, <c>request_body</c> and <c>response_body</c> are truncated to this many
    ///   UTF-8 bytes in the returned list. The JSONL file on disk is never modified.
    /// </param>
    /// <param name="typeFilter">
    ///   When set, only entries whose <c>type</c> matches (case-insensitive) are included.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>All matching entries (empty list if the trace file has no entries yet).</returns>
    /// <exception cref="ZapiCliException">
    ///   Thrown with <c>SESSION_NOT_FOUND</c> / exit 1 if the named session does not exist.
    /// </exception>
    public async Task<IReadOnlyList<ApiTraceEntry>> ExportAsync(
        string name,
        int? truncateBodyBytes = null,
        string? typeFilter = null,
        CancellationToken ct = default)
    {
        // Verify the session exists.
        var sessions = await _session.ListSessionsAsync(ct);
        if (!sessions.Any(s => s.Name.Equals(name, StringComparison.Ordinal)))
            throw new ZapiCliException(
                $"Trace session '{name}' not found.",
                ErrorCodes.SessionNotFound,
                exitCode: 1);

        var traceFile = Path.Combine(_session.TracesDir, name, "trace.jsonl");
        if (!File.Exists(traceFile))
            return [];

        // Read all lines (non-destructive — no writes).
        var lines = await File.ReadAllLinesAsync(traceFile, ct);
        var results = new List<ApiTraceEntry>(lines.Length);

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            ApiTraceEntry? entry;
            try
            {
                entry = JsonSerializer.Deserialize<ApiTraceEntry>(line, JsonOptions);
            }
            catch (JsonException)
            {
                // Malformed line — skip silently (crash-safe partial sessions per ADR-0007).
                continue;
            }

            if (entry is null) continue;

            // Apply type filter.
            if (typeFilter is not null &&
                !entry.Type.Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
                continue;

            // Apply body truncation (output only — JSONL is unchanged).
            if (truncateBodyBytes.HasValue && truncateBodyBytes.Value > 0)
            {
                entry = entry with
                {
                    RequestBody = TruncateToBytes(entry.RequestBody, truncateBodyBytes.Value),
                    ResponseBody = TruncateToBytes(entry.ResponseBody, truncateBodyBytes.Value),
                };
            }

            results.Add(entry);
        }

        return results;
    }

    /// <summary>
    /// Truncates <paramref name="value"/> so that its UTF-8 byte representation does not exceed
    /// <paramref name="maxBytes"/> bytes. May produce a partial multi-byte sequence at the
    /// truncation boundary, which is acceptable for trace output.
    /// </summary>
    private static string? TruncateToBytes(string? value, int maxBytes)
    {
        if (value is null) return null;
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        if (bytes.Length <= maxBytes) return value;
        return System.Text.Encoding.UTF8.GetString(bytes, 0, maxBytes);
    }
}
