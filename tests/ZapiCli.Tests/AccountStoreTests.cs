using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Tests;

public sealed class AccountStoreTests
{
    [Fact]
    public async Task SaveThenLoad_RoundTrips_AccountEntry()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var store = new AccountStore(NullLogger<AccountStore>.Instance, dir);
            var entry = new AccountEntry
            {
                Name = "test-account",
                Domain = "zoho.com",
                Email = "test@zoho.com",
                TokenType = "pat",
                Scopes = ["ZohoAPI.all"],
                IsDefault = true,
                NeedsReauth = false
            };
            var root = new AccountsRoot { Accounts = [entry] };

            await store.SaveAsync(root);
            var loaded = await store.LoadAsync();

            Assert.Single(loaded.Accounts);
            var a = loaded.Accounts[0];
            Assert.Equal("test-account", a.Name);
            Assert.Equal("zoho.com", a.Domain);
            Assert.Equal("test@zoho.com", a.Email);
            Assert.Equal("pat", a.TokenType);
            Assert.Equal(["ZohoAPI.all"], a.Scopes);
            Assert.True(a.IsDefault);
            Assert.False(a.NeedsReauth);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_AccountsJson_ContainsNoTokenField()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var store = new AccountStore(NullLogger<AccountStore>.Instance, dir);
            var root = new AccountsRoot
            {
                Accounts = [new AccountEntry { Name = "work", TokenType = "pat", IsDefault = true }]
            };

            await store.SaveAsync(root);

            var json = await File.ReadAllTextAsync(Path.Combine(dir, "accounts.json"));
            using var doc = JsonDocument.Parse(json);
            var account = doc.RootElement.GetProperty("accounts")[0];

            // Confirm structural fields are present
            Assert.True(account.TryGetProperty("name", out _));
            Assert.True(account.TryGetProperty("token_type", out _));

            // Token values must NEVER appear in accounts.json (ADR-0004)
            Assert.False(account.TryGetProperty("token", out _), "Token field must not be stored in accounts.json");
            Assert.False(account.TryGetProperty("token_value", out _), "Token value must not be stored in accounts.json");
            Assert.False(account.TryGetProperty("pat_token", out _), "PAT token must not be stored in accounts.json");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task FindAsync_ReturnsAccount_WhenNameMatches()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var store = new AccountStore(NullLogger<AccountStore>.Instance, dir);
            var root = new AccountsRoot
            {
                Accounts =
                [
                    new AccountEntry { Name = "dev", TokenType = "pat", IsDefault = true },
                    new AccountEntry { Name = "prod", TokenType = "pat", IsDefault = false }
                ]
            };
            await store.SaveAsync(root);

            var found = await store.FindAsync("prod");

            Assert.NotNull(found);
            Assert.Equal("prod", found.Name);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task FindAsync_ReturnsNull_WhenNameNotFound()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var store = new AccountStore(NullLogger<AccountStore>.Instance, dir);
            await store.SaveAsync(new AccountsRoot());

            var found = await store.FindAsync("nonexistent");

            Assert.Null(found);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task GetDefaultAsync_ThrowsNoDefaultAccount_WhenNoneIsDefault()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var store = new AccountStore(NullLogger<AccountStore>.Instance, dir);
            var root = new AccountsRoot
            {
                Accounts = [new AccountEntry { Name = "dev", TokenType = "pat", IsDefault = false }]
            };
            await store.SaveAsync(root);

            var ex = await Assert.ThrowsAsync<ZapiCliException>(() => store.GetDefaultAsync());

            Assert.Equal(ErrorCodes.NoDefaultAccount, ex.Code);
            Assert.Equal(1, ex.ExitCode);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var store = new AccountStore(NullLogger<AccountStore>.Instance, dir);
            var root = await store.LoadAsync();
            Assert.Empty(root.Accounts);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
