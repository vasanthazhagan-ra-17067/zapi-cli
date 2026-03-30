using System.Security.Cryptography;
using System.Text;
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

    // ─── MobileLoginAsync ─────────────────────────────────────────────────────

    public Task<(string Name, string Dc)> MobileLoginAsync(
        string? name,
        string clientId,
        string[] scopes,
        string? clientSecret = null,
        CancellationToken ct = default) =>
        MobileLoginAsync(name, clientId, scopes, "us", OAuthConstants.DefaultCallbackPort, clientSecret,
            new RsaKeyPairProvider(), ct, () => new LocalCallbackServer(OAuthConstants.DefaultCallbackPort));

    /// <summary>
    /// Internal overload for unit tests: accepts injectable <see cref="IRsaKeyPairProvider"/> and
    /// <paramref name="serverFactory"/> to avoid network/browser activity in tests.
    /// The <paramref name="dc"/> parameter is preserved for test backward compatibility but is
    /// not used for auth URL construction — auth URL always uses https://accounts.zoho.com.
    /// Effective DC is derived from the <c>location</c> field in the OAuth callback.
    /// </summary>
    internal async Task<(string Name, string Dc)> MobileLoginAsync(
        string? name,
        string clientId,
        string[] scopes,
        string dc,
        int callbackPort,
        string? clientSecret,
        IRsaKeyPairProvider rsaProvider,
        CancellationToken ct,
        Func<LocalCallbackServer> serverFactory)
    {
        // Step 1: Inject required profile scope + dedup.
        var finalScopes = scopes
            .Append(OAuthConstants.RequiredProfileScope)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Step 2 (early, optional): Uniqueness check for non-null names before browser opens.
        if (name is not null)
        {
            var existing = await _accountStore.FindAsync(name, ct).ConfigureAwait(false);
            if (existing is not null)
                throw new ZapiCliException(
                    $"Account '{name}' already exists. Use 'account remove' first to replace it.",
                    ErrorCodes.ACCOUNT_ALREADY_EXISTS,
                    exitCode: 1);
        }

        // Step 3: Generate RSA key pair (public key → ss_id, private key → decrypt gt_sec).
        var (publicKeyBase64, privateKey) = rsaProvider.Generate();
        using var privateRsa = privateKey; // ensures RSA key is disposed when login completes or throws

        // Step 4: Start callback server + build mobile auth URL.
        // Auth URL always uses https://accounts.zoho.com as the global entry point.
        const string globalAuthBase = "https://accounts.zoho.com";
        await using var server = serverFactory();

        var state = _browserFlow.GenerateState();
        var redirectUri = $"http://localhost:{server.Port}/callback";
        var authUrl = _browserFlow.BuildMobileAuthorizationUrl(
            globalAuthBase, clientId, redirectUri, finalScopes, state, publicKeyBase64);

        Console.Error.WriteLine($"Redirect URI (must be registered in Zoho Developer Console): {redirectUri}");
        _browserFlow.OpenBrowser(authUrl);
        Console.Error.WriteLine("Waiting for browser authentication... (timeout: 120s)");

        // Step 5: Wait for mobile callback with full parameter set.
        var result = await server
            .WaitForMobileCallbackAsync(TimeSpan.FromSeconds(120), ct)
            .ConfigureAwait(false);

        // Step 6: CSRF state verification.
        if (result.State != state)
            throw new ZapiCliException(
                "OAuth state mismatch — possible CSRF attack.",
                ErrorCodes.STATE_MISMATCH,
                exitCode: 1);

        // Step 7: Derive effectiveDc from callback location (defaults to "us" if absent).
        var effectiveDc = result.Location?.ToLowerInvariant() is { Length: > 0 } loc ? loc : "us";

        // Step 8: Get client_secret — decrypt gt_sec via RSA, or fall back to the explicitly provided secret.
        string resolvedClientSecret;
        if (!string.IsNullOrEmpty(result.GtSec))
        {
            try
            {
                var cipherBytes = Convert.FromBase64String(result.GtSec);
                var plainBytes = privateRsa.Decrypt(cipherBytes, RSAEncryptionPadding.Pkcs1);
                resolvedClientSecret = Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                throw new ZapiCliException(
                    "Failed to decrypt the client secret from the OAuth redirect. " +
                    "Ensure the client ID is registered with Zoho as a Mobile/Desktop app type.",
                    ErrorCodes.RSA_DECRYPT_FAILURE,
                    exitCode: 2);
            }
        }
        else if (!string.IsNullOrEmpty(clientSecret))
        {
            resolvedClientSecret = clientSecret;
        }
        else
        {
            throw new ZapiCliException(
                "Zoho did not return an encrypted client secret (gt_sec) in the OAuth callback. " +
                "Pass --client-secret to provide it explicitly.",
                ErrorCodes.RSA_DECRYPT_FAILURE,
                exitCode: 2);
        }

        // Step 9: Token exchange + account persistence.
        return await ExchangeAndFinalizeAsync(
            name, result.Code, redirectUri, clientId, resolvedClientSecret, effectiveDc, finalScopes, ct,
            accountsServerOverride: result.AccountsServer,
            rtHash: result.GtHash,
            requireDcLocations: true).ConfigureAwait(false);
    }


    // ─── ExchangeAndFinalizeAsync ─────────────────────────────────────────────

    /// <summary>
    /// Shared token-exchange + account-persistence helper used by <see cref="MobileLoginAsync"/>.
    /// Performs: POST /oauth/v2/token → GET /oauth/user/info → ZohoCorp guard →
    /// name derivation (when null) → uniqueness check → keychain store → accounts.json persist.
    /// </summary>
    private async Task<(string Name, string Dc)> ExchangeAndFinalizeAsync(
        string? name,
        string code,
        string redirectUri,
        string clientId,
        string clientSecret,
        string dc,
        IEnumerable<string> scopes,
        CancellationToken ct,
        string? accountsServerOverride = null,
        string? rtHash = null,
        bool requireDcLocations = false)
    {
        // Step 1: Resolve DC base URL (mobile flow may override with accounts-server from redirect).
        var baseUrl = accountsServerOverride ?? DcResolver.GetAccountsBaseUrl(dc);

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

        // Mobile flow: include rt_hash (= gt_hash from redirect) in the token exchange.
        if (rtHash is not null)
            tokenFormData["rt_hash"] = rtHash;

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

            // Mobile flow: warn if dc_locations is absent but only hard-fail when
            // accounts-server was NOT already provided by the callback (no fallback routing available).
            if (requireDcLocations &&
                (!tokenRoot.TryGetProperty("dc_locations", out var dclEl) ||
                 dclEl.ValueKind != JsonValueKind.Object) &&
                accountsServerOverride is null)
                throw new ZapiCliException(
                    "Token exchange response did not contain 'dc_locations'. " +
                    "This is required for the Zoho Mobile OAuth flow. " +
                    "Ensure your client ID is registered as a Mobile/Desktop app type in the Zoho Developer Console.",
                    ErrorCodes.DCL_MISSING,
                    exitCode: 2);
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
                    $"Ensure the '{OAuthConstants.RequiredProfileScope}' scope is granted on your Self-Client app.",
                    ErrorCodes.EMAIL_REQUIRED,
                    exitCode: 1);

            var zuid = root.TryGetProperty("ZUID", out var zuidEl)
                ? zuidEl.ValueKind == JsonValueKind.Number
                    ? zuidEl.GetInt64().ToString()
                    : zuidEl.GetString()
                : null;

            // Step 5: ZohoCorp block — MUST run after email is known, BEFORE any write.
            ZohoCorpGuard.AssertNotZohoCorp(email);

            // Step 5b: Derive account name from email when not provided by caller.
            var resolvedName = string.IsNullOrWhiteSpace(name)
                ? email.Replace('@', '_').Replace('.', '_')
                : name;

            // Step 5c: Uniqueness check (also catches null-name case deferred from caller).
            var existingAcct = await _accountStore.FindAsync(resolvedName, ct).ConfigureAwait(false);
            if (existingAcct is not null)
                throw new ZapiCliException(
                    $"Account '{resolvedName}' already exists. Use 'account remove' first to replace it.",
                    ErrorCodes.ACCOUNT_ALREADY_EXISTS,
                    exitCode: 1);

            // Step 6: Store credentials in the OS keychain.
            await _authProvider.StoreTokenAsync(resolvedName, accessToken, refreshToken, clientId, clientSecret, ct)
                .ConfigureAwait(false);

            // Step 7: Persist account entry in accounts.json.
            var accountsRoot = await _accountStore.LoadAsync(ct).ConfigureAwait(false);
            var isDefault = accountsRoot.Accounts.Count == 0;

            var entry = new AccountEntry
            {
                Name = resolvedName,
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

            return (resolvedName, dc);
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
        string? name,
        string? email = null,
        string? zuidstring = null,
        CancellationToken ct = default)
    {
        var account = await ResolveAccountAsync(name, email, zuidstring, ct).ConfigureAwait(false);

        return new AccountShowView
        {
            Name = account.Name,
            Dc = account.Dc,
            Email = account.Email,
            Zuid = account.Zuid,
            Scopes = account.Scopes,
            IsDefault = account.IsDefault,
        };
    }

    // ─── SetDefaultAsync ──────────────────────────────────────────────────────

    public async Task SetDefaultAsync(string? name, string? email = null, string? zuidstring = null, CancellationToken ct = default)
    {
        var account = await ResolveAccountAsync(name, email, zuidstring, ct).ConfigureAwait(false);

        var root = await _accountStore.LoadAsync(ct).ConfigureAwait(false);
        var updatedAccounts = root.Accounts
            .Select(a => a with { IsDefault = a.Name == account.Name })
            .ToList();

        await _accountStore.SaveAsync(
                new AccountsRoot { Accounts = updatedAccounts }, ct)
            .ConfigureAwait(false);
    }

    // ─── RemoveAccountAsync ───────────────────────────────────────────────────

    public async Task RemoveAccountAsync(string? name, string? email = null, string? zuidstring = null, CancellationToken ct = default)
    {
        var account = await ResolveAccountAsync(name, email, zuidstring, ct).ConfigureAwait(false);
        var resolvedName = account.Name;

        // Attempt server-side token revocation (best-effort — failure does not abort local cleanup).
        try
        {
            var accessToken = await _authProvider.GetTokenAsync(resolvedName, ct).ConfigureAwait(false);
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
                    resolvedName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Token revocation failed for account '{AccountName}'. Continuing with local cleanup.",
                resolvedName);
        }

        // Clear keychain entry (best-effort).
        await _authProvider.ClearTokenAsync(resolvedName, ct).ConfigureAwait(false);

        // Remove from accounts.json and re-assign default if needed.
        var root = await _accountStore.LoadAsync(ct).ConfigureAwait(false);
        var remaining = root.Accounts.Where(a => a.Name != resolvedName).ToList();

        if (account.IsDefault && remaining.Count > 0)
            remaining[0] = remaining[0] with { IsDefault = true };

        await _accountStore.SaveAsync(
                new AccountsRoot { Accounts = remaining }, ct)
            .ConfigureAwait(false);
    }

    // ─── ReAuthAsync ──────────────────────────────────────────────────────────

    public async Task ReAuthAsync(string? name, string? email = null, string? zuidstring = null, CancellationToken ct = default)
    {
        var account = await ResolveAccountAsync(name, email, zuidstring, ct).ConfigureAwait(false);

        // Apply ZohoCorp block defensively — account should never have been added with a corp email.
        ZohoCorpGuard.AssertNotZohoCorp(account.Email);

        await _authProvider.RefreshTokenAsync(account.Name, account.Scopes, account.Dc, ct)
            .ConfigureAwait(false);
    }

    // ─── AddScopesAsync ───────────────────────────────────────────────────────

    public Task<(string AccountName, List<string> UpdatedScopes)> AddScopesAsync(
        string accountName,
        IEnumerable<string> scopesToAdd,
        int callbackPort = OAuthConstants.DefaultCallbackPort,
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

    // ─── RenameAccountAsync ───────────────────────────────────────────────────

    public async Task<(string OldName, string NewName)> RenameAccountAsync(
        string? name,
        string? email,
        string? zuidstring,
        string newName,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newName))
            throw new ZapiCliException(
                "--new-name is required.",
                ErrorCodes.INVALID_ARGS,
                exitCode: 1);

        if (newName.IndexOfAny(['/', '\\', ':', '*', '?']) >= 0)
            throw new ZapiCliException(
                "--new-name must not contain / \\ : * ? characters.",
                ErrorCodes.INVALID_ARGS,
                exitCode: 1);

        var oldEntry = await ResolveAccountAsync(name, email, zuidstring, ct).ConfigureAwait(false);

        var root = await _accountStore.LoadAsync(ct).ConfigureAwait(false);

        if (root.Accounts.Any(a => a.Name.Equals(newName, StringComparison.Ordinal)))
            throw new ZapiCliException(
                $"Account '{newName}' already exists. Choose a different name.",
                ErrorCodes.ACCOUNT_ALREADY_EXISTS,
                exitCode: 1);

        await _authProvider.RenameTokenAsync(oldEntry.Name, newName, ct).ConfigureAwait(false);

        var updatedAccounts = root.Accounts
            .Select(a => a.Name.Equals(oldEntry.Name, StringComparison.Ordinal)
                ? a with { Name = newName }
                : a)
            .ToList();

        await _accountStore.SaveAsync(new AccountsRoot { Accounts = updatedAccounts }, ct).ConfigureAwait(false);

        return (oldEntry.Name, newName);
    }

    // ─── ResolveAccountAsync (private helper) ─────────────────────────────────

    private async Task<AccountEntry> ResolveAccountAsync(
        string? name,
        string? email,
        string? zuidstring,
        CancellationToken ct)
    {
        var count = (string.IsNullOrWhiteSpace(name) ? 0 : 1)
                  + (string.IsNullOrWhiteSpace(email) ? 0 : 1)
                  + (string.IsNullOrWhiteSpace(zuidstring) ? 0 : 1);

        if (count == 0)
            throw new ZapiCliException(
                "At least one of --name, --email, or --zuidstring is required.",
                ErrorCodes.INVALID_ARGS,
                exitCode: 1);

        if (count > 1)
            throw new ZapiCliException(
                "Only one of --name, --email, or --zuidstring may be specified.",
                ErrorCodes.DUPLICATE_IDENTIFIER,
                exitCode: 1);

        if (!string.IsNullOrWhiteSpace(name))
        {
            var byName = await _accountStore.FindAsync(name, ct).ConfigureAwait(false);
            if (byName is null)
                throw new ZapiCliException(
                    $"Account '{name}' not found.",
                    ErrorCodes.ACCOUNT_NOT_FOUND,
                    exitCode: 1);
            return byName;
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var byEmail = await _accountStore.FindByEmailAsync(email, ct).ConfigureAwait(false);
            if (byEmail is null)
                throw new ZapiCliException(
                    $"No account found with email '{email}'.",
                    ErrorCodes.ACCOUNT_NOT_FOUND,
                    exitCode: 1);
            return byEmail;
        }

        // zuidstring path
        var byZuid = await _accountStore.FindByZuidAsync(zuidstring!, ct).ConfigureAwait(false);
        if (byZuid is null)
            throw new ZapiCliException(
                $"No account found with ZUIDSTRING '{zuidstring}'.",
                ErrorCodes.ACCOUNT_NOT_FOUND,
                exitCode: 1);
        return byZuid;
    }
}
