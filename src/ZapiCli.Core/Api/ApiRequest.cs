namespace ZapiCli.Core.Api;

/// <summary>
/// Describes an outbound API call to a Zoho product endpoint.
/// The caller supplies the full URL on every invocation (ADR-0006 — no base-URL construction).
/// </summary>
public sealed record ApiRequest
{
    /// <summary>Fully qualified endpoint URL (e.g. https://cliq.zoho.com/api/v2/channels).</summary>
    public required string Url { get; init; }

    /// <summary>HTTP method: GET, POST, PUT, PATCH, DELETE.</summary>
    public required string Method { get; init; }

    /// <summary>Optional request body (raw string — typically JSON).</summary>
    public string? Body { get; init; }

    /// <summary>Extra request headers to send alongside the injected Authorization header.</summary>
    public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Additional query parameters to append to the URL.</summary>
    public Dictionary<string, string> QueryParams { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Name of the account whose credentials should be used for this call.</summary>
    public required string AccountName { get; init; }
}
