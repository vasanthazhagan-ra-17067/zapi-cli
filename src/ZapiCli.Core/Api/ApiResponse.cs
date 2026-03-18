namespace ZapiCli.Core.Api;

/// <summary>
/// The result of an outbound Zoho API call.
/// The raw server response body is passed through untransformed (ADR-0008 WriteRaw contract).
/// </summary>
public sealed record ApiResponse
{
    /// <summary>HTTP status code returned by the Zoho API server.</summary>
    public int StatusCode { get; init; }

    /// <summary>Raw response body exactly as returned by the server (no re-serialization).</summary>
    public required string Body { get; init; }

    /// <summary><c>true</c> when <see cref="StatusCode"/> is in the 200-299 range.</summary>
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}
