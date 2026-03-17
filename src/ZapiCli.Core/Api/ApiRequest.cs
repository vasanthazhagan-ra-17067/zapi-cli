namespace ZapiCli.Core.Api;

/// <summary>
/// Immutable value object describing an outgoing API call.
/// Assembled by the command layer; executed by <see cref="ApiClient"/>.
/// </summary>
public sealed record ApiRequest
{
    /// <summary>Caller-supplied base URL, e.g. <c>https://cliq.zoho.com/api/v2</c>.</summary>
    public required string BaseUrl { get; init; }

    /// <summary>HTTP method: GET, POST, PUT, PATCH, or DELETE.</summary>
    public required string Method { get; init; }

    /// <summary>Path to append to <see cref="BaseUrl"/>, e.g. <c>/channels</c>.</summary>
    public required string Path { get; init; }

    /// <summary>Optional JSON request body.</summary>
    public string? Body { get; init; }

    /// <summary>
    /// Additional HTTP headers to include (excluding Authorization, which is injected by
    /// <see cref="ApiClient"/>).
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>();

    /// <summary>Query parameters to append to the final URL.</summary>
    public IReadOnlyDictionary<string, string> QueryParams { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// Account name to authenticate with. When <c>null</c> or empty the default account
    /// is resolved by <see cref="ApiClient"/>.
    /// </summary>
    public string? AccountName { get; init; }
}
