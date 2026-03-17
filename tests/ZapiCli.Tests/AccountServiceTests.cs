using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Auth;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

/// <summary>
/// Unit tests for <see cref="AccountService"/> covering the acceptance criteria
/// from Story 04.
/// </summary>
public sealed class AccountServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly AccountStore _store;
    private readonly InMemoryKeychainProvider _keychain;
    private readonly PatAuthProvider _auth;

    public AccountServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _store = new AccountStore(NullLogger<AccountStore>.Instance, _dir);
        _keychain = new InMemoryKeychainProvider();
        _auth = new PatAuthProvider(_keychain);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private AccountService BuildService(string? email) =>
        new(_store, _auth, new FakeUserInfoService(email));

    // ─── account add ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_WithZohoCorpEmail_ThrowsAccountDomainBlocked_BeforeKeychainWrite()
    {
        var svc = BuildService("employee@zohocorp.com");

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => svc.AddAsync("work", "zoho.com", "my-pat-token", "pat"));

        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
        Assert.Equal(1, ex.ExitCode);

        // Token must NOT have been written to keychain
        Assert.Null(await _keychain.GetAsync("zapi-cli:work:pat"));
    }

    [Theory]
    [InlineData("employee@zohocorp.eu")]
    [InlineData("employee@zohocorp.in")]
    [InlineData("employee@zohocorp.com.au")]
    public async Task AddAsync_ZohoCorpAllTlds_AllBlocked(string email)
    {
        var svc = BuildService(email);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => svc.AddAsync("corp-account", "zoho.com", "token", "pat"));

        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
    }

    [Fact]
    public async Task AddAsync_ZohoPersonalEmail_Succeeds()
    {
        var svc = BuildService("user@zoho.com");

        var name = await svc.AddAsync("personal", "zoho.com", "my-pat-token", "pat");

        Assert.Equal("personal", name);
        var entry = await _store.FindAsync("personal");
        Assert.NotNull(entry);
        Assert.Equal("user@zoho.com", entry.Email);
        // Token must be in keychain, not in accounts.json
        Assert.Equal("my-pat-token", await _keychain.GetAsync("zapi-cli:personal:pat"));
    }

    [Fact]
    public async Task AddAsync_FirstAccount_BecomesDefaultAutomatically()
    {
        var svc = BuildService("user@zoho.com");

        await svc.AddAsync("first", "zoho.com", "token1", "pat");

        var entry = await _store.FindAsync("first");
        Assert.NotNull(entry);
        Assert.True(entry.IsDefault);
    }

    [Fact]
    public async Task AddAsync_DuplicateName_ThrowsAccountAlreadyExists()
    {
        var svc = BuildService("user@zoho.com");
        await svc.AddAsync("work", "zoho.com", "token1", "pat");

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => svc.AddAsync("work", "zoho.com", "token2", "pat"));

        Assert.Equal(ErrorCodes.AccountAlreadyExists, ex.Code);
    }

    // ─── account list ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ListAsync_MasksTokenAsStars()
    {
        var svc = BuildService("user@zoho.com");
        await svc.AddAsync("work", "zoho.com", "super-secret-pat", "pat");

        var accounts = await svc.ListAsync();

        Assert.Single(accounts);
        Assert.Equal("***", accounts[0].Token);
    }

    [Fact]
    public async Task ListAsync_ReturnsAllAccounts()
    {
        var svc = BuildService("user@zoho.com");
        await svc.AddAsync("dev", "zoho.com", "tok1", "pat");

        // Use a fresh service instance but same backing store to add a second account
        var svc2 = BuildService("other@zoho.com");
        await svc2.AddAsync("prod", "zoho.com", "tok2", "pat");

        var accounts = await svc.ListAsync();

        Assert.Equal(2, accounts.Count);
        Assert.Contains(accounts, a => a.Name == "dev");
        Assert.Contains(accounts, a => a.Name == "prod");
        Assert.All(accounts, a => Assert.Equal("***", a.Token));
    }

    // ─── account remove ───────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_ClearsKeychainEntry()
    {
        var svc = BuildService("user@zoho.com");
        await svc.AddAsync("work", "zoho.com", "my-pat", "pat");

        await svc.RemoveAsync("work");

        // Token removed from keychain
        Assert.Null(await _keychain.GetAsync("zapi-cli:work:pat"));
        // Entry removed from store
        Assert.Null(await _store.FindAsync("work"));
    }

    [Fact]
    public async Task RemoveAsync_ThrowsAccountNotFound_WhenNameMissing()
    {
        var svc = BuildService(null);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => svc.RemoveAsync("nonexistent"));

        Assert.Equal(ErrorCodes.AccountNotFound, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    // ─── account show ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ShowAsync_ReturnsAccountWithMaskedToken()
    {
        var svc = BuildService("user@zoho.com");
        await svc.AddAsync("work", "zoho.com", "real-token", "pat");

        var dto = await svc.ShowAsync("work");

        Assert.Equal("work", dto.Name);
        Assert.Equal("***", dto.Token);
        Assert.Equal("user@zoho.com", dto.Email);
    }

    [Fact]
    public async Task ShowAsync_ThrowsAccountNotFound_WhenNameMissing()
    {
        var svc = BuildService(null);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => svc.ShowAsync("ghost"));

        Assert.Equal(ErrorCodes.AccountNotFound, ex.Code);
    }

    // ─── account set-default ──────────────────────────────────────────────────

    [Fact]
    public async Task SetDefaultAsync_OnlyOneAccountHasIsDefaultTrue()
    {
        var svc = BuildService("user@zoho.com");
        await svc.AddAsync("dev", "zoho.com", "tok1", "pat");
        await svc.AddAsync("prod", "zoho.com", "tok2", "pat");

        // dev starts as default (first added); switch to prod
        await svc.SetDefaultAsync("prod");

        var root = await _store.LoadAsync();
        var dev = root.Accounts.First(a => a.Name == "dev");
        var prod = root.Accounts.First(a => a.Name == "prod");

        Assert.False(dev.IsDefault, "dev should no longer be default");
        Assert.True(prod.IsDefault, "prod should be the new default");
    }

    [Fact]
    public async Task SetDefaultAsync_ThrowsAccountNotFound_WhenNameMissing()
    {
        var svc = BuildService(null);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => svc.SetDefaultAsync("phantom"));

        Assert.Equal(ErrorCodes.AccountNotFound, ex.Code);
    }
}
