using ZapiCli.Core.Security;

namespace ZapiCli.Core.Accounts;

/// <summary>
/// Orchestrates scope management operations on behalf of the scope command group.
/// ZohoCorp block check is applied uniformly to all scope operations, including
/// the read-only <see cref="ListScopesAsync"/> (ADR-0005, OQ-005).
/// </summary>
public sealed class ScopeService
{
    private readonly IAccountStore _store;

    public ScopeService(IAccountStore store) => _store = store;

    /// <summary>
    /// Adds a scope to the account's scope list and sets <c>NeedsReauth = true</c>.
    /// If the scope is already present, exits without error and returns the existing list.
    /// </summary>
    public async Task<List<string>> AddScopeAsync(
        string? accountName, string scope, CancellationToken ct = default)
    {
        var (root, account) = await ResolveAsync(accountName, ct);
        ZohoCorpGuard.AssertNotZohoCorp(account.Email);

        if (account.Scopes.Contains(scope, StringComparer.Ordinal))
            return account.Scopes;

        var updated = new List<string>(account.Scopes) { scope };
        await PersistAsync(root, account.Name, updated, ct);
        return updated;
    }

    /// <summary>
    /// Removes a scope from the account and sets <c>NeedsReauth = true</c>.
    /// Throws <see cref="ZapiCliException"/> with code <c>SCOPE_NOT_FOUND</c> if the scope
    /// is not registered on the account.
    /// </summary>
    public async Task<List<string>> RemoveScopeAsync(
        string? accountName, string scope, CancellationToken ct = default)
    {
        var (root, account) = await ResolveAsync(accountName, ct);
        ZohoCorpGuard.AssertNotZohoCorp(account.Email);

        if (!account.Scopes.Contains(scope, StringComparer.Ordinal))
            throw new ZapiCliException(
                $"Scope '{scope}' is not registered on account '{account.Name}'.",
                ErrorCodes.ScopeNotFound,
                exitCode: 1);

        var updated = account.Scopes
            .Where(s => !s.Equals(scope, StringComparison.Ordinal))
            .ToList();
        await PersistAsync(root, account.Name, updated, ct);
        return updated;
    }

    /// <summary>
    /// Returns the current scope list for the account.
    /// ZohoCorp block is applied even for this read-only operation (ADR-0005, OQ-005).
    /// </summary>
    public async Task<List<string>> ListScopesAsync(
        string? accountName, CancellationToken ct = default)
    {
        var (_, account) = await ResolveAsync(accountName, ct);
        ZohoCorpGuard.AssertNotZohoCorp(account.Email);
        return account.Scopes;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private async Task PersistAsync(
        AccountsRoot root, string accountName, List<string> newScopes, CancellationToken ct)
    {
        var updatedAccounts = root.Accounts
            .Select(a => a.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase)
                ? a with { Scopes = newScopes, NeedsReauth = true }
                : a)
            .ToList();
        await _store.SaveAsync(root with { Accounts = updatedAccounts }, ct);
    }

    private async Task<(AccountsRoot, AccountEntry)> ResolveAsync(
        string? accountName, CancellationToken ct)
    {
        var root = await _store.LoadAsync(ct);

        if (accountName is not null)
        {
            var found = root.Accounts.Find(
                a => a.Name.Equals(accountName, StringComparison.OrdinalIgnoreCase))
                ?? throw new ZapiCliException(
                    $"Account '{accountName}' not found.",
                    ErrorCodes.AccountNotFound,
                    exitCode: 1);
            return (root, found);
        }

        var def = root.Accounts.Find(a => a.IsDefault)
            ?? throw new ZapiCliException(
                "No default account configured. Use 'account set-default <name>' to set one.",
                ErrorCodes.NoDefaultAccount,
                exitCode: 1);
        return (root, def);
    }
}
