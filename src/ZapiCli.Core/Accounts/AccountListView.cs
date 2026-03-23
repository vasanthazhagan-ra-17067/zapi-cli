namespace ZapiCli.Core.Accounts;

/// <summary>
/// Projection used by <c>account list</c> output. Never includes any token or credential values.
/// </summary>
public sealed record AccountListView
{
    public required string Name { get; init; }
    public required string Dc { get; init; }
    public string? Email { get; init; }

    public string? Zuid { get; init; }

    public bool IsDefault { get; init; }
    public int ScopeCount { get; init; }
}
