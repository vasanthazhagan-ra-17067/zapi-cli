using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Tests.Fakes;
using Xunit;

namespace ZapiCli.Tests.Accounts;

/// <summary>
/// Tests for Story 19: multi-identifier account resolution via --name, --email, --zuidstring.
/// Exercises <c>ResolveAccountAsync</c> through the public service methods.
/// </summary>
public sealed class AccountResolveTests : IDisposable
{
    private readonly string _tmpDir =
        Path.Combine(Path.GetTempPath(), "zapi-resolve-test-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly AccountStore _store;
    private readonly FakeAuthProvider _auth = new();

    public AccountResolveTests()
    {
        Directory.CreateDirectory(_tmpDir);
        _store = new AccountStore(_tmpDir, NullLogger<AccountStore>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    private const string FakeTokenJson =
        "{\"access_token\":\"tok\",\"refresh_token\":\"rtok\",\"token_type\":\"Bearer\",\"expires_in\":3600000}";

    private IHttpClientFactory UserInfoFactory(string email, string zuid = "Z001")
    {
        var userInfoJson = $"{{\"Email\":\"{email}\",\"ZUID\":\"{zuid}\"}}";
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
            httpFactory ?? UserInfoFactory("user@example.com"),
            NullLogger<AccountService>.Instance,
            new FakeOAuthBrowserFlow());

    private async Task SeedAccountAsync(string name, string email, string zuidstring = "Z001")
    {
        var root = await _store.LoadAsync();
        root.Accounts.Add(new AccountEntry
        {
            Name = name,
            Email = email,
            Zuid = zuidstring,
            Dc = "us",
            IsDefault = root.Accounts.Count == 0,
            Scopes = [],
        });
        await _store.SaveAsync(root);
    }

    // ─── ShowAccountAsync resolution ─────────────────────────────────────────

    [Fact]
    public async Task ShowAccount_ByName_ReturnsView()
    {
        await SeedAccountAsync("alice", "alice@acme.com", "Z100");
        var service = CreateService();

        var view = await service.ShowAccountAsync(name: "alice");

        Assert.Equal("alice", view.Name);
    }

    [Fact]
    public async Task ShowAccount_ByEmail_ReturnsView()
    {
        await SeedAccountAsync("alice", "alice@acme.com", "Z100");
        var service = CreateService();

        var view = await service.ShowAccountAsync(name: null, email: "alice@acme.com");

        Assert.Equal("alice", view.Name);
    }

    [Fact]
    public async Task ShowAccount_ByZuidString_ReturnsView()
    {
        await SeedAccountAsync("alice", "alice@acme.com", "Z100");
        var service = CreateService();

        var view = await service.ShowAccountAsync(name: null, zuidstring: "Z100");

        Assert.Equal("alice", view.Name);
    }

    [Fact]
    public async Task ShowAccount_NoIdentifier_ThrowsInvalidArgs()
    {
        await SeedAccountAsync("alice", "alice@acme.com", "Z100");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.ShowAccountAsync(name: null, email: null, zuidstring: null));

        Assert.Equal(ErrorCodes.INVALID_ARGS, ex.Code);
    }

    [Fact]
    public async Task ShowAccount_TwoIdentifiers_ThrowsDuplicateIdentifier()
    {
        await SeedAccountAsync("alice", "alice@acme.com", "Z100");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.ShowAccountAsync(name: "alice", email: "alice@acme.com"));

        Assert.Equal(ErrorCodes.DUPLICATE_IDENTIFIER, ex.Code);
    }

    [Fact]
    public async Task ShowAccount_ByEmail_NotFound_ThrowsAccountNotFound()
    {
        await SeedAccountAsync("alice", "alice@acme.com", "Z100");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.ShowAccountAsync(name: null, email: "nobody@acme.com"));

        Assert.Equal(ErrorCodes.ACCOUNT_NOT_FOUND, ex.Code);
    }

    [Fact]
    public async Task ShowAccount_ByZuidString_NotFound_ThrowsAccountNotFound()
    {
        await SeedAccountAsync("alice", "alice@acme.com", "Z100");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.ShowAccountAsync(name: null, zuidstring: "Z999"));

        Assert.Equal(ErrorCodes.ACCOUNT_NOT_FOUND, ex.Code);
    }

    // ─── SetDefaultAsync resolution ──────────────────────────────────────────

    [Fact]
    public async Task SetDefault_ByEmail_UpdatesDefault()
    {
        await SeedAccountAsync("alice", "alice@acme.com", "Z100");
        await SeedAccountAsync("bob", "bob@acme.com", "Z200");
        var service = CreateService();

        await service.SetDefaultAsync(name: null, email: "bob@acme.com");

        var updated = await _store.FindAsync("bob");
        Assert.True(updated?.IsDefault);
    }

    // ─── RemoveAccountAsync resolution ───────────────────────────────────────

    [Fact]
    public async Task RemoveAccount_ByEmail_RemovesAccount()
    {
        await SeedAccountAsync("alice", "alice@acme.com", "Z100");
        var service = CreateService();

        await service.RemoveAccountAsync(name: null, email: "alice@acme.com");

        var removed = await _store.FindAsync("alice");
        Assert.Null(removed);
    }

    // ─── Email case-insensitivity ─────────────────────────────────────────────

    [Fact]
    public async Task FindByEmail_IsCaseInsensitive()
    {
        await SeedAccountAsync("alice", "Alice@ACME.COM", "Z100");
        var service = CreateService();

        var view = await service.ShowAccountAsync(name: null, email: "alice@acme.com");

        Assert.Equal("alice", view.Name);
    }
}
