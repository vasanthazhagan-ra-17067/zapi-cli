using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Spectre.Console.Cli;
using ZapiCli.Commands;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests.Accounts;

/// <summary>
/// Tests for Story 13: --scope flag wired through AddAccountAsync and LoginAsync
/// into AccountEntry.Scopes.
/// </summary>
public sealed class AccountAddScopesTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly AccountStore _store;
    private readonly FakeAuthProvider _auth = new();

    public AccountAddScopesTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"zapi-cli-scope13-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        _store = new AccountStore(_tmpDir, NullLogger<AccountStore>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── helpers ────────────────────────────────────────────────────────────────

    private const string FakeTokenJson =
        "{\"access_token\":\"test-access\",\"refresh_token\":\"test-refresh\",\"token_type\":\"Bearer\",\"expires_in\":3600000}";

    private static IHttpClientFactory UserInfoFactory(string email = "user@example.com")
    {
        var userInfoJson = $"{{\"Email\":\"{email}\",\"ZUID\":\"Z001\"}}";
        return FakeHttpMessageHandler.ToFactory(req =>
            req.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(FakeTokenJson, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(userInfoJson, Encoding.UTF8, "application/json") });
    }

    private AccountService CreateService(IHttpClientFactory? httpFactory = null)
        => new AccountService(
            _store,
            _auth,
            httpFactory ?? UserInfoFactory(),
            NullLogger<AccountService>.Instance,
            new FakeOAuthBrowserFlow());

    // ── Test 1: Single scope persisted ────────────────────────────────────────

    [Fact]
    public async Task AddAccountAsync_SingleScope_IsPersistedInAccountEntry()
    {
        var service = CreateService();

        await service.AddAccountAsync(
            "work", "code", "https://www.zoho.com", "cid", "csec", "us",
            new[] { "ZohoCRM.Contacts.READ" });

        var entry = await _store.FindAsync("work");
        Assert.NotNull(entry);
        Assert.Equal(new[] { "ZohoCRM.Contacts.READ" }, entry!.Scopes);
    }

    // ── Test 2: Multiple comma-parsed scopes stored correctly ─────────────────

    [Fact]
    public async Task AddAccountAsync_MultipleScopes_AllPersistedInAccountEntry()
    {
        var service = CreateService();

        await service.AddAccountAsync(
            "work", "code", "https://www.zoho.com", "cid", "csec", "us",
            new[] { "ZohoCRM.Contacts.READ", "ZohoCRM.Deals.READ" });

        var entry = await _store.FindAsync("work");
        Assert.NotNull(entry);
        Assert.Contains("ZohoCRM.Contacts.READ", entry!.Scopes);
        Assert.Contains("ZohoCRM.Deals.READ", entry!.Scopes);
        Assert.Equal(2, entry.Scopes.Count);
    }

    // ── Test 3: AccountAddSettings.Validate() fails when --scope is missing ───

    [Fact]
    public void AccountAddSettings_MissingScope_ValidationFails()
    {
        var settings = new AccountCommands.AccountAddSettings
        {
            Name = "work",
            Code = "gc",
            ClientId = "cid",
            ClientSecret = "csec",
            // Scope intentionally omitted
        };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("scope", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AccountAddSettings_WhitespaceScope_ValidationFails()
    {
        var settings = new AccountCommands.AccountAddSettings
        {
            Name = "work",
            Code = "gc",
            ClientId = "cid",
            ClientSecret = "csec",
            Scope = "   ",
        };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("scope", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Test 4: LoginAsync stores scopes ──────────────────────────────────────

    [Fact]
    public async Task LoginAsync_SingleScope_IsPersistedInAccountEntry()
    {
        var service = CreateService();

        var fakeServer = new FakeLocalCallbackServer(code: "login-code", state: "fake-state-token");
        await service.LoginAsync(
            "work", "cid", "csec",
            new[] { "ZohoCliq.Channels.READ" },
            "us",
            callbackPort: 8085,
            serverFactory: () => fakeServer);

        var entry = await _store.FindAsync("work");
        Assert.NotNull(entry);
        Assert.Equal(new[] { "ZohoCliq.Channels.READ" }, entry!.Scopes);
    }

    // ── Test 5: LoginAsync with empty scopes stores empty list ────────────────

    [Fact]
    public async Task LoginAsync_EmptyScopes_StoresEmptyScopeList()
    {
        var service = CreateService();

        var fakeServer = new FakeLocalCallbackServer(code: "login-code", state: "fake-state-token");
        await service.LoginAsync(
            "work", "cid", "csec",
            Array.Empty<string>(),
            "us",
            callbackPort: 8085,
            serverFactory: () => fakeServer);

        var entry = await _store.FindAsync("work");
        Assert.NotNull(entry);
        Assert.Empty(entry!.Scopes);
    }
}
