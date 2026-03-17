using ZapiCli.Core;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IAccountStore"/> for unit tests.
/// No file system access is performed.
/// </summary>
public sealed class FakeAccountStore : IAccountStore
{
    private AccountsRoot _root;

    public FakeAccountStore(AccountsRoot? root = null) =>
        _root = root ?? new AccountsRoot();

    public Task<AccountsRoot> LoadAsync(CancellationToken ct = default) =>
        Task.FromResult(_root);

    public Task SaveAsync(AccountsRoot root, CancellationToken ct = default)
    {
        _root = root;
        return Task.CompletedTask;
    }

    public Task<AccountEntry?> FindAsync(string name, CancellationToken ct = default) =>
        Task.FromResult(
            _root.Accounts.FirstOrDefault(a =>
                a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

    public Task<AccountEntry> GetDefaultAsync(CancellationToken ct = default)
    {
        var account = _root.Accounts.FirstOrDefault(a => a.IsDefault)
            ?? _root.Accounts.FirstOrDefault();
        if (account is null)
            throw new ZapiCliException(
                "No default account is configured. Run 'zapi-cli account set-default'.",
                ErrorCodes.NoDefaultAccount,
                exitCode: 1);
        return Task.FromResult(account);
    }
}
