namespace ZapiCli.Core.Accounts;

/// <summary>Root object for accounts.json.</summary>
public sealed record AccountsRoot
{
    public List<AccountEntry> Accounts { get; init; } = [];
}
