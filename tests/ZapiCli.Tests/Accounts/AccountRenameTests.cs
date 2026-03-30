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
/// Tests for Story 20: account rename command and RenameAccountAsync.
/// </summary>
public sealed class AccountRenameTests : IDisposable
{
    private readonly string _tmpDir =
        Path.Combine(Path.GetTempPath(), "zapi-rename-test-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly FakeAccountStore _store = new();
    private readonly FakeAuthProvider _auth = new();

    public AccountRenameTests()
    {
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    private static IHttpClientFactory MakeHttpFactory(string email = "user@acme.com", string zuid = "Z001")
    {
        var tokenJson = "{\"access_token\":\"tok\",\"refresh_token\":\"rtok\",\"token_type\":\"Bearer\",\"expires_in\":3600000}";
        var userInfoJson = $"{{\"Email\":\"{email}\",\"ZUID\":\"{zuid}\"}}";
        return FakeHttpMessageHandler.ToFactory(req =>
            req.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(tokenJson, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent(userInfoJson, Encoding.UTF8, "application/json") });
    }

    private AccountService CreateService()
        => new AccountService(
            _store,
            _auth,
            MakeHttpFactory(),
            NullLogger<AccountService>.Instance,
            new FakeOAuthBrowserFlow());

    private void SeedAccount(string name, string email = "alice@acme.com", string zuidstring = "Z100")
    {
        _store.AddAccount(new AccountEntry
        {
            Name = name,
            Email = email,
            Zuid = zuidstring,
            Dc = "us",
            IsDefault = true,
            Scopes = [],
        });
        _auth.SetToken(name, $"tok-{name}");
    }

    // ─── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RenameByName_UpdatesAccountsJson()
    {
        SeedAccount("alice");
        var service = CreateService();

        var (oldName, newName) = await service.RenameAccountAsync("alice", null, null, "alice-work");

        Assert.Equal("alice", oldName);
        Assert.Equal("alice-work", newName);

        var renamed = await _store.FindAsync("alice-work");
        Assert.NotNull(renamed);

        var old = await _store.FindAsync("alice");
        Assert.Null(old);
    }

    [Fact]
    public async Task RenameByName_CallsRenameTokenAsync()
    {
        SeedAccount("alice");
        var service = CreateService();

        await service.RenameAccountAsync("alice", null, null, "alice-work");

        Assert.Equal(1, _auth.RenameTokenCallCount);
        Assert.Equal("alice", _auth.LastRenameOldName);
        Assert.Equal("alice-work", _auth.LastRenameNewName);
    }

    [Fact]
    public async Task RenameByEmail_ResolvesViaEmail()
    {
        SeedAccount("alice", "alice@acme.com");
        var service = CreateService();

        var (oldName, newName) = await service.RenameAccountAsync(null, "alice@acme.com", null, "alice2");

        Assert.Equal("alice", oldName);
        Assert.Equal("alice2", newName);
    }

    [Fact]
    public async Task RenameByZuid_ResolvesViaZuid()
    {
        SeedAccount("alice", "alice@acme.com", "Z100");
        var service = CreateService();

        var (oldName, newName) = await service.RenameAccountAsync(null, null, "Z100", "alice-new");

        Assert.Equal("alice", oldName);
        Assert.Equal("alice-new", newName);
    }

    // ─── Name collision ──────────────────────────────────────────────────────

    [Fact]
    public async Task RenameToExistingName_ThrowsAccountAlreadyExists()
    {
        SeedAccount("alice");
        _store.AddAccount(new AccountEntry { Name = "bob", Email = "bob@acme.com", Zuid = "Z200", Dc = "us", IsDefault = false, Scopes = [] });
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.RenameAccountAsync("alice", null, null, "bob"));

        Assert.Equal(ErrorCodes.ACCOUNT_ALREADY_EXISTS, ex.Code);
    }

    // ─── Invalid args ────────────────────────────────────────────────────────

    [Fact]
    public async Task RenameWithNoIdentifier_ThrowsInvalidArgs()
    {
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.RenameAccountAsync(null, null, null, "newname"));

        Assert.Equal(ErrorCodes.INVALID_ARGS, ex.Code);
    }

    [Fact]
    public async Task RenameWithMultipleIdentifiers_ThrowsDuplicateIdentifier()
    {
        SeedAccount("alice");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.RenameAccountAsync("alice", "alice@acme.com", null, "newname"));

        Assert.Equal(ErrorCodes.DUPLICATE_IDENTIFIER, ex.Code);
    }

    [Fact]
    public async Task RenameWithInvalidCharsInNewName_ThrowsInvalidArgs()
    {
        SeedAccount("alice");
        var service = CreateService();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.RenameAccountAsync("alice", null, null, "bad/name"));

        Assert.Equal(ErrorCodes.INVALID_ARGS, ex.Code);
    }

    // ─── Keychain partial failure ─────────────────────────────────────────────

    [Fact]
    public async Task RenameTokenAsync_DeleteFails_ThrowsAccountRenameFailed()
    {
        SeedAccount("alice");
        var service = CreateService();
        // Simulate delete failure on OAuthProvider.RenameTokenAsync by injecting exception
        _auth.RenameTokenException = new ZapiCliException(
            "Credentials written for 'alice-work' but failed to remove old key 'alice'.",
            ErrorCodes.ACCOUNT_RENAME_FAILED,
            exitCode: 1);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.RenameAccountAsync("alice", null, null, "alice-work"));

        Assert.Equal(ErrorCodes.ACCOUNT_RENAME_FAILED, ex.Code);
    }
}
