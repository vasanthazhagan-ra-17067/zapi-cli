using Xunit;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

/// <summary>
/// Unit tests for <see cref="ScopeService"/> covering Story 06 acceptance criteria.
/// </summary>
public sealed class ScopeServiceTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static AccountEntry MakeAccount(
        string name,
        string email = "user@zoho.com",
        bool isDefault = true,
        string[]? scopes = null) =>
        new()
        {
            Name = name,
            TokenType = "pat",
            Email = email,
            IsDefault = isDefault,
            Scopes = scopes is null ? [] : [.. scopes],
        };

    private static (ScopeService service, FakeAccountStore store) Build(
        params AccountEntry[] entries)
    {
        var store = new FakeAccountStore(new AccountsRoot { Accounts = [.. entries] });
        return (new ScopeService(store), store);
    }

    // ─── scope add ───────────────────────────────────────────────────────────

    [Fact]
    public async Task AddScopeAsync_AppendsScope_AndReturnsUpdatedList()
    {
        var (service, _) = Build(MakeAccount("prod"));

        var result = await service.AddScopeAsync(null, "ZohoDesk.Tickets.READ");

        Assert.Single(result);
        Assert.Contains("ZohoDesk.Tickets.READ", result);
    }

    [Fact]
    public async Task AddScopeAsync_PersistsNeedsReauthTrue()
    {
        var (service, store) = Build(MakeAccount("prod"));

        await service.AddScopeAsync(null, "ZohoDesk.Tickets.READ");

        var saved = (await store.LoadAsync()).Accounts.Single();
        Assert.True(saved.NeedsReauth);
    }

    [Fact]
    public async Task AddScopeAsync_DuplicateScope_DoesNotAddDuplicate()
    {
        var (service, store) = Build(MakeAccount("prod", scopes: ["ZohoDesk.Tickets.READ"]));

        var result = await service.AddScopeAsync(null, "ZohoDesk.Tickets.READ");

        Assert.Single(result);
        // no extra save — original account was returned unchanged
        var saved = (await store.LoadAsync()).Accounts.Single();
        Assert.Single(saved.Scopes);
    }

    [Fact]
    public async Task AddScopeAsync_ZohoCorpAccount_ThrowsAccountDomainBlocked()
    {
        var (service, _) = Build(MakeAccount("corp", email: "emp@zohocorp.com"));

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.AddScopeAsync(null, "ZohoDesk.Tickets.READ"));

        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    // ─── scope remove ─────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveScopeAsync_RemovesScope_AndReturnsRemainingScopes()
    {
        var (service, store) = Build(
            MakeAccount("prod", scopes: ["ZohoDesk.Tickets.READ", "ZohoCRM.Leads.READ"]));

        var result = await service.RemoveScopeAsync(null, "ZohoDesk.Tickets.READ");

        Assert.DoesNotContain("ZohoDesk.Tickets.READ", result);
        Assert.Contains("ZohoCRM.Leads.READ", result);

        var saved = (await store.LoadAsync()).Accounts.Single();
        Assert.True(saved.NeedsReauth);
        Assert.Single(saved.Scopes);
    }

    [Fact]
    public async Task RemoveScopeAsync_MissingScope_ThrowsScopeNotFound()
    {
        var (service, _) = Build(MakeAccount("prod"));

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.RemoveScopeAsync(null, "ZohoDesk.Tickets.READ"));

        Assert.Equal(ErrorCodes.ScopeNotFound, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public async Task RemoveScopeAsync_ZohoCorpAccount_ThrowsAccountDomainBlocked()
    {
        var (service, _) = Build(
            MakeAccount("corp", email: "emp@zohocorp.com", scopes: ["ZohoDesk.Tickets.READ"]));

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.RemoveScopeAsync(null, "ZohoDesk.Tickets.READ"));

        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
    }

    // ─── scope list ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListScopesAsync_EmptyScopes_ReturnsEmptyListWithoutError()
    {
        var (service, _) = Build(MakeAccount("prod"));

        var result = await service.ListScopesAsync(null);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ListScopesAsync_ReturnsAllRegisteredScopes()
    {
        var (service, _) = Build(MakeAccount("prod", scopes: ["ScopeA", "ScopeB"]));

        var result = await service.ListScopesAsync(null);

        Assert.Equal(2, result.Count);
        Assert.Contains("ScopeA", result);
        Assert.Contains("ScopeB", result);
    }

    [Fact]
    public async Task ListScopesAsync_ZohoCorpAccount_ThrowsAccountDomainBlocked()
    {
        var (service, _) = Build(MakeAccount("corp", email: "emp@zohocorp.com"));

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.ListScopesAsync(null));

        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
    }

    // ─── account resolution ───────────────────────────────────────────────────

    [Fact]
    public async Task AddScopeAsync_ExplicitAccountName_UsesNamedAccount()
    {
        var (service, store) = Build(
            MakeAccount("prod", isDefault: true),
            MakeAccount("staging", isDefault: false));

        await service.AddScopeAsync("staging", "ZohoDesk.Tickets.READ");

        var root = await store.LoadAsync();
        var staging = root.Accounts.Single(a => a.Name == "staging");
        var prod = root.Accounts.Single(a => a.Name == "prod");

        Assert.Single(staging.Scopes);
        Assert.Empty(prod.Scopes);         // default account was NOT mutated
    }

    [Fact]
    public async Task AddScopeAsync_NoDefaultAccount_ThrowsNoDefaultAccount()
    {
        var store = new FakeAccountStore(new AccountsRoot { Accounts = [] });
        var service = new ScopeService(store);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.AddScopeAsync(null, "ZohoDesk.Tickets.READ"));

        Assert.Equal(ErrorCodes.NoDefaultAccount, ex.Code);
    }

    [Fact]
    public async Task AddScopeAsync_UnknownExplicitAccount_ThrowsAccountNotFound()
    {
        var (service, _) = Build(MakeAccount("prod"));

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => service.AddScopeAsync("nonexistent", "ZohoDesk.Tickets.READ"));

        Assert.Equal(ErrorCodes.AccountNotFound, ex.Code);
    }
}
