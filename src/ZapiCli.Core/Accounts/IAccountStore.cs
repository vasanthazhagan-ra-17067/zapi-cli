namespace ZapiCli.Core.Accounts;

public interface IAccountStore
{
    Task<AccountsRoot> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AccountsRoot root, CancellationToken ct = default);
    Task<AccountEntry?> FindAsync(string name, CancellationToken ct = default);
    Task<AccountEntry> GetDefaultAsync(CancellationToken ct = default);
}
