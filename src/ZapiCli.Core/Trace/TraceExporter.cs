using System.Text;
using System.Text.Json;

namespace ZapiCli.Core.Trace;

/// <summary>
/// Reads trace entries from a session's export file and returns them as a list of
/// <see cref="JsonElement"/> values with optional type-filtering and body truncation.
/// The trace file itself is never modified.
/// </summary>
public sealed class TraceExporter
{
    private readonly ITraceSession _traceSession;

    public TraceExporter(ITraceSession traceSession)
    {
        _traceSession = traceSession;
    }

    /// <summary>
    /// Exports entries from the session identified by <paramref name="sessionId"/> or
    /// <paramref name="sessionName"/>. Exactly one of the two must be provided.
    /// </summary>
    /// <param name="sessionId">UUID — unambiguous primary key lookup.</param>
    /// <param name="sessionName">Name — throws <see cref="ZapiCliException"/> SESSION_AMBIGUOUS if multiple match.</param>
    /// <param name="typeFilter">Optional filter: <c>api</c> or <c>pex</c>.</param>
    /// <param name="truncateBodyChars">
    /// If set, <c>request_body</c> and <c>response_body</c> fields in the returned elements are
    /// truncated to this many characters. The trace file on disk is unchanged.
    /// </param>
    public async Task<IReadOnlyList<JsonElement>> ExportAsync(
        string? sessionId,
        string? sessionName,
        string? typeFilter,
        int? truncateBodyChars,
        CancellationToken ct = default)
    {
        var session = await ResolveSessionAsync(sessionId, sessionName, ct).ConfigureAwait(false);

        if (!File.Exists(session.ExportPath))
            return [];

        var results = new List<JsonElement>();

        foreach (var line in File.ReadLines(session.ExportPath, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            JsonElement element;
            try
            {
                element = JsonSerializer.Deserialize<JsonElement>(line);
            }
            catch (JsonException)
            {
                continue; // Skip malformed lines.
            }

            // Apply type filter.
            if (typeFilter != null)
            {
                if (!element.TryGetProperty("type", out var typeProp))
                    continue;
                if (!typeProp.GetString()!.Equals(typeFilter, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            // Apply body truncation (output only — file unchanged).
            if (truncateBodyChars.HasValue)
                element = TruncateBodyFields(element, truncateBodyChars.Value);

            results.Add(element);
        }

        return results;
    }

    // ─── Private helpers ──────────────────────────────────────────────────

    private async Task<TraceSessionEntry> ResolveSessionAsync(
        string? sessionId,
        string? sessionName,
        CancellationToken ct)
    {
        if (sessionId != null)
        {
            var found = await _traceSession.FindByIdAsync(sessionId, ct).ConfigureAwait(false);
            if (found is null)
                throw new ZapiCliException(
                    $"Session '{sessionId}' not found.",
                    ErrorCodes.SESSION_NOT_FOUND);
            return found;
        }

        if (sessionName != null)
        {
            var matches = await _traceSession.FindByNameAsync(sessionName, ct).ConfigureAwait(false);
            if (matches.Count == 0)
                throw new ZapiCliException(
                    $"Session '{sessionName}' not found.",
                    ErrorCodes.SESSION_NOT_FOUND);
            if (matches.Count > 1)
                throw new ZapiCliException(
                    $"Multiple sessions named '{sessionName}'. Use --id with one of: " +
                    string.Join(", ", matches.Select(m => m.UniqueId)) + ".",
                    ErrorCodes.SESSION_AMBIGUOUS);
            return matches[0];
        }

        throw new ZapiCliException("Either --id or --name is required.", ErrorCodes.INVALID_ARGS);
    }

    /// <summary>
    /// Returns a new <see cref="JsonElement"/> with <c>request_body</c> and
    /// <c>response_body</c> string values truncated to <paramref name="maxChars"/>.
    /// All other properties are preserved as-is.
    /// </summary>
    private static JsonElement TruncateBodyFields(JsonElement element, int maxChars)
    {
        using var ms = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            foreach (var prop in element.EnumerateObject())
            {
                if ((prop.Name == "request_body" || prop.Name == "response_body") &&
                    prop.Value.ValueKind == JsonValueKind.String)
                {
                    var str = prop.Value.GetString()!;
                    writer.WriteString(prop.Name, str.Length > maxChars ? str[..maxChars] : str);
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        ms.Seek(0, System.IO.SeekOrigin.Begin);
        using var doc = JsonDocument.Parse(ms);
        return doc.RootElement.Clone();
    }
}
