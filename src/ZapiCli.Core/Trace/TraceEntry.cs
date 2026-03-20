namespace ZapiCli.Core.Trace;

/// <summary>
/// Represents a single API call trace entry written as one JSON line to the trace file.
/// Session and SessionId are populated by <see cref="TraceWriter"/> from the active session;
/// ApiClient sets all other fields. Seq is also assigned by TraceWriter via IncrementEntryCountAsync.
/// </summary>
public sealed record ApiTraceEntry
{
    public int Seq { get; init; }
    public string Type { get; init; } = "api";

    // Populated by TraceWriter, not by ApiClient.
    public string Session { get; init; } = "";
    public string SessionId { get; init; } = "";

    public required DateTimeOffset Timestamp { get; init; }
    public required int DurationMs { get; init; }
    public required string Account { get; init; }
    public required string Method { get; init; }

    /// <summary>Scheme + host + port only (uri.GetLeftPart(UriPartial.Authority)).</summary>
    public required string BaseUrl { get; init; }

    /// <summary>Full URL as dispatched.</summary>
    public required string Url { get; init; }

    /// <summary>
    /// Outgoing request headers. Authorization, Cookie, X-Auth-Token, X-Api-Key are stripped
    /// by <see cref="TraceWriter"/> before writing to disk.
    /// </summary>
    public required Dictionary<string, string> RequestHeaders { get; init; }

    public string? RequestBody { get; init; }
    public required int ResponseStatus { get; init; }

    /// <summary>
    /// Response headers. Set-Cookie and WWW-Authenticate are stripped by <see cref="TraceWriter"/>.
    /// </summary>
    public required Dictionary<string, string> ResponseHeaders { get; init; }

    public string? ResponseBody { get; init; }

    /// <summary>Set when a transport-level exception prevented a response from being received.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Represents a Pex real-time event trace entry, written alongside API trace entries.
/// </summary>
public sealed record PexTraceEntry
{
    public int Seq { get; init; }
    public string Type { get; init; } = "pex";

    // Populated by TraceWriter.
    public string Session { get; init; } = "";
    public string SessionId { get; init; } = "";

    public required DateTimeOffset Timestamp { get; init; }
    public required string Account { get; init; }
    public int? RelatedApiSeq { get; init; }
    public required string HandlerClass { get; init; }
    public required string CallbackMethod { get; init; }
    public required string Payload { get; init; }
}
