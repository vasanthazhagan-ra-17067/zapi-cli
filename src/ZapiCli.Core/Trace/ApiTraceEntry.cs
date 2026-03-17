using System.Text.Json.Serialization;

namespace ZapiCli.Core.Trace;

/// <summary>
/// Represents a single API call record in an append-only JSONL trace file.
/// <para>
/// The <c>authorization</c> key is <strong>always excluded</strong> from
/// <see cref="RequestHeaders"/> by <see cref="TraceWriter"/> before serialisation.
/// </para>
/// </summary>
public sealed record ApiTraceEntry
{
    [JsonPropertyName("seq")]
    public int Seq { get; init; }

    [JsonPropertyName("type")]
    public string Type { get; init; } = "api";

    [JsonPropertyName("session")]
    public string Session { get; init; } = "";

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; }

    [JsonPropertyName("duration_ms")]
    public long DurationMs { get; init; }

    [JsonPropertyName("account")]
    public string Account { get; init; } = "";

    [JsonPropertyName("method")]
    public string Method { get; init; } = "";

    [JsonPropertyName("base_url")]
    public string BaseUrl { get; init; } = "";

    [JsonPropertyName("url")]
    public string Url { get; init; } = "";

    [JsonPropertyName("request_headers")]
    public Dictionary<string, string> RequestHeaders { get; init; } = new();

    [JsonPropertyName("request_body")]
    public string? RequestBody { get; init; }

    [JsonPropertyName("response_status")]
    public int? ResponseStatus { get; init; }

    [JsonPropertyName("response_headers")]
    public Dictionary<string, string> ResponseHeaders { get; init; } = new();

    [JsonPropertyName("response_body")]
    public string? ResponseBody { get; init; }

    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
