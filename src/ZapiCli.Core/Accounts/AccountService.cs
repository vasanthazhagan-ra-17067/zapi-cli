using System.Text.Json;
using Microsoft.Extensions.Logging;
using ZapiCli.Core.Auth;
using ZapiCli.Core.Security;

namespace ZapiCli.Core.Accounts;

/// <summary>
/// Implements all account subcommand operations (ADR-0002, ADR-0003).
/// All security checks run here before any credential or state mutation.
/// </summary>
public sealed class AccountService : IAccountService
{
    private readonly IAccountStore _accountStore;
    private readonly IAuthProvider _authProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AccountService> _logger;
    private readonly IOAuthBrowserFlow _browserFlow;

    public AccountService(
        IAccountStore accountStore,
        IAuthProvider authProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<AccountService> logger,
        IOAuthBrowserFlow browserFlow)
    {
        _accountStore = accountStore;
        _authProvider = authProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _browserFlow = browserFlow;
    }

    // ─── AddAccountAsync ──────────────────────────────────────────────────────

    public async Task<(string Name, string Dc)> AddAccountAsync(
        string name,
        string code,
        string redirectUri,
        string clientId,
        string clientSecret,
        string dc,
        IEnumerable<string> scopes,
        CancellationToken ct = default)
    {
        // Uniqueness check — abort early before any network call.
        var existing = await _accountStore.FindAsync(name, ct).ConfigureAwait(false);
        if (existing is not null)
            throw new ZapiCliException(
                $"Account '{name}' already exists. Use 'account remove' first to replace it.",
                ErrorCodes.ACCOUNT_ALREADY_EXISTS,
                exitCode: 1);

        return await ExchangeAndFinalizeAsync(name, code, redirectUri, clientId, clientSecret, dc, scopes, ct)
            .ConfigureAwait(false);
    }

    // ─── LoginAsync ───────────────────────────────────────────────────────────

    public Task<(string Name, string Dc)> LoginAsync(
        string name,
        string clientId,
        string clientSecret,
        string[] scopes,
        string dc,
        int callbackPort = 8085,
        CancellationToken ct = default) =>
        LoginAsync(name, clientId, clientSecret, scopes, dc, callbackPort, () => new LocalCallbackServer(callbackPort), ct);

    /// <summary>
    /// Internal overload that accepts a <paramref name="serverFactory"/> — used by tests to inject
    /// a fake callback server that returns preset code+state without binding an HttpListener.
    /// </summary>
    internal async Task<(string Name, string Dc)> LoginAsync(
        string name,
        string clientId,
        string clientSecret,
        string[] scopes,
        string dc,
        int callbackPort,
        Func<LocalCallbackServer> serverFactory,
        CancellationToken ct = default)
    {
        // Step 1: Uniqueness check.
        var existing = await _accountStore.FindAsync(name, ct).ConfigureAwait(false);
        if (existing is not null)
            throw new ZapiCliException(
                $"Account '{name}' already exists. Use 'account remove' first to replace it.",
                ErrorCodes.ACCOUNT_ALREADY_EXISTS,
                exitCode: 1);

        // Step 2: Resolve DC base URL (needed to build the authorization URL).
        var baseUrl = DcResolver.GetAccountsBaseUrl(dc);

        // Step 3–7: Browser OAuth flow.
        await using var server = serverFactory();

        var state = _browserFlow.GenerateState();
        var redirectUri = $"http://localhost:{server.Port}/callback";
        var authUrl = _browserFlow.BuildAuthorizationUrl(baseUrl, clientId, redirectUri, scopes, state);

        Console.Error.WriteLine($"Redirect URI (must be registered in Zoho Developer Console): {redirectUri}");
        _browserFlow.OpenBrowser(authUrl);

        Console.Error.WriteLine("Waiting for browser authentication... (timeout: 120s)");

        var (code, returnedState) = await server
            .WaitForCallbackAsync(TimeSpan.FromSeconds(120), ct)
            .ConfigureAwait(false);

        // Step 8: CSRF state verification.
        if (returnedState != state)
            throw new ZapiCliException(
                "OAuth state mismatch — possible CSRF attack.",
                ErrorCodes.STATE_MISMATCH,
                exitCode: 1);

        // Step 9: Exchange code + finalize (same as AddAccountAsync).
        return await ExchangeAndFinalizeAsync(name, code, redirectUri, clientId, clientSecret, dc, scopes, ct)
            .ConfigureAwait(false);
    }

    // ─── ExchangeAndFinalizeAsync ─────────────────────────────────────────────

    /// <summary>
    /// Shared token-exchange + account-persistence helper used by both
    /// <see cref="AddAccountAsync"/> and <see cref="LoginAsync"/>.
    /// Performs: POST /oauth/v2/token → GET /oauth/user/info → ZohoCorp guard →
    /// keychain store → accounts.json persist.
    /// </summary>
    private async Task<(string Name, string Dc)> ExchangeAndFinalizeAsync(
        string name,
        string code,
        string redirectUri,
        string clientId,
        string clientSecret,
        string dc,
        IEnumerable<string> scopes,
        CancellationToken ct)
    {
        // Step 1: Resolve DC base URL — validates dc value.
        var baseUrl = DcResolver.GetAccountsBaseUrl(dc);

        // Step 2: Exchange the grant code for access_token + refresh_token.
        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(30);

        var tokenFormData = new Dictionary<string, string>
        {
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
        };

        HttpResponseMessage tokenResponse;
        try
        {
            tokenResponse = await httpClient.PostAsync(
                $"{baseUrl}/oauth/v2/token",
                new FormUrlEncodedContent(tokenFormData),
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ZapiCliException(
                $"Failed to reach Zoho token endpoint: {ex.Message}",
                ErrorCodes.AUTH_FAILURE,
                exitCode: 2);
        }

        string accessToken;
        string refreshToken;

        using (tokenResponse)
        {
            if (!tokenResponse.IsSuccessStatusCode)
                throw new ZapiCliException(
                    $"Grant code exchange failed (HTTP {(int)tokenResponse.StatusCode}). " +
                    "Verify --code, --client-id, --client-secret, and --redirect-uri are correct.",
                    ErrorCodes.AUTH_FAILURE,
                    exitCode: 2);

            var tokenBody = await tokenResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var tokenDoc = JsonDocument.Parse(tokenBody);
            var tokenRoot = tokenDoc.RootElement;

            if (tokenRoot.TryGetProperty("error", out var errEl))
                throw new ZapiCliException(
                    $"Grant code exchange error: {errEl.GetString()}. " +
                    "The grant code may have expired (max 10 min). Generate a new one from the Developer Console.",
                    ErrorCodes.AUTH_FAILURE,
                    exitCode: 2);

            if (!tokenRoot.TryGetProperty("access_token", out var atEl) ||
                string.IsNullOrEmpty(atEl.GetString()))
                throw new ZapiCliException(
                    "Token exchange response did not contain 'access_token'.",
                    ErrorCodes.AUTH_FAILURE,
                    exitCode: 2);

            if (!tokenRoot.TryGetProperty("refresh_token", out var rtEl) ||
                string.IsNullOrEmpty(rtEl.GetString()))
                throw new ZapiCliException(
                    "Token exchange response did not contain 'refresh_token'. " +
                    "Ensure the 'offline_access' scope or equivalent is granted.",
                    ErrorCodes.AUTH_FAILURE,
                    exitCode: 2);

            accessToken = atEl.GetString()!;
            refreshToken = rtEl.GetString()!;
        }

        // Step 3: Fetch user-info to validate token and retrieve email + ZPUID.
        using var userInfoRequest = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/oauth/user/info");
        userInfoRequest.Headers.TryAddWithoutValidation("Authorization", $"Zoho-oauthtoken {accessToken}");

        HttpResponseMessage userInfoResponse;
        try
        {
            userInfoResponse = await httpClient.SendAsync(userInfoRequest, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ZapiCliException(
                $"Failed to reach Zoho user-info endpoint: {ex.Message}",
                ErrorCodes.AUTH_FAILURE,
                exitCode: 2);
        }

        using (userInfoResponse)
        {
            if (!userInfoResponse.IsSuccessStatusCode)
                throw new ZapiCliException(
                    $"User-info fetch failed (HTTP {(int)userInfoResponse.StatusCode}).",
                    ErrorCodes.AUTH_FAILURE,
                    exitCode: 2);

            var body = await userInfoResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // Step 4: Parse Email and ZPUID from the user-info response.
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var email = root.TryGetProperty("Email", out var emailEl)
                ? emailEl.GetString()
                : null;

            if (string.IsNullOrEmpty(email))
                throw new ZapiCliException(
                    "The Zoho user-info endpoint did not return an email address. " +
                    "Ensure the 'AaaServer.profile.READ' scope is granted on your Self-Client app.",
                    ErrorCodes.EMAIL_REQUIRED,
                    exitCode: 1);

            var zuid = root.TryGetProperty("ZUID", out var zuidEl)
                ? zuidEl.ValueKind == JsonValueKind.Number
                    ? zuidEl.GetInt64().ToString()
                    : zuidEl.GetString()
                : null;

            // Step 5: ZohoCorp block — MUST run after email is known, BEFORE any write.
            ZohoCorpGuard.AssertNotZohoCorp(email);

            // Step 6: Store credentials in the OS keychain.
            await _authProvider.StoreTokenAsync(name, accessToken, refreshToken, clientId, clientSecret, ct)
                .ConfigureAwait(false);

            // Step 7: Persist account entry in accounts.json.
            var accountsRoot = await _accountStore.LoadAsync(ct).ConfigureAwait(false);
            var isDefault = accountsRoot.Accounts.Count == 0;

            var entry = new AccountEntry
            {
                Name = name,
                Dc = dc,
                Email = email,
                Zuid = zuid,
                Scopes = [.. scopes],
                IsDefault = isDefault,
            };

            var updatedRoot = new AccountsRoot
            {
                Accounts = [.. accountsRoot.Accounts, entry],
            };

            await _accountStore.SaveAsync(updatedRoot, ct).ConfigureAwait(false);

            return (name, dc);
        }
    }

    // ─── ListAccountsAsync ────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AccountListView>> ListAccountsAsync(
        CancellationToken ct = default)
    {
        var root = await _accountStore.LoadAsync(ct).ConfigureAwait(false);
        return root.Accounts
            .Select(a => new AccountListView
            {
                Name = a.Name,
                Dc = a.Dc,
                Email = a.Email,
                Zuid = a.Zuid,
                IsDefault = a.IsDefault,
                ScopeCount = a.Scopes.Count,
            })
            .ToList();
    }

    // ─── ShowAccountAsync ─────────────────────────────────────────────────────

    public async Task<AccountShowView> ShowAccountAsync(
        string name,
        CancellationToken ct = default)
    {
        var account = await _accountStore.FindAsync(name, ct).ConfigureAwait(false);
        if (account is null)
            throw new ZapiCliException(
                $"Account '{name}' not found.",
                ErrorCodes.ACCOUNT_NOT_FOUND,
                exitCode: 1);

        return new AccountShowView
        {
            Name = account.Name,
            Dc = account.Dc,
            Email = account.Email,
            Zuid = account.Zuid,
            Scopes = account.Scopes,
            IsDefault = account.IsDefault,
            Token = "***",
        };
    }

    // ─── SetDefaultAsync ──────────────────────────────────────────────────────

    public async Task SetDefaultAsync(string name, CancellationToken ct = default)
    {
        var root = await _accountStore.LoadAsync(ct).ConfigureAwait(false);

        if (!root.Accounts.Any(a => a.Name == name))
            throw new ZapiCliException(
                $"Account '{name}' not found.",
                ErrorCodes.ACCOUNT_NOT_FOUND,
                exitCode: 1);

        var updatedAccounts = root.Accounts
            .Select(a => a with { IsDefault = a.Name == name })
            .ToList();

        await _accountStore.SaveAsync(
                new AccountsRoot { Accounts = updatedAccounts }, ct)
            .ConfigureAwait(false);
    }

    // ─── RemoveAccountAsync ───────────────────────────────────────────────────

    public async Task RemoveAccountAsync(string name, CancellationToken ct = default)
    {
        var root = await _accountStore.LoadAsync(ct).ConfigureAwait(false);

        var account = root.Accounts.FirstOrDefault(a => a.Name == name);
        if (account is null)
            throw new ZapiCliException(
                $"Account '{name}' not found.",
                ErrorCodes.ACCOUNT_NOT_FOUND,
                exitCode: 1);

        // Attempt server-side token revocation (best-effort — failure does not abort local cleanup).
        try
        {
            var accessToken = await _authProvider.GetTokenAsync(name, ct).ConfigureAwait(false);
            var baseUrl = DcResolver.GetAccountsBaseUrl(account.Dc);

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(30);

            var formData = new Dictionary<string, string> { ["token"] = accessToken };
            using var revokeContent = new FormUrlEncodedContent(formData);
            using var revokeResponse = await httpClient
                .PostAsync($"{baseUrl}/oauth/v2/token/revoke", revokeContent, ct)
                .ConfigureAwait(false);

            if (!revokeResponse.IsSuccessStatusCode)
                _logger.LogWarning(
                    "Token revocation returned HTTP {StatusCode} for account '{AccountName}'. " +
                    "Continuing with local cleanup.",
                    (int)revokeResponse.StatusCode,
                    name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Token revocation failed for account '{AccountName}'. Continuing with local cleanup.",
                name);
        }

        // Clear keychain entry (best-effort).
        await _authProvider.ClearTokenAsync(name, ct).ConfigureAwait(false);

        // Remove from accounts.json and re-assign default if needed.
        var remaining = root.Accounts.Where(a => a.Name != name).ToList();

        if (account.IsDefault && remaining.Count > 0)
            remaining[0] = remaining[0] with { IsDefault = true };

        await _accountStore.SaveAsync(
                new AccountsRoot { Accounts = remaining }, ct)
            .ConfigureAwait(false);
    }

    // ─── ReAuthAsync ──────────────────────────────────────────────────────────

    public async Task ReAuthAsync(string name, CancellationToken ct = default)
    {
        var account = await _accountStore.FindAsync(name, ct).ConfigureAwait(false);
        if (account is null)
            throw new ZapiCliException(
                $"Account '{name}' not found.",
                ErrorCodes.ACCOUNT_NOT_FOUND,
                exitCode: 1);

        // Apply ZohoCorp block defensively — account should never have been added with a corp email.
        ZohoCorpGuard.AssertNotZohoCorp(account.Email);

        await _authProvider.RefreshTokenAsync(name, account.Scopes, account.Dc, ct)
            .ConfigureAwait(false);
    }

    // ─── AddScopesAsync ───────────────────────────────────────────────────────

    public Task<(string AccountName, List<string> UpdatedScopes)> AddScopesAsync(
        string accountName,
        IEnumerable<string> scopesToAdd,
        int callbackPort = 8085,
        CancellationToken ct = default) =>
        AddScopesAsync(accountName, scopesToAdd, callbackPort, port => new LocalCallbackServer(port), ct);

    /// <summary>
    /// Internal overload that accepts a <paramref name="serverFactory"/> — used by tests to inject
    /// a fake callback server without binding an HttpListener.
    /// </summary>
    internal async Task<(string AccountName, List<string> UpdatedScopes)> AddScopesAsync(
        string accountName,
        IEnumerable<string> scopesToAdd,
        int callbackPort,
        Func<int, LocalCallbackServer> serverFactory,
        CancellationToken ct = default)
    {
        // Step 1: Load account.
        var root = await _accountStore.LoadAsync(ct).ConfigureAwait(false);
        var account = root.Accounts.FirstOrDefault(a => a.Name == accountName);
        if (account is null)
            throw new ZapiCliException(
                $"Account '{accountName}' not found.",
                ErrorCodes.ACCOUNT_NOT_FOUND,
                exitCode: 1);

        // Step 2: ZohoCorp block.
        ZohoCorpGuard.AssertNotZohoCorp(account.Email);

        // Step 3: Build deduped updated scope list.
        var scopesList = scopesToAdd.ToList();
        var updatedScopes = account.Scopes.ToList();
        foreach (var s in scopesList)
        {
            if (!updatedScopes.Contains(s, StringComparer.Ordinal))
                updatedScopes.Add(s);
        }

        // Step 4: Obtain scope enhancement token.
        var (enhanceToken, clientId) = await _authProvider
            .GetScopeEnhancementTokenAsync(accountName, account.Dc, ct)
            .ConfigureAwait(false);

        // Step 5–12: Browser consent flow.
        await using var server = serverFactory(callbackPort);
        var redirectUri = $"http://localhost:{server.Port}/callback";
        var baseUrl = DcResolver.GetAccountsBaseUrl(account.Dc);

        var addExtraScopeUrl =
            $"{baseUrl}/oauth/v2/token/addextrascope" +
            $"?client_id={Uri.EscapeDataString(clientId)}" +
            $"&response_type=update_scopes" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString(string.Join(",", scopesList))}" +
            $"&enhance_token={Uri.EscapeDataString(enhanceToken)}" +
            $"&logout=true";

        Console.Error.WriteLine($"Redirect URI (must be registered in Zoho Developer Console): {redirectUri}");
        _browserFlow.OpenBrowser(addExtraScopeUrl);
        Console.Error.WriteLine("Waiting for browser scope consent... (timeout: 120s)");

        await server.WaitForScopeEnhancedCallbackAsync(TimeSpan.FromSeconds(120), ct)
            .ConfigureAwait(false);

        // Step 13: Persist updated scopes.
        var updatedAccount = account with { Scopes = updatedScopes };
        var updatedAccounts = root.Accounts
            .Select(a => a.Name == accountName ? updatedAccount : a)
            .ToList();
        await _accountStore.SaveAsync(
            new AccountsRoot { Accounts = updatedAccounts }, ct).ConfigureAwait(false);

        // Step 14: Refresh access token so the new scope is reflected immediately.
        await _authProvider.RefreshTokenAsync(accountName, updatedScopes, account.Dc, ct)
            .ConfigureAwait(false);

        return (updatedAccount.Name, updatedScopes);
    }

    // ─── GetScopesAsync ───────────────────────────────────────────────────────

    public async Task<List<string>> GetScopesAsync(
        string accountName,
        CancellationToken ct = default)
    {
        var root = await _accountStore.LoadAsync(ct).ConfigureAwait(false);
        var account = root.Accounts.FirstOrDefault(a => a.Name == accountName);
        if (account is null)
            throw new ZapiCliException(
                $"Account '{accountName}' not found.",
                ErrorCodes.ACCOUNT_NOT_FOUND,
                exitCode: 1);

        ZohoCorpGuard.AssertNotZohoCorp(account.Email);

        return account.Scopes.ToList();
    }
}
