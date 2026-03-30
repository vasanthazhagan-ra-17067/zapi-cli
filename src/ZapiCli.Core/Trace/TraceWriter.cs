using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ZapiCli.Core.Trace;

/// <summary>
/// Appends <see cref="ApiTraceEntry"/> and <see cref="PexTraceEntry"/> records as JSONL
/// to the active session's export file with security header exclusion.
/// </summary>
public sealed class TraceWriter : ITraceWriter
{
    // Security headers to strip from request headers before writing to disk.
    private static readonly HashSet<string> RequestHeadersToStrip =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Cookie",
            "X-Auth-Token",
            "X-Api-Key",
        };

    // Security headers to strip from response headers before writing to disk.
    private static readonly HashSet<string> ResponseHeadersToStrip =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Set-Cookie",
            "WWW-Authenticate",
        };

    private readonly ITraceSession _traceSession;
    private readonly ILogger<TraceWriter> _logger;

    public TraceWriter(ITraceSession traceSession, ILogger<TraceWriter> logger)
    {
        _traceSession = traceSession;
        _logger = logger;
    }

    public async Task AppendApiEntryAsync(ApiTraceEntry entry, CancellationToken ct = default)
    {
        try
        {
            // Step 1: Get active session — if none, return silently.
            var session = await _traceSession.GetActiveSessionAsync(ct).ConfigureAwait(false);
            if (session is null)
                return;

            // Step 2: Defensive status check (handles races between GetActive and write).
            if (session.Status == "closing")
            {
                _logger.LogWarning(
                    "Session {SessionId} is closing; trace entry dropped silently.", session.UniqueId);
                return;
            }
            if (session.Status == "closed")
                return;

            // Step 3: Obtain monotonic seq under the UUID-keyed Mutex.
            var seq = await _traceSession.IncrementEntryCountAsync(session.UniqueId, ct)
                .ConfigureAwait(false);
            if (seq <= 0)
            {
                _logger.LogWarning(
                    "Mutex timeout for session {SessionId}; trace entry dropped silently.", session.UniqueId);
                return;
            }

            // Step 4: Build safe entry — strip security headers; fill in Session/SessionId/Seq.
            var safeEntry = entry with
            {
                Seq = seq,
                Session = session.Name,
                SessionId = session.UniqueId,
                RequestHeaders = entry.RequestHeaders
                    .Where(kv => !RequestHeadersToStrip.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
                ResponseHeaders = entry.ResponseHeaders
                    .Where(kv => !ResponseHeadersToStrip.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            };

            // Step 5: Serialize to one JSON line.
            var line = JsonSerializer.Serialize(safeEntry, OutputJsonOptions.Shared);

            // Step 6: Ensure the export directory exists.
            var dir = Path.GetDirectoryName(session.ExportPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Step 7: Append to the export file (UTF-8 no BOM, FileMode.Append).
            await File.AppendAllTextAsync(
                session.ExportPath,
                line + "\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                ct).ConfigureAwait(false);

            // Step 8: Restrict trace file to owner only — trace entries may contain
            // full API response bodies including PII. Idempotent; safe to call on every append.
            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(session.ExportPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                catch (PlatformNotSupportedException) { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append API trace entry; entry silently dropped.");
        }
    }

    public async Task AppendPexEntryAsync(PexTraceEntry entry, CancellationToken ct = default)
    {
        try
        {
            var session = await _traceSession.GetActiveSessionAsync(ct).ConfigureAwait(false);
            if (session is null)
                return;

            if (session.Status == "closing")
            {
                _logger.LogWarning(
                    "Session {SessionId} is closing; pex trace entry dropped silently.", session.UniqueId);
                return;
            }
            if (session.Status == "closed")
                return;

            var seq = await _traceSession.IncrementEntryCountAsync(session.UniqueId, ct)
                .ConfigureAwait(false);
            if (seq <= 0)
            {
                _logger.LogWarning(
                    "Mutex timeout for session {SessionId}; pex trace entry dropped silently.", session.UniqueId);
                return;
            }

            var safeEntry = entry with
            {
                Seq = seq,
                Session = session.Name,
                SessionId = session.UniqueId,
            };

            var line = JsonSerializer.Serialize(safeEntry, OutputJsonOptions.Shared);

            var dir = Path.GetDirectoryName(session.ExportPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            await File.AppendAllTextAsync(
                session.ExportPath,
                line + "\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                ct).ConfigureAwait(false);

            if (!OperatingSystem.IsWindows())
            {
                try { File.SetUnixFileMode(session.ExportPath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                catch (PlatformNotSupportedException) { }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to append Pex trace entry; entry silently dropped.");
        }
    }
}
