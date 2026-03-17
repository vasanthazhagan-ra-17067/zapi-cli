namespace ZapiCli.Core.Api;

/// <summary>
/// Immutable value object containing the raw result of an API call.
/// </summary>
public sealed record ApiResponse
{
    /// <summary>HTTP status code returned by the server.</summary>
    public required int StatusCode { get; init; }

    /// <summary>Raw response body as a UTF-8 string (typically JSON).</summary>
    public required string Body { get; init; }

    /// <summary><c>true</c> when <see cref="StatusCode"/> is in the 2xx range.</summary>
    public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
}
