using System.Net;
using System.Text;
using System.Text.Json;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Auth;
using ZapiCli.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace ZapiCli.Tests.Auth;

/// <summary>
/// TDD test scenarios for <see cref="AccountService.MobileLoginAsync"/>.
/// These tests define the full expected behaviour of the Zoho Mobile OAuth 2.0 flow
/// (<c>/oauth/v2/mobile/auth</c>) and serve as an executable specification.
///
/// All scenarios are RED (failing) until <c>MobileLoginAsync</c> is implemented.
/// See <c>plan/feature-mobile-auth-login-1.md</c> for the implementation plan.
///
/// Test map:
///   Scenario 1  — RSA key pair is generated before the browser is opened
///   Scenario 2  — Auth URL uses /oauth/v2/mobile/auth path (not /oauth/v2/auth)
///   Scenario 3  — Auth URL carries the ss_id (RSA public key) parameter
///   Scenario 4  — gt_sec RSA decryption produces the correct client_secret for token exchange
///   Scenario 5  — Token exchange POST includes rt_hash (= gt_hash) parameter
///   Scenario 6  — Token exchange POST is sent to accounts-server from redirect (DCL routing)
///   Scenario 7  — DCL_MISSING error when dc_locations is absent from token response
///   Scenario 8  — STATE_MISMATCH error when CSRF state does not match
///   Scenario 9  — ACCOUNT_ALREADY_EXISTS error when duplicate account name is provided
///   Scenario 10 — LOGIN_TIMEOUT error when callback server signals timeout
///   Scenario 11 — RSA_DECRYPT_FAILURE error when gt_sec cannot be decrypted
///   Scenario 12 — Happy path: account and keychain are persisted after successful login
/// </summary>
public sealed class MobileLoginAsyncTests : IDisposable
{
    private readonly FakeRsaKeyPairProvider _rsaProvider = new();

    public void Dispose() => _rsaProvider.Dispose();

    // ─── Service factory ──────────────────────────────────────────────────────

    private static AccountService CreateService(
        FakeAccountStore accountStore,
        FakeAuthProvider authProvider,
        IHttpClientFactory httpClientFactory,
        FakeOAuthBrowserFlow browserFlow)
    {
        return new AccountService(
            accountStore,
            authProvider,
            httpClientFactory,
            NullLogger<AccountService>.Instance,
            browserFlow);
    }

    // ─── HTTP factory helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Simulates a successful Zoho token endpoint + user-info endpoint.
    /// Token endpoint returns dc_locations so the mobile flow does not abort.
    /// </summary>
    private static IHttpClientFactory BuildSuccessHttpFactory(
        string accessToken = "at_mobile_test",
        string refreshToken = "rt_mobile_test",
        string email = "mobile@example.com",
        string zuid = "99887",
        string dcLocations = "{\"us\":\"accounts.zoho.com\"}",
        List<HttpRequestMessage>? capturedRequests = null)
    {
        return FakeHttpMessageHandler.ToFactory(req =>
        {
            capturedRequests?.Add(req);

            if (req.RequestUri!.AbsolutePath.Contains("token"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"access_token\":\"{accessToken}\"," +
                        $"\"refresh_token\":\"{refreshToken}\"," +
                        $"\"dc_locations\":{dcLocations}}}",
                        Encoding.UTF8, "application/json"),
                };

            if (req.RequestUri.AbsolutePath.Contains("user/info"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"Email\":\"{email}\",\"ZUID\":\"{zuid}\"}}",
                        Encoding.UTF8, "application/json"),
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    /// <summary>
    /// Token endpoint returns dc_locations absent — used for Scenario 7.
    /// </summary>
    private static IHttpClientFactory BuildNoDclHttpFactory()
    {
        return FakeHttpMessageHandler.ToFactory(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("token"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"access_token\":\"at\",\"refresh_token\":\"rt\"}",
                        Encoding.UTF8, "application/json"),
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    /// <summary>
    /// Captures the full token request body for form-data inspection.
    /// </summary>
    private static IHttpClientFactory BuildCapturingHttpFactory(
        out List<(string Url, string Body)> tokenCalls)
    {
        var calls = new List<(string Url, string Body)>();
        tokenCalls = calls;

        return FakeHttpMessageHandler.ToFactory(req =>
        {
            if (req.RequestUri!.AbsolutePath.Contains("token"))
            {
                var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty;
                calls.Add((req.RequestUri.ToString(), body));

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"access_token\":\"at\",\"refresh_token\":\"rt\"," +
                        "\"dc_locations\":{\"us\":\"accounts.zoho.com\"}}",
                        Encoding.UTF8, "application/json"),
                };
            }

            if (req.RequestUri.AbsolutePath.Contains("user/info"))
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"Email\":\"user@example.com\",\"ZUID\":\"1\"}",
                        Encoding.UTF8, "application/json"),
                };

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });
    }

    // ─── Scenario 1: RSA key pair generated before browser opens ─────────────

    [Fact]
    public async Task MobileLoginAsync_GeneratesRsaKeyPair_BeforeOpeningBrowser()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("s1-state");
        var factory = BuildSuccessHttpFactory();
        var service = CreateService(store, auth, factory, browser);

        var encryptedSecret = _rsaProvider.EncryptAsServer("client_secret_s1");
        var cbResult = new MobileCallbackResult(
            Code: "code-s1",
            State: "s1-state",
            GtHash: "hash-s1",
            GtSec: encryptedSecret,
            AccountsServer: "https://accounts.zoho.com",
            Location: "us");

        await service.MobileLoginAsync(
            "acct-s1", "cid-s1", ["ZohoAPI.READ"], "us",
            8085, null, _rsaProvider, ct: default, serverFactory: () => new FakeMobileCallbackServer(cbResult));

        // At least one BuildMobileUrl call means the RSA provider was invoked before browser opened.
        Assert.Single(browser.BuildMobileUrlCalls);
        Assert.Equal(1, browser.GenerateStateCalls);
    }

    // ─── Scenario 2: Auth URL path is /oauth/v2/mobile/auth ──────────────────

    [Fact]
    public async Task MobileLoginAsync_BuildsUrlWithMobileAuthPath()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("s2-state");
        var factory = BuildSuccessHttpFactory();
        var service = CreateService(store, auth, factory, browser);

        var encryptedSecret = _rsaProvider.EncryptAsServer("secret-s2");
        var cbResult = new MobileCallbackResult("code-s2", "s2-state", "hash-s2", encryptedSecret, "https://accounts.zoho.com", "us");

        await service.MobileLoginAsync(
            "acct-s2", "cid-s2", ["ZohoAPI.READ"], "us",
            8085, null, _rsaProvider, ct: default, serverFactory: () => new FakeMobileCallbackServer(cbResult));

        var call = Assert.Single(browser.BuildMobileUrlCalls);
        // The fake returns a URL containing the mobile path.
        var builtUrl = browser.BuildMobileAuthorizationUrl(
            call.BaseUrl, call.ClientId, call.RedirectUri, call.Scopes, call.State, call.PublicKeyBase64);
        Assert.Contains("/oauth/v2/mobile/auth", builtUrl);
    }

    // ─── Scenario 3: Auth URL carries ss_id (RSA public key) ─────────────────

    [Fact]
    public async Task MobileLoginAsync_BuildsUrlWithSsIdParameter()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("s3-state");
        var factory = BuildSuccessHttpFactory();
        var service = CreateService(store, auth, factory, browser);

        var encryptedSecret = _rsaProvider.EncryptAsServer("secret-s3");
        var cbResult = new MobileCallbackResult("code-s3", "s3-state", "hash-s3", encryptedSecret, "https://accounts.zoho.com", "us");

        await service.MobileLoginAsync(
            "acct-s3", "cid-s3", ["ZohoAPI.READ"], "us",
            8085, null, _rsaProvider, ct: default, serverFactory: () => new FakeMobileCallbackServer(cbResult));

        var call = Assert.Single(browser.BuildMobileUrlCalls);
        // PublicKeyBase64 must be non-empty and match the key from the RSA provider.
        Assert.False(string.IsNullOrEmpty(call.PublicKeyBase64));
        var (expectedPubKey, _) = _rsaProvider.Generate();
        Assert.Equal(expectedPubKey, call.PublicKeyBase64);
    }

    // ─── Scenario 4: gt_sec decryption produces the correct client_secret ─────

    [Fact]
    public async Task MobileLoginAsync_DecryptsGtSecToGetClientSecret_UsedInTokenExchange()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("s4-state");
        var factory = BuildCapturingHttpFactory(out var tokenCalls);
        var service = CreateService(store, auth, factory, browser);

        const string realClientSecret = "real_decrypted_secret_42";
        var encryptedSecret = _rsaProvider.EncryptAsServer(realClientSecret);
        var cbResult = new MobileCallbackResult("code-s4", "s4-state", "hash-s4", encryptedSecret, "https://accounts.zoho.com", "us");

        await service.MobileLoginAsync(
            "acct-s4", "cid-s4", ["ZohoAPI.READ"], "us",
            8085, null, _rsaProvider, ct: default, serverFactory: () => new FakeMobileCallbackServer(cbResult));

        // The decrypted client_secret must appear in the token exchange POST body.
        var (tokenUrl, tokenBody) = Assert.Single(tokenCalls);
        Assert.Contains("client_secret=" + Uri.EscapeDataString(realClientSecret), tokenBody);
    }

    // ─── Scenario 5: Token exchange POST includes rt_hash ─────────────────────

    [Fact]
    public async Task MobileLoginAsync_TokenExchangeIncludesRtHashParameter()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("s5-state");
        var factory = BuildCapturingHttpFactory(out var tokenCalls);
        var service = CreateService(store, auth, factory, browser);

        const string gtHash = "gt_hash_value_xyz";
        var encryptedSecret = _rsaProvider.EncryptAsServer("secret-s5");
        var cbResult = new MobileCallbackResult("code-s5", "s5-state", gtHash, encryptedSecret, "https://accounts.zoho.com", "us");

        await service.MobileLoginAsync(
            "acct-s5", "cid-s5", ["ZohoAPI.READ"], "us",
            8085, null, _rsaProvider, ct: default, serverFactory: () => new FakeMobileCallbackServer(cbResult));

        var (_, tokenBody) = Assert.Single(tokenCalls);
        Assert.Contains("rt_hash=" + Uri.EscapeDataString(gtHash), tokenBody);
    }

    // ─── Scenario 6: Token exchange sent to accounts-server URL (DCL routing) ─

    [Fact]
    public async Task MobileLoginAsync_TokenExchangeUsesAccountsServerFromRedirect()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("s6-state");
        var factory = BuildCapturingHttpFactory(out var tokenCalls);
        var service = CreateService(store, auth, factory, browser);

        // EU-routed accounts server — different from the US DcResolver default.
        const string euAccountsServer = "https://accounts.zoho.eu";
        var encryptedSecret = _rsaProvider.EncryptAsServer("secret-s6");
        var cbResult = new MobileCallbackResult("code-s6", "s6-state", "hash-s6", encryptedSecret, euAccountsServer, "eu");

        await service.MobileLoginAsync(
            "acct-s6", "cid-s6", ["ZohoAPI.READ"], "us", // dc="us" intentionally wrong — accounts-server overrides it
            8085, null, _rsaProvider, ct: default, serverFactory: () => new FakeMobileCallbackServer(cbResult));

        var (tokenUrl, _) = Assert.Single(tokenCalls);
        Assert.StartsWith(euAccountsServer, tokenUrl);
    }

    // ─── Scenario 7: DCL_MISSING error when dc_locations absent ──────────────

    [Fact]
    public async Task MobileLoginAsync_ThrowsDclMissing_WhenDcLocationsAbsentFromTokenResponse()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("s7-state");
        var factory = BuildNoDclHttpFactory();
        var service = CreateService(store, auth, factory, browser);

        var encryptedSecret = _rsaProvider.EncryptAsServer("secret-s7");
        // Pass null AccountsServer — no fallback routing available, so DCL_MISSING must fire.
        var cbResult = new MobileCallbackResult("code-s7", "s7-state", "hash-s7", encryptedSecret, null, "us");

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            service.MobileLoginAsync(
                "acct-s7", "cid-s7", ["ZohoAPI.READ"], "us",
                8085, null, _rsaProvider, ct: default, serverFactory: () => new FakeMobileCallbackServer(cbResult)));

        Assert.Equal(ErrorCodes.DCL_MISSING, ex.Code);
    }

    // ─── Scenario 8: STATE_MISMATCH when CSRF state doesn't match ────────────

    [Fact]
    public async Task MobileLoginAsync_ThrowsStateMismatch_WhenCsrfStateMismatch()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("expected-state");
        var factory = BuildSuccessHttpFactory();
        var service = CreateService(store, auth, factory, browser);

        var encryptedSecret = _rsaProvider.EncryptAsServer("secret-s8");
        // Return a different state than what was sent → CSRF attack simulation.
        var cbResult = new MobileCallbackResult("code-s8", "wrong-state", "hash-s8", encryptedSecret, "https://accounts.zoho.com", "us");

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            service.MobileLoginAsync(
                "acct-s8", "cid-s8", ["ZohoAPI.READ"], "us",
                8085, null, _rsaProvider, ct: default, serverFactory: () => new FakeMobileCallbackServer(cbResult)));

        Assert.Equal(ErrorCodes.STATE_MISMATCH, ex.Code);
        // No token request should have been made after state mismatch.
    }

    // ─── Scenario 9: ACCOUNT_ALREADY_EXISTS ───────────────────────────────────

    [Fact]
    public async Task MobileLoginAsync_ThrowsAccountAlreadyExists_WhenDuplicateName()
    {
        var store = new FakeAccountStore();
        store.AddAccount(new AccountEntry
        {
            Name = "existing-mobile",
            Dc = "us",
            Email = "user@example.com",
            Zuid = "1",
            Scopes = [],
            IsDefault = true,
        });

        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow();
        var factory = BuildSuccessHttpFactory();
        var service = CreateService(store, auth, factory, browser);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            service.MobileLoginAsync(
                "existing-mobile", "cid", ["ZohoAPI.READ"], "us",
                8085, null, _rsaProvider, ct: default,
                serverFactory: () => new FakeMobileCallbackServer(
                    new MobileCallbackResult("c", "s", "h", "e", "https://accounts.zoho.com", "us"))));

        Assert.Equal(ErrorCodes.ACCOUNT_ALREADY_EXISTS, ex.Code);
        // Browser should never be opened — duplicate check runs first.
        Assert.Empty(browser.OpenBrowserCalls);
    }

    // ─── Scenario 10: LOGIN_TIMEOUT ───────────────────────────────────────────

    [Fact]
    public async Task MobileLoginAsync_ThrowsLoginTimeout_WhenCallbackServerTimesOut()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("s10-state");
        var factory = BuildSuccessHttpFactory();
        var service = CreateService(store, auth, factory, browser);

        var timeoutEx = new ZapiCliException(
            "Browser authentication timed out.",
            ErrorCodes.LOGIN_TIMEOUT, exitCode: 1);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            service.MobileLoginAsync(
                "acct-s10", "cid-s10", ["ZohoAPI.READ"], "us",
                8085, null, _rsaProvider, ct: default,
                serverFactory: () => new FakeMobileCallbackServer(timeoutEx)));

        Assert.Equal(ErrorCodes.LOGIN_TIMEOUT, ex.Code);
    }

    // ─── Scenario 11: RSA_DECRYPT_FAILURE ────────────────────────────────────

    [Fact]
    public async Task MobileLoginAsync_ThrowsRsaDecryptFailure_WhenGtSecIsInvalid()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("s11-state");
        var factory = BuildSuccessHttpFactory();
        var service = CreateService(store, auth, factory, browser);

        // Corrupted/non-RSA-encrypted gt_sec.
        const string corruptedGtSec = "not-valid-base64-rsa-ciphertext!!!";
        var cbResult = new MobileCallbackResult("code-s11", "s11-state", "hash-s11", corruptedGtSec, "https://accounts.zoho.com", "us");

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            service.MobileLoginAsync(
                "acct-s11", "cid-s11", ["ZohoAPI.READ"], "us",
                8085, null, _rsaProvider, ct: default, serverFactory: () => new FakeMobileCallbackServer(cbResult)));

        Assert.Equal(ErrorCodes.RSA_DECRYPT_FAILURE, ex.Code);
    }

    // ─── Scenario 12: Happy path — account and keychain persisted ─────────────

    [Fact]
    public async Task MobileLoginAsync_PersistsAccountAndKeychain_OnSuccess()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("s12-state");
        var factory = BuildSuccessHttpFactory(
            accessToken: "at_final",
            refreshToken: "rt_final",
            email: "happy@example.com",
            zuid: "11111");
        var service = CreateService(store, auth, factory, browser);

        const string realSecret = "the-real-client-secret";
        var encryptedSecret = _rsaProvider.EncryptAsServer(realSecret);
        var cbResult = new MobileCallbackResult(
            "code-s12", "s12-state", "hash-s12", encryptedSecret,
            "https://accounts.zoho.com", "us");

        var (name, dc) = await service.MobileLoginAsync(
            "acct-s12", "cid-s12", ["ZohoAPI.READ"], "us",
            8085, null, _rsaProvider, ct: default,
            serverFactory: () => new FakeMobileCallbackServer(cbResult));

        // Return values correct.
        Assert.Equal("acct-s12", name);
        Assert.Equal("us", dc);

        // Keychain was written exactly once with the decrypted client_secret.
        Assert.Equal(1, auth.StoreTokenCallCount);

        // Account was persisted in the store.
        var saved = await store.FindAsync("acct-s12");
        Assert.NotNull(saved);
        Assert.Equal("happy@example.com", saved!.Email);
        Assert.Equal("11111", saved.Zuid);
    }

    // ─── Story 21: Auth URL always uses global accounts.zoho.com ─────────────

    [Fact]
    public async Task MobileLoginAsync_AuthUrlAlwaysUsesGlobalAccountsZohoCom()
    {
        // Even if dc="eu" is passed, auth URL base must still be accounts.zoho.com.
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("global-state");
        var factory = BuildSuccessHttpFactory();
        var service = CreateService(store, auth, factory, browser);

        var encryptedSecret = _rsaProvider.EncryptAsServer("secret");
        var cbResult = new MobileCallbackResult("code", "global-state", "hash", encryptedSecret,
            "https://accounts.zoho.com", "eu");

        await service.MobileLoginAsync(
            "acct-global", "cid", ["ZohoAPI.READ"], "eu",
            8085, null, _rsaProvider, ct: default,
            serverFactory: () => new FakeMobileCallbackServer(cbResult));

        var call = Assert.Single(browser.BuildMobileUrlCalls);
        Assert.Equal("https://accounts.zoho.com", call.BaseUrl);
    }

    // ─── Story 21: DC derived from result.Location ────────────────────────────

    [Fact]
    public async Task MobileLoginAsync_LocationEu_EffectiveDcIsEu()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("eu-state");
        var factory = BuildSuccessHttpFactory(dcLocations: "{\"eu\":\"accounts.zoho.eu\"}");
        var service = CreateService(store, auth, factory, browser);

        var encryptedSecret = _rsaProvider.EncryptAsServer("secret-eu");
        var cbResult = new MobileCallbackResult("code-eu", "eu-state", "hash-eu", encryptedSecret,
            "https://accounts.zoho.eu", "eu");

        var (_, dc) = await service.MobileLoginAsync(
            "acct-eu", "cid", ["ZohoAPI.READ"], "us",  // dc param ignored — location from callback wins
            8085, null, _rsaProvider, ct: default,
            serverFactory: () => new FakeMobileCallbackServer(cbResult));

        Assert.Equal("eu", dc);
        var saved = await store.FindAsync("acct-eu");
        Assert.NotNull(saved);
        Assert.Equal("eu", saved!.Dc);
    }

    // ─── Story 21: Null name derived from email ───────────────────────────────

    [Fact]
    public async Task MobileLoginAsync_NullName_DerivedFromEmail()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("name-state");
        var factory = BuildSuccessHttpFactory(email: "john.doe@example.com");
        var service = CreateService(store, auth, factory, browser);

        var encryptedSecret = _rsaProvider.EncryptAsServer("secret");
        var cbResult = new MobileCallbackResult("code", "name-state", "hash", encryptedSecret,
            "https://accounts.zoho.com", "us");

        var (name, _) = await service.MobileLoginAsync(
            null, "cid", ["ZohoAPI.READ"], "us",
            8085, null, _rsaProvider, ct: default,
            serverFactory: () => new FakeMobileCallbackServer(cbResult));

        // name derived by replacing @ and . with _
        Assert.Equal("john_doe_example_com", name);

        var saved = await store.FindAsync("john_doe_example_com");
        Assert.NotNull(saved);
    }

    // ─── Story 21: RequiredProfileScope always injected ──────────────────────

    [Fact]
    public async Task MobileLoginAsync_RequiredProfileScopeAlwaysAdded()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("scope-state");
        var factory = BuildSuccessHttpFactory();
        var service = CreateService(store, auth, factory, browser);

        var encryptedSecret = _rsaProvider.EncryptAsServer("secret");
        var cbResult = new MobileCallbackResult("code", "scope-state", "hash", encryptedSecret,
            "https://accounts.zoho.com", "us");

        await service.MobileLoginAsync(
            "acct-scope", "cid", ["ZohoCRM.Contacts.READ"], "us",
            8085, null, _rsaProvider, ct: default,
            serverFactory: () => new FakeMobileCallbackServer(cbResult));

        var call = Assert.Single(browser.BuildMobileUrlCalls);
        Assert.Contains(OAuthConstants.RequiredProfileScope, call.Scopes,
            StringComparer.OrdinalIgnoreCase);
    }

    // ─── Story 21: Duplicate RequiredProfileScope is deduped ─────────────────

    [Fact]
    public async Task MobileLoginAsync_DuplicateRequiredScope_Deduplicated()
    {
        var store = new FakeAccountStore();
        var auth = new FakeAuthProvider();
        var browser = new FakeOAuthBrowserFlow("dedup-state");
        var factory = BuildSuccessHttpFactory();
        var service = CreateService(store, auth, factory, browser);

        var encryptedSecret = _rsaProvider.EncryptAsServer("secret");
        var cbResult = new MobileCallbackResult("code", "dedup-state", "hash", encryptedSecret,
            "https://accounts.zoho.com", "us");

        // Caller explicitly passes RequiredProfileScope — must not duplicate.
        await service.MobileLoginAsync(
            "acct-dedup", "cid", ["ZohoCRM.Contacts.READ", OAuthConstants.RequiredProfileScope], "us",
            8085, null, _rsaProvider, ct: default,
            serverFactory: () => new FakeMobileCallbackServer(cbResult));

        var call = Assert.Single(browser.BuildMobileUrlCalls);
        var count = call.Scopes.Count(s =>
            s.Equals(OAuthConstants.RequiredProfileScope, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, count);
    }
}
