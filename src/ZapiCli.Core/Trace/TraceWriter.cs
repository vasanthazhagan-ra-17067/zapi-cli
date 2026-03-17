using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ZapiCli.Core.Trace;

/// <summary>
/// Appends <see cref="ApiTraceEntry"/> records to the active session's <c>trace.jsonl</c> file.
/// <para>
/// Key invariants (ADR-0007):
/// <list type="bullet">
///   <item>The <c>authorization</c> request header is <strong>unconditionally excluded</strong>
///         before any serialisation — regardless of what the caller passes in.</item>
///   <item>If no session is active, the entry is silently dropped.</item>
///   <item>Any write failure is caught, logged at <c>Warning</c>, and <strong>never
///         re-thrown</strong> — the calling <c>api call</c> must always complete.</item>
/// </list>
/// </para>
/// </summary>
public sealed class TraceWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly ILogger<TraceWriter> _logger;
    private readonly TraceSession _session;

    public TraceWriter(ILogger<TraceWriter> logger, TraceSession session)
    {
        _logger = logger;
        _session = session;
    }

    /// <summary>
    /// Appends <paramref name="entry"/> to the active session's trace file.
    /// <para>
    /// The method fills <c>Session</c> and <c>Seq</c> from the active session — the caller
    /// does not need to populate those fields. <c>Authorization</c> is stripped from
    /// <c>RequestHeaders</c> unconditionally.
    /// </para>
    /// </summary>
    public async Task AppendApiEntryAsync(ApiTraceEntry entry)
    {
        try
        {
            // 1. Check whether a session is currently active; silently drop if not.
            var activeSession = await _session.GetActiveSessionAsync();
            if (activeSession is null) return;

            // 2. Atomically acquire the next sequence number.
            var seq = await _session.IncrementEntryCountAsync(activeSession.Name);
            if (seq < 0) return; // Mutex timeout — already logged by TraceSession.

            // 3. Fill in session-managed fields.
            entry = entry with { Session = activeSession.Name, Seq = seq };

            // 4. Unconditionally strip the Authorization header (ADR-0007 security requirement).
            //    The comparison is case-insensitive to catch "Authorization", "authorization", etc.
            var sanitized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in entry.RequestHeaders)
            {
                if (!k.Equals("authorization", StringComparison.OrdinalIgnoreCase))
                    sanitized[k] = v;
            }
            entry = entry with { RequestHeaders = sanitized };

            // 5. Serialise as a single JSONL line.
            var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;

            // 6. Append to trace.jsonl.
            var traceFile = Path.Combine(_session.TracesDir, activeSession.Name, "trace.jsonl");
            await File.AppendAllTextAsync(traceFile, line);
        }
        catch (Exception ex)
        {
            // Trace write failures must NEVER propagate to the caller (ADR-0007).
            _logger.LogWarning(ex, "TraceWriter failed to append entry; trace entry silently dropped.");
        }
    }
}
