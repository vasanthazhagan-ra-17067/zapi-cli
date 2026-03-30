using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Auth;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests.Accounts;

/// <summary>
/// Tests verifying that scopes are persisted through the MobileLoginAsync flow into AccountEntry.Scopes.
/// </summary>
public sealed class AccountAddScopesTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly AccountStore _store;
    private readonly FakeAuthProvider _auth = new();
    private readonly FakeRsaKeyPairProvider _rsaProvider = new();

    public AccountAddScopesTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"zapi-cli-scope13-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        _store = new AccountStore(_tmpDir, NullLogger<AccountStore>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
        _rsaProvider.Dispose();
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private IHttpClientFactory UserInfoFactory(string email = "user@example.com")
    {
        var userInfoJson = $"{{\"Email\":\"{email}\",\"ZUID\":\"Z001\"}}";
        return FakeHttpMessageHandler.ToFactory(req =>
            req.RequestUri!.AbsolutePath.Contains("token")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(
                        $"{{\"access_token\":\"test-access\",\"refresh_token\":\"test-refresh\"," +
                        "\"dc_locations\":{\"us\":\"accounts.zoho.com\"}}",
                        Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(userInfoJson, Encoding.UTF8, "application/json") });
    }

    private FakeMobileCallbackServer MakeFakeServer() =>
        new FakeMobileCallbackServer(new MobileCallbackResult(
            Code: "grant-code",
            State: "fake-state-token",
            GtHash: "irrelevant-hash",
            GtSec: _rsaProvider.EncryptAsServer("fake-client-secret"),
            AccountsServer: "https://accounts.zoho.com",
            Location: "us"));

    private AccountService CreateService(IHttpClientFactory? httpFactory = null)
        => new AccountService(
            _store,
            _auth,
            httpFactory ?? UserInfoFactory(),
            NullLogger<AccountService>.Instance,
            new FakeOAuthBrowserFlow("fake-state-token"));

    // ── Test 1: Single scope persisted via MobileLoginAsync ───────────────────

    [Fact]
    public async Task MobileLoginAsync_SingleScope_IsPersistedInAccountEntry()
    {
        var service = CreateService();

        await service.MobileLoginAsync(
            "work", "cid", ["ZohoCRM.Contacts.READ"],
            dc: "us",
            callbackPort: 54321,
            clientSecret: null,
            rsaProvider: _rsaProvider,
            ct: default,
            serverFactory: MakeFakeServer);

        var entry = await _store.FindAsync("work");
        Assert.NotNull(entry);
        Assert.Contains("ZohoCRM.Contacts.READ", entry!.Scopes);
    }

    // ── Test 2: Multiple scopes persisted via MobileLoginAsync ────────────────

    [Fact]
    public async Task MobileLoginAsync_MultipleScopes_AllPersistedInAccountEntry()
    {
        var service = CreateService();

        await service.MobileLoginAsync(
            "work2", "cid", ["ZohoCRM.Contacts.READ", "ZohoCRM.Deals.READ"],
            dc: "us",
            callbackPort: 54321,
            clientSecret: null,
            rsaProvider: _rsaProvider,
            ct: default,
            serverFactory: MakeFakeServer);

        var entry = await _store.FindAsync("work2");
        Assert.NotNull(entry);
        Assert.Contains("ZohoCRM.Contacts.READ", entry!.Scopes);
        Assert.Contains("ZohoCRM.Deals.READ", entry.Scopes);
    }
}

