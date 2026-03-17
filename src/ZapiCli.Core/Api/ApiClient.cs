using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Logging;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Auth;
using ZapiCli.Core.Security;
using ZapiCli.Core.Trace;

namespace ZapiCli.Core.Api;

/// <summary>
/// Dispatches authenticated HTTP requests to Zoho product APIs.
/// <para>
/// Security call order (ADR-0003, ADR-0005, ADR-0006):
/// <list type="number">
///   <item>Load account entry.</item>
///   <item>ZohoCorp block check (second enforcement point, ADR-0005).</item>
///   <item>NeedsReauth check.</item>
///   <item>Assemble final URL (base-url + path, normalise leading '/').</item>
///   <item><strong>Host allowlist check — fires BEFORE any token read.</strong></item>
///   <item>Inject Authorization header (GetTokenAsync).</item>
///   <item>Dispatch HttpClient.SendAsync.</item>
///   <item>Return ApiResponse (or throw API_ERROR on non-2xx).</item>
/// </list>
/// </para>
/// </summary>
public sealed class ApiClient
{
    // ─── Compile-time host allowlist (ADR-0006) ────────────────────────────
    private static readonly IReadOnlyList<string> AllowedHostSuffixes =
    [
        ".zoho.com",
        ".zoho.eu",
        ".zoho.in",
        ".zoho.com.au",
        ".zohoapis.com",
        ".zohoapis.in",
    ];

    private readonly HttpClient _http;
    private readonly IAuthProvider _authProvider;
    private readonly IAccountStore _accountStore;
    private readonly ILogger<ApiClient> _logger;
    private readonly TraceWriter? _traceWriter;

    public ApiClient(
        HttpClient http,
        IAuthProvider authProvider,
        IAccountStore accountStore,
        ILogger<ApiClient> logger,
        TraceWriter? traceWriter = null)
    {
        _http = http;
        _authProvider = authProvider;
        _accountStore = accountStore;
        _logger = logger;
        _traceWriter = traceWriter;
    }

    /// <summary>
    /// Executes the API call described by <paramref name="request"/> and returns the response.
    /// Throws <see cref="ZapiCliException"/> on security violations or non-2xx responses.
    /// </summary>
    public async Task<ApiResponse> CallAsync(ApiRequest request, CancellationToken ct = default)
    {
        // Step 1 — Resolve account.
        AccountEntry account;
        if (string.IsNullOrWhiteSpace(request.AccountName))
            account = await _accountStore.GetDefaultAsync(ct);
        else
        {
            account = await _accountStore.FindAsync(request.AccountName, ct)
                ?? throw new ZapiCliException(
                    $"Account '{request.AccountName}' not found.",
                    ErrorCodes.AccountNotFound,
                    exitCode: 1);
        }

        // Step 2 — ZohoCorp block (second enforcement point, ADR-0005).
        ZohoCorpGuard.AssertNotZohoCorp(account.Email);

        // Step 3 — Reauth check.
        if (account.NeedsReauth)
            throw new ZapiCliException(
                $"Account '{account.Name}' requires re-authentication. Run 'zapi-cli account re-auth --name {account.Name}'.",
                ErrorCodes.NeedsReauth,
                exitCode: 2);

        // Step 4 — Assemble final URL.
        var normalizedPath = request.Path.StartsWith('/') ? request.Path : '/' + request.Path;
        var rawUrl = request.BaseUrl.TrimEnd('/') + normalizedPath;

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            throw new ZapiCliException(
                $"Malformed URL assembled from base-url '{request.BaseUrl}' and path '{request.Path}'.",
                ErrorCodes.InvalidArgs,
                exitCode: 1);

        // Append query parameters (before host check so the full URI is validated).
        if (request.QueryParams.Count > 0)
        {
            var query = string.Join("&", request.QueryParams.Select(
                kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));
            var builder = new UriBuilder(uri) { Query = query };
            uri = builder.Uri;
        }

        // Step 5 — Host allowlist check: MUST fire before any token read (ADR-0006).
        if (!IsHostAllowed(uri))
            throw new ZapiCliException(
                $"Host '{uri.Host}' is not on the Zoho host allowlist. " +
                "Only Zoho product API hosts are permitted.",
                ErrorCodes.HostNotAllowed,
                exitCode: 1);

        // Step 6 — Read token (only after host is confirmed safe).
        var token = await _authProvider.GetTokenAsync(account.Name, ct);

        // Step 7 — Build and dispatch the HTTP request.
        using var httpRequest = new HttpRequestMessage(new HttpMethod(request.Method), uri);

        httpRequest.Headers.Authorization =
            new AuthenticationHeaderValue("Zoho-oauthtoken", token);

        foreach (var (key, value) in request.Headers)
            httpRequest.Headers.TryAddWithoutValidation(key, value);

        var method = request.Method.ToUpperInvariant();
        if (request.Body is not null)
        {
            httpRequest.Content = new StringContent(request.Body, Encoding.UTF8, "application/json");
        }
        else if (method is "POST" or "PUT" or "PATCH")
        {
            // Inject empty JSON body with content-type so the server doesn't reject the request.
            httpRequest.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        }

        _logger.LogDebug("Dispatching {Method} {Uri}", method, uri);

        // Capture start time and begin the stopwatch before the HTTP call.
        var callStart = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();

        using var response = await _http.SendAsync(httpRequest, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        sw.Stop();

        // Trace the call (success or HTTP error) — ADR-0007.
        // TraceWriter handles its own exceptions and never re-throws.
        if (_traceWriter is not null)
        {
            var responseHeaders = CollectHeaders(response.Headers, response.Content.Headers);
            await _traceWriter.AppendApiEntryAsync(new ApiTraceEntry
            {
                Timestamp = callStart,
                DurationMs = sw.ElapsedMilliseconds,
                Account = account.Name,
                Method = method,
                BaseUrl = request.BaseUrl,
                Url = uri.ToString(),
                RequestHeaders = new Dictionary<string, string>(request.Headers),
                RequestBody = request.Body,
                ResponseStatus = (int)response.StatusCode,
                ResponseHeaders = responseHeaders,
                ResponseBody = body,
                Error = response.IsSuccessStatusCode
                    ? null
                    : $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
            });
        }

        // Step 8 — Return or throw.
        if (!response.IsSuccessStatusCode)
            throw new ZapiCliException(
                $"Zoho API returned {(int)response.StatusCode} {response.ReasonPhrase}.",
                ErrorCodes.ApiError,
                exitCode: 1)
            {
                Detail = body,
            };

        return new ApiResponse
        {
            StatusCode = (int)response.StatusCode,
            Body = body,
        };
    }

    private static Dictionary<string, string> CollectHeaders(
        HttpResponseHeaders headers,
        HttpContentHeaders contentHeaders)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in headers)
            dict[h.Key] = string.Join(", ", h.Value);
        foreach (var h in contentHeaders)
            dict[h.Key] = string.Join(", ", h.Value);
        return dict;
    }

    // ─── Host allowlist (ADR-0006) ─────────────────────────────────────────
    private static bool IsHostAllowed(Uri uri) =>
        AllowedHostSuffixes.Any(suffix =>
            uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
