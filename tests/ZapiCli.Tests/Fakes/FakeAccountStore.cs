using ZapiCli.Core;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IAccountStore"/> for unit tests.
/// Tracks save calls so tests can assert persistence behaviour.
/// </summary>
public sealed class FakeAccountStore : IAccountStore
{
    private readonly List<AccountEntry> _accounts = new();

    public int SaveCallCount { get; private set; }

    public void AddAccount(AccountEntry account) => _accounts.Add(account);

    public Task<AccountsRoot> LoadAsync(CancellationToken ct = default)
        => Task.FromResult(new AccountsRoot { Accounts = new List<AccountEntry>(_accounts) });

    public Task SaveAsync(AccountsRoot root, CancellationToken ct = default)
    {
        SaveCallCount++;
        _accounts.Clear();
        _accounts.AddRange(root.Accounts);
        return Task.CompletedTask;
    }

    public Task<AccountEntry?> FindAsync(string name, CancellationToken ct = default)
        => Task.FromResult(
            _accounts.FirstOrDefault(a => a.Name.Equals(name, StringComparison.Ordinal)));

    public Task<AccountEntry> GetDefaultAsync(CancellationToken ct = default)
    {
        var account = _accounts.FirstOrDefault(a => a.IsDefault) ?? _accounts.FirstOrDefault();
        if (account is null)
            throw new ZapiCliException(
                "No default account configured.",
                ErrorCodes.NO_DEFAULT_ACCOUNT);
        return Task.FromResult(account);
    }
}
