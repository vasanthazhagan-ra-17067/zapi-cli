using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Auth;
using ZapiCli.Core.Security;
using ZapiCli.Core.Trace;

namespace ZapiCli.Core.Api;

/// <summary>
/// Dispatches authenticated HTTP requests to Zoho product REST APIs.
/// Enforces the compile-time host allowlist (ADR-0004), the ZohoCorp block (ADR-0003),
/// and the automatic token-refresh flow (ADR-0002).
/// </summary>
public sealed class ApiClient
{
    private const string HttpClientName = "zapi-api";

    private readonly IAuthProvider _authProvider;
    private readonly IAccountStore _accountStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITraceWriter _traceWriter;
    private readonly ILogger<ApiClient> _logger;

    public ApiClient(
        IAuthProvider authProvider,
        IAccountStore accountStore,
        IHttpClientFactory httpClientFactory,
        ITraceWriter traceWriter,
        ILogger<ApiClient> logger)
    {
        _authProvider = authProvider;
        _accountStore = accountStore;
        _httpClientFactory = httpClientFactory;
        _traceWriter = traceWriter;
        _logger = logger;
    }

    // ─── Public API ──────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the API call described by <paramref name="request"/>, applying all
    /// security controls and the auto-refresh flow before dispatching HTTP.
    /// Writes a trace entry to the active session (if any) after the call completes.
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

        // Step 3: Parse and validate URL — SSRF check (ADR-0004).
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

        // ─── Trace setup ─────────────────────────────────────────────────────
        var traceTimestamp = DateTimeOffset.UtcNow;
        var traceBaseUrl = uri.GetLeftPart(UriPartial.Authority);
        var traceReqHeaders = BuildTraceRequestHeaders(request, token);

        // Step 7: Send initial request (timed for trace DurationMs).
        var httpClient = _httpClientFactory.CreateClient(HttpClientName);
        var sw = Stopwatch.StartNew();
        var response = await SendAsync(httpClient, request, finalUri, token, ct).ConfigureAwait(false);

        // Step 8: On 401 — auto-refresh token and retry ONCE (ADR-0002).
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogInformation("Received 401 for account '{AccountName}' — refreshing token and retrying.", request.AccountName);
            response.Dispose();

            // Re-fetch account scopes and dc (they may have been updated).
            var refreshedAccount = await _accountStore.FindAsync(request.AccountName, ct).ConfigureAwait(false) ?? account;
            token = await _authProvider.RefreshTokenAsync(request.AccountName, refreshedAccount.Scopes, refreshedAccount.Dc, ct).ConfigureAwait(false);

            // Update trace headers to reflect the refreshed token (will be stripped by TraceWriter).
            traceReqHeaders = BuildTraceRequestHeaders(request, token);

            // DurationMs tracks only the FINAL SendAsync (after any 401 retry) per spec.
            sw.Restart();
            response = await SendAsync(httpClient, request, finalUri, token, ct).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                sw.Stop();
                response.Dispose();

                // Write trace for the failed auth-retry scenario before throwing.
                var authFailEntry = new ApiTraceEntry
                {
                    Timestamp = traceTimestamp,
                    DurationMs = (int)sw.ElapsedMilliseconds,
                    Account = request.AccountName,
                    Method = request.Method.ToUpperInvariant(),
                    BaseUrl = traceBaseUrl,
                    Url = finalUri.ToString(),
                    RequestHeaders = traceReqHeaders,
                    RequestBody = request.Body,
                    ResponseStatus = 401,
                    ResponseHeaders = new Dictionary<string, string>(),
                    Error = "Authentication failed after token refresh.",
                };
                await _traceWriter.AppendApiEntryAsync(authFailEntry, CancellationToken.None).ConfigureAwait(false);

                throw new ZapiCliException(
                    "Authentication failed after token refresh. Run 'zapi-cli account re-auth' to re-authenticate.",
                    ErrorCodes.AUTH_FAILURE,
                    exitCode: 2);
            }
        }

        sw.Stop();

        // Step 9: Capture trace metadata before reading body.
        var traceStatus = (int)response.StatusCode;
        var traceRespHeaders = ExtractResponseHeaders(response);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        // Step 10: Write trace entry — uses CancellationToken.None so a ctrl-C after the call
        // still allows the entry to be flushed.
        var traceEntry = new ApiTraceEntry
        {
            Timestamp = traceTimestamp,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Account = request.AccountName,
            Method = request.Method.ToUpperInvariant(),
            BaseUrl = traceBaseUrl,
            Url = finalUri.ToString(),
            RequestHeaders = traceReqHeaders,
            RequestBody = request.Body,
            ResponseStatus = traceStatus,
            ResponseHeaders = traceRespHeaders,
            ResponseBody = body,
        };
        await _traceWriter.AppendApiEntryAsync(traceEntry, CancellationToken.None).ConfigureAwait(false);

        return new ApiResponse { StatusCode = traceStatus, Body = body };
    }

    // ─── Host validation (ADR-0004) ──────────────────────────────────────────

    /// <summary>
    /// Validates that <paramref name="uri"/>'s host ends with one of the allowed Zoho domain
    /// suffixes. Delegates to <see cref="HostValidator.ValidateHost"/>.
    /// </summary>
    internal static void ValidateHost(Uri uri) => HostValidator.ValidateHost(uri);

    // ─── Private helpers ─────────────────────────────────────────────────────

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

    // ─── Trace helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the outgoing request header dictionary for trace recording.
    /// Includes Authorization (which TraceWriter will strip before writing to disk).
    /// </summary>
    private static Dictionary<string, string> BuildTraceRequestHeaders(ApiRequest request, string token)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = $"Zoho-oauthtoken {token}",
        };

        foreach (var (key, value) in request.Headers)
        {
            if (!key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                headers[key] = value;
        }

        return headers;
    }

    /// <summary>
    /// Collects response headers (both message headers and content headers) into a flat dictionary.
    /// </summary>
    private static Dictionary<string, string> ExtractResponseHeaders(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, values) in response.Headers)
            headers[key] = string.Join(", ", values);
        foreach (var (key, values) in response.Content.Headers)
            headers[key] = string.Join(", ", values);
        return headers;
    }
}
