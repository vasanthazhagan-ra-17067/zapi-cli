using ZapiCli.Core.Auth;
using ZapiCli.Core.Security;

namespace ZapiCli.Core.Accounts;

/// <summary>
/// Orchestrates account management operations on behalf of the account command group.
/// Business rules enforced here:
/// <list type="bullet">
///   <item>ZohoCorp block fires BEFORE any keychain write (ADR-0005).</item>
///   <item>Token values are never stored in accounts.json — only in keychain.</item>
///   <item>First account added automatically becomes the default.</item>
/// </list>
/// </summary>
public sealed class AccountService
{
    private readonly IAccountStore _store;
    private readonly IAuthProvider _auth;
    private readonly IUserInfoService _userInfo;

    public AccountService(IAccountStore store, IAuthProvider auth, IUserInfoService userInfo)
    {
        _store = store;
        _auth = auth;
        _userInfo = userInfo;
    }

    /// <summary>
    /// Adds a new account.
    /// <para>
    /// Flow: fetch email from user-info API → ZohoCorp block check → store token in keychain
    /// → append AccountEntry (no token) to accounts.json.
    /// </para>
    /// </summary>
    /// <returns>The name of the newly created account.</returns>
    public async Task<string> AddAsync(
        string name,
        string domain,
        string token,
        string tokenType,
        CancellationToken ct = default)
    {
        var email = await _userInfo.GetEmailAsync(domain, token, ct);

        // Guard fires BEFORE any persistent write (ADR-0005).
        ZohoCorpGuard.AssertNotZohoCorp(email);

        var root = await _store.LoadAsync(ct);

        if (root.Accounts.Exists(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ZapiCliException(
                $"Account '{name}' already exists. Use 'account remove {name}' first.",
                ErrorCodes.AccountAlreadyExists,
                exitCode: 1);

        // Write to keychain only after all pre-condition checks pass.
        await _auth.StoreTokenAsync(name, token, ct);

        root.Accounts.Add(new AccountEntry
        {
            Name = name,
            Domain = domain,
            Email = email,
            TokenType = tokenType,
            IsDefault = root.Accounts.Count == 0,   // first account auto-defaults
        });

        await _store.SaveAsync(root, ct);
        return name;
    }

    /// <summary>Returns all accounts with token masked as "***".</summary>
    public async Task<IReadOnlyList<AccountOutputDto>> ListAsync(CancellationToken ct = default)
    {
        var root = await _store.LoadAsync(ct);
        return root.Accounts.ConvertAll(AccountOutputDto.FromEntry);
    }

    /// <summary>Removes an account: clears keychain entry then removes from accounts.json.</summary>
    public async Task RemoveAsync(string name, CancellationToken ct = default)
    {
        var root = await _store.LoadAsync(ct);
        var idx = root.Accounts.FindIndex(
            a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (idx < 0)
            throw new ZapiCliException(
                $"Account '{name}' not found.",
                ErrorCodes.AccountNotFound,
                exitCode: 1);

        await _auth.ClearTokenAsync(name, ct);
        root.Accounts.RemoveAt(idx);
        await _store.SaveAsync(root, ct);
    }

    /// <summary>Returns a single account with token masked as "***".</summary>
    public async Task<AccountOutputDto> ShowAsync(string name, CancellationToken ct = default)
    {
        var entry = await _store.FindAsync(name, ct)
            ?? throw new ZapiCliException(
                $"Account '{name}' not found.",
                ErrorCodes.AccountNotFound,
                exitCode: 1);

        return AccountOutputDto.FromEntry(entry);
    }

    /// <summary>
    /// Sets <paramref name="name"/> as the default account and clears the flag on all others.
    /// </summary>
    public async Task SetDefaultAsync(string name, CancellationToken ct = default)
    {
        var root = await _store.LoadAsync(ct);

        if (!root.Accounts.Exists(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new ZapiCliException(
                $"Account '{name}' not found.",
                ErrorCodes.AccountNotFound,
                exitCode: 1);

        var updated = root.Accounts
            .Select(a => a with
            {
                IsDefault = a.Name.Equals(name, StringComparison.OrdinalIgnoreCase),
            })
            .ToList();

        await _store.SaveAsync(root with { Accounts = updated }, ct);
    }
}
