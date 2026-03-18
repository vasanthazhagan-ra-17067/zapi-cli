using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Auth;
using ZapiCli.Core.Security;

namespace ZapiCli.Core.Api;

/// <summary>
/// Dispatches authenticated HTTP requests to Zoho product REST APIs.
/// Enforces the compile-time host allowlist (ADR-0004), the ZohoCorp block (ADR-0003),
/// and the automatic token-refresh flow (ADR-0002).
/// </summary>
public sealed class ApiClient
{
    // ─── Sealed compile-time allowlist (ADR-0004) ────────────────────────────
    // Not read from config; not overridable at runtime.
    private static readonly string[] AllowedHostSuffixes =
    [
        "zoho.com",
        "zoho.eu",
        "zoho.in",
        "zoho.com.au",
        "zohoapis.com",
        "zohoapis.in",
    ];

    private const string HttpClientName = "zapi-api";

    private readonly IAuthProvider _authProvider;
    private readonly IAccountStore _accountStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ApiClient> _logger;

    public ApiClient(
        IAuthProvider authProvider,
        IAccountStore accountStore,
        IHttpClientFactory httpClientFactory,
        ILogger<ApiClient> logger)
    {
        _authProvider = authProvider;
        _accountStore = accountStore;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the API call described by <paramref name="request"/>, applying all
    /// security controls and the auto-refresh flow before dispatching HTTP.
    /// </summary>
    public async Task<ApiResponse> CallAsync(ApiRequest request, CancellationToken ct = default)
    {
        // Step 1: Load account — must exist before any further checks.
        var account = await _accountStore.FindAsync(request.AccountName, ct).ConfigureAwait(false)
            ?? throw new ZapiCliException(
                $"Account '{request.AccountName}' not found.",
                ErrorCodes.ACCOUNT_NOT_FOUND);

        // Step 2: ZohoCorp domain block (ADR-0003) — checked before any HTTP dispatch.
        ZohoCorpGuard.AssertNotZohoCorp(account.Email);

        // Step 3: NeedsReauth pre-refresh — refresh BEFORE the request, not in response to 401.
        if (account.NeedsReauth)
        {
            _logger.LogInformation("Account '{AccountName}' has NeedsReauth=true — refreshing token.", request.AccountName);
            await _authProvider.RefreshTokenAsync(request.AccountName, account.Scopes, account.Dc, ct).ConfigureAwait(false);
            await ClearNeedsReauthAsync(request.AccountName, ct).ConfigureAwait(false);
        }

        // Step 4: Parse and validate URL — SSRF check (ADR-0004).
        Uri uri;
        try
        {
            uri = new Uri(request.Url);
        }
        catch (UriFormatException ex)
        {
            throw new ZapiCliException($"Invalid URL: {ex.Message}", ErrorCodes.INVALID_ARGS);
        }

        ValidateHost(uri);

        // Step 5: Retrieve current access token from keychain.
        var token = await _authProvider.GetTokenAsync(request.AccountName, ct).ConfigureAwait(false);

        // Step 6: Build URI with additional query parameters.
        var finalUri = BuildUriWithQueryParams(uri, request.QueryParams);

        // Step 7: Send initial request.
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var response = await SendAsync(httpClient, request, finalUri, token, ct).ConfigureAwait(false);

        // Step 8: On 401 — auto-refresh token and retry ONCE (ADR-0002).
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogInformation("Received 401 for account '{AccountName}' — refreshing token and retrying.", request.AccountName);
            response.Dispose();

            // Re-fetch account scopes and dc (they may have been updated).
            var refreshedAccount = await _accountStore.FindAsync(request.AccountName, ct).ConfigureAwait(false) ?? account;
            token = await _authProvider.RefreshTokenAsync(request.AccountName, refreshedAccount.Scopes, refreshedAccount.Dc, ct).ConfigureAwait(false);

            response = await SendAsync(httpClient, request, finalUri, token, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                response.Dispose();
                throw new ZapiCliException(
                    "Authentication failed after token refresh. Run 'zapi-cli account re-auth' to re-authenticate.",
                    ErrorCodes.AUTH_FAILURE,
                    exitCode: 2);
            }
        }

        // Step 9: Read body and return.
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new ApiResponse { StatusCode = (int)response.StatusCode, Body = body };
    }

    // ─── Host validation (ADR-0004) ──────────────────────────────────────────

    /// <summary>
    /// Validates that <paramref name="uri"/>'s host ends with one of the allowed Zoho domain
    /// suffixes using proper boundary matching (prevents 'evilzoho.com' from matching 'zoho.com').
    /// </summary>
    internal static void ValidateHost(Uri uri)
    {
        var host = uri.Host;
        foreach (var suffix in AllowedHostSuffixes)
        {
            if (host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith("." + suffix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        throw new ZapiCliException(
            "Target host is not in the allowed Zoho domain list.",
            ErrorCodes.HOST_NOT_ALLOWED,
            exitCode: 1);
    }

    // ─── Private helpers ─────────────────────────────────────────────────────

    private async Task ClearNeedsReauthAsync(string accountName, CancellationToken ct)
    {
        var root = await _accountStore.LoadAsync(ct).ConfigureAwait(false);
        var idx = root.Accounts.FindIndex(a =>
            a.Name.Equals(accountName, StringComparison.Ordinal));

        if (idx >= 0)
        {
            root.Accounts[idx] = root.Accounts[idx] with { NeedsReauth = false };
            await _accountStore.SaveAsync(root, ct).ConfigureAwait(false);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient httpClient,
        ApiRequest request,
        Uri finalUri,
        string token,
        CancellationToken ct)
    {
        using var httpRequest = BuildRequestMessage(request, finalUri, token);
        // HttpCompletionOption.ResponseHeadersRead is not used because we need the full body.
        return await httpClient.SendAsync(httpRequest, ct).ConfigureAwait(false);
    }

    private static HttpRequestMessage BuildRequestMessage(ApiRequest request, Uri finalUri, string token)
    {
        var method = new HttpMethod(request.Method.ToUpperInvariant());
        var httpRequest = new HttpRequestMessage(method, finalUri);

        // Authorization — injected by ApiClient only; command classes never set this (ADR-0008).
        // NOTE: Never include this header in trace entries (Story 8 invariant).
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Zoho-oauthtoken {token}");

        // Body content.
        if (request.Body is not null)
        {
            // Caller may override Content-Type via --header; otherwise default to application/json.
            var mediaType = request.Headers.TryGetValue("Content-Type", out var ct) ? ct : "application/json";
            httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, mediaType);
        }

        // Extra headers — skip Authorization (security) and Content-Type (already applied above).
        foreach (var (key, value) in request.Headers)
        {
            if (key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                continue;
            if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                continue;
            httpRequest.Headers.TryAddWithoutValidation(key, value);
        }

        return httpRequest;
    }

    private static Uri BuildUriWithQueryParams(Uri baseUri, Dictionary<string, string> queryParams)
    {
        if (queryParams.Count == 0)
            return baseUri;

        var builder = new UriBuilder(baseUri);
        var existingQuery = string.IsNullOrEmpty(builder.Query)
            ? ""
            : builder.Query.TrimStart('?') + "&";

        var additionalParams = string.Join("&", queryParams.Select(
            kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        builder.Query = existingQuery + additionalParams;
        return builder.Uri;
    }
}
