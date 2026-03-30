using Microsoft.Extensions.Logging.Abstractions;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using Xunit;

namespace ZapiCli.Tests.Accounts;

/// <summary>
/// Tests for Story 18: AccountStore AppDataDir support + FindByEmailAsync + FindByZuidAsync.
/// </summary>
public sealed class AccountStoreAppDirTests : IDisposable
{
    private readonly string _configDir =
        Path.Combine(Path.GetTempPath(), "zapi-appdir-test-" + Guid.NewGuid().ToString("N")[..8]);
    private readonly string _appDataDir;

    public AccountStoreAppDirTests()
    {
        Directory.CreateDirectory(_configDir);
        _appDataDir = Path.Combine(_configDir, "appdata");
        Directory.CreateDirectory(_appDataDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_configDir, recursive: true); } catch { /* best-effort */ }
    }

    private AccountStore Make(string? appDataDir = null) =>
        new AccountStore(_configDir, NullLogger<AccountStore>.Instance, appDataDir);

    private static AccountEntry MakeEntry(string name, string email = "user@example.com", string? zuid = null) =>
        new AccountEntry
        {
            Name = name,
            Email = email,
            Zuid = zuid,
            Dc = "us",
            IsDefault = true,
            Scopes = [],
        };

    // ─── AppDataDir data directory routing ───────────────────────────────────

    [Fact]
    public async Task AccountsJson_WrittenToConfigDir_WhenAppDataDirIsNull()
    {
        var store = Make(appDataDir: null);
        var root = new AccountsRoot { Accounts = [MakeEntry("alice")] };
        await store.SaveAsync(root);

        Assert.True(File.Exists(Path.Combine(_configDir, "accounts.json")));
        Assert.False(File.Exists(Path.Combine(_appDataDir, "accounts.json")));
    }

    [Fact]
    public async Task AccountsJson_WrittenToAppDataDir_WhenAppDataDirIsSet()
    {
        var store = Make(appDataDir: _appDataDir);
        var root = new AccountsRoot { Accounts = [MakeEntry("bob")] };
        await store.SaveAsync(root);

        Assert.False(File.Exists(Path.Combine(_configDir, "accounts.json")));
        Assert.True(File.Exists(Path.Combine(_appDataDir, "accounts.json")));
    }

    // ─── FindByEmailAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task FindByEmailAsync_CaseInsensitive_Match()
    {
        var store = Make();
        await store.SaveAsync(new AccountsRoot
        {
            Accounts = [MakeEntry("alice", email: "Alice@Example.COM")],
        });

        var found = await store.FindByEmailAsync("alice@example.com");
        Assert.NotNull(found);
        Assert.Equal("alice", found!.Name);
    }

    [Fact]
    public async Task FindByEmailAsync_ReturnsNull_WhenNoMatch()
    {
        var store = Make();
        await store.SaveAsync(new AccountsRoot
        {
            Accounts = [MakeEntry("bob", email: "bob@example.com")],
        });

        var found = await store.FindByEmailAsync("notbob@example.com");
        Assert.Null(found);
    }

    // ─── FindByZuidAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task FindByZuidAsync_ExactMatch_Ordinal()
    {
        var store = Make();
        await store.SaveAsync(new AccountsRoot
        {
            Accounts = [MakeEntry("carol", email: "c@c.com") with { Zuid = "12345678" }],
        });

        var found = await store.FindByZuidAsync("12345678");
        Assert.NotNull(found);
        Assert.Equal("carol", found!.Name);
    }

    [Fact]
    public async Task FindByZuidAsync_DoesNotMatch_Prefix()
    {
        var store = Make();
        await store.SaveAsync(new AccountsRoot
        {
            Accounts = [MakeEntry("dave") with { Zuid = "12345678" }],
        });

        var found = await store.FindByZuidAsync("1234");  // prefix only
        Assert.Null(found);
    }

    [Fact]
    public async Task FindByZuidAsync_ReturnsNull_WhenNoMatch()
    {
        var store = Make();
        await store.SaveAsync(new AccountsRoot
        {
            Accounts = [MakeEntry("eve") with { Zuid = "11111111" }],
        });

        var found = await store.FindByZuidAsync("99999999");
        Assert.Null(found);
    }
}
