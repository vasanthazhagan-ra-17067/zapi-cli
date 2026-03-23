using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Auth;
using ZapiCli.Core.Security;
using ZapiCli.Keychain;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

// ── AccountStore ─────────────────────────────────────────────────────────────

public sealed class AccountStoreTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly AccountStore _store;

    public AccountStoreTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"zapi-cli-test-{Guid.NewGuid():N}");
        _store = new AccountStore(_tmpDir, NullLogger<AccountStore>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public async Task AccountStore_RoundTrip_AllFieldsMatch()
    {
        var entry = new AccountEntry
        {
            Name = "work",
            Dc = "us",
            Email = "alice@example.com",
            Zuid = "12345678",
            Scopes = ["ZohoMail.messages.READ", "ZohoCRM.modules.ALL"],
            IsDefault = true,
        };

        await _store.SaveAsync(new AccountsRoot { Accounts = [entry] });
        var loaded = await _store.FindAsync("work");

        Assert.NotNull(loaded);
        Assert.Equal("work", loaded.Name);
        Assert.Equal("us", loaded.Dc);
        Assert.Equal("alice@example.com", loaded.Email);
        Assert.Equal("12345678", loaded.Zuid);
        Assert.Equal(2, loaded.Scopes.Count);
        Assert.True(loaded.IsDefault);
    }

    [Fact]
    public async Task AccountStore_ZuidSerializesAsZuidstring()
    {
        var entry = new AccountEntry { Name = "test", Dc = "us", Zuid = "ZUID_VALUE" };
        await _store.SaveAsync(new AccountsRoot { Accounts = [entry] });

        var accountsJson = await File.ReadAllTextAsync(Path.Combine(_tmpDir, "accounts.json"));
        Assert.Contains("\"zuidstring\"", accountsJson);
        Assert.Contains("ZUID_VALUE", accountsJson);
        Assert.DoesNotContain("\"zuid\"", accountsJson);
    }

    [Fact]
    public async Task AccountStore_FilePermissions_0600OnUnix()
    {
        if (OperatingSystem.IsWindows())
            return; // Permissions test is Unix-only.

        var entry = new AccountEntry { Name = "test", Dc = "us" };
        await _store.SaveAsync(new AccountsRoot { Accounts = [entry] });

        var accountsPath = Path.Combine(_tmpDir, "accounts.json");
        var mode = File.GetUnixFileMode(accountsPath);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
    }

    [Fact]
    public async Task AccountStore_GetDefaultAsync_ReturnsIsDefaultEntry()
    {
        var root = new AccountsRoot
        {
            Accounts =
            [
                new AccountEntry { Name = "other", Dc = "eu", IsDefault = false },
                new AccountEntry { Name = "main", Dc = "us", IsDefault = true },
            ],
        };
        await _store.SaveAsync(root);

        var def = await _store.GetDefaultAsync();
        Assert.Equal("main", def.Name);
    }

    [Fact]
    public async Task AccountStore_GetDefaultAsync_ThrowsWhenEmpty()
    {
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => _store.GetDefaultAsync());

        Assert.Equal(ErrorCodes.NO_DEFAULT_ACCOUNT, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public async Task AccountStore_FindAsync_ReturnsNullForMissingName()
    {
        await _store.SaveAsync(new AccountsRoot { Accounts = [new AccountEntry { Name = "a", Dc = "us" }] });
        var result = await _store.FindAsync("nonexistent");
        Assert.Null(result);
    }

    [Fact]
    public async Task AccountStore_LoadAsync_ReturnsEmptyRootWhenFileAbsent()
    {
        var root = await _store.LoadAsync();
        Assert.NotNull(root);
        Assert.Empty(root.Accounts);
    }
}

// ── ZohoCorpGuard ─────────────────────────────────────────────────────────────

public sealed class ZohoCorpGuardTests
{
    [Theory]
    [InlineData("user@zohocorp.com")]
    [InlineData("user@zohocorp.eu")]
    [InlineData("admin@zohocorp.com.au")]
    [InlineData("hr@ZOHOCORP.COM")]
    public void ZohoCorpGuard_BlocksZohoCorpDomains(string email)
    {
        var ex = Assert.Throws<ZapiCliException>(
            () => ZohoCorpGuard.AssertNotZohoCorp(email));

        Assert.Equal(ErrorCodes.ACCOUNT_DOMAIN_BLOCKED, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("alice@zoho.com")]
    [InlineData("dev@gmail.com")]
    public void ZohoCorpGuard_PassesNonZohoCorpDomains(string email)
    {
        // Must not throw.
        ZohoCorpGuard.AssertNotZohoCorp(email);
    }

    [Fact]
    public void ZohoCorpGuard_PassesNullEmail()
    {
        // Must not throw.
        ZohoCorpGuard.AssertNotZohoCorp(null);
    }

    [Fact]
    public void ZohoCorpGuard_PassesEmailWithNoAtSign()
    {
        // Must not throw — guard only blocks confirmed ZohoCorp domains.
        ZohoCorpGuard.AssertNotZohoCorp("notanemail");
    }
}

// ── DcResolver ───────────────────────────────────────────────────────────────

public sealed class DcResolverTests
{
    [Theory]
    [InlineData("us", "https://accounts.zoho.com")]
    [InlineData("eu", "https://accounts.zoho.eu")]
    [InlineData("in", "https://accounts.zoho.in")]
    [InlineData("au", "https://accounts.zoho.com.au")]
    [InlineData("cn", "https://accounts.zoho.com.cn")]
    [InlineData("jp", "https://accounts.zoho.jp")]
    [InlineData("sa", "https://accounts.zoho.sa")]
    [InlineData("uk", "https://accounts.zoho.uk")]
    [InlineData("ca", "https://accounts.zohocloud.ca")]
    public void DcResolver_MapsAllValidDatacenters(string dc, string expectedUrl)
    {
        Assert.Equal(expectedUrl, DcResolver.GetAccountsBaseUrl(dc));
    }

    [Fact]
    public void DcResolver_ThrowsForUnknownDc()
    {
        var ex = Assert.Throws<ZapiCliException>(
            () => DcResolver.GetAccountsBaseUrl("invalid"));

        Assert.Equal(ErrorCodes.INVALID_ARGS, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }
}

// ── OAuthProvider ─────────────────────────────────────────────────────────────

public sealed class OAuthProviderTests
{
    private static OAuthProvider CreateProvider(IKeychainProvider keychain)
        => new(keychain, new FakeHttpClientFactory(), NullLogger<OAuthProvider>.Instance);

    [Fact]
    public async Task OAuthProvider_StoreAndGet_RoundTrip()
    {
        var keychain = new InMemoryKeychainProvider();
        var provider = CreateProvider(keychain);

        await provider.StoreTokenAsync("work", "tok-123", "ref-456", "cid-abc", "sec-xyz");
        var token = await provider.GetTokenAsync("work");

        Assert.Equal("tok-123", token);
    }

    [Fact]
    public async Task OAuthProvider_StoreTokenAsync_StoresJsonBlob()
    {
        var keychain = new InMemoryKeychainProvider();
        var provider = CreateProvider(keychain);

        await provider.StoreTokenAsync("acct", "myToken", "myRefreshToken", "myClientId", "myClientSecret");

        // Verify the raw JSON blob stored in keychain contains expected camelCase fields.
        var raw = await keychain.GetAsync("zapi-cli:acct:oauth");
        Assert.NotNull(raw);
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal("myToken", doc.RootElement.GetProperty("accessToken").GetString());
        Assert.Equal("myRefreshToken", doc.RootElement.GetProperty("refreshToken").GetString());
        Assert.Equal("myClientId", doc.RootElement.GetProperty("clientId").GetString());
        Assert.Equal("myClientSecret", doc.RootElement.GetProperty("clientSecret").GetString());
    }

    [Fact]
    public async Task OAuthProvider_GetTokenAsync_ThrowsKeychainError_WhenNoEntry()
    {
        var keychain = new InMemoryKeychainProvider();
        var provider = CreateProvider(keychain);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => provider.GetTokenAsync("nonexistent"));

        Assert.Equal(ErrorCodes.KEYCHAIN_ERROR, ex.Code);
        Assert.Equal(2, ex.ExitCode);
    }

    [Fact]
    public async Task OAuthProvider_ClearTokenAsync_RemovesEntry()
    {
        var keychain = new InMemoryKeychainProvider();
        var provider = CreateProvider(keychain);

        await provider.StoreTokenAsync("del", "tok", "ref-tok", "cid", "sec");
        await provider.ClearTokenAsync("del");

        var raw = await keychain.GetAsync("zapi-cli:del:oauth");
        Assert.Null(raw);
    }

    [Fact]
    public async Task OAuthProvider_ClearTokenAsync_DoesNotThrow_WhenKeyMissing()
    {
        var keychain = new InMemoryKeychainProvider();
        var provider = CreateProvider(keychain);

        // Should not throw even if the key was never stored.
        await provider.ClearTokenAsync("ghost");
    }

    // ── AccountEntry: no credential properties ────────────────────────────────

    [Fact]
    public void AccountEntry_HasNoCredentialProperties()
    {
        var type = typeof(AccountEntry);
        Assert.Null(type.GetProperty("Token"));
        Assert.Null(type.GetProperty("AccessToken"));
        Assert.Null(type.GetProperty("ClientId"));
        Assert.Null(type.GetProperty("ClientSecret"));
    }
}

// ── Fake helpers ─────────────────────────────────────────────────────────────

file sealed class FakeHttpClientFactory : IHttpClientFactory
{
    public HttpClient CreateClient(string name) =>
        throw new NotImplementedException("HTTP not needed for these unit tests.");
}
