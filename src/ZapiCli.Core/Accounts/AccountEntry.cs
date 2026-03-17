namespace ZapiCli.Core.Accounts;

/// <summary>
/// Represents a single configured account. Token values are never stored here —
/// they live exclusively in the OS keychain via <see cref="Auth.IAuthProvider"/>.
/// </summary>
public sealed record AccountEntry
{
    public required string Name { get; init; }
    public string Domain { get; init; } = "zoho.com";
    public string? Email { get; init; }
    public List<string> Scopes { get; init; } = [];
    public required string TokenType { get; init; }
    public bool IsDefault { get; init; }
    public bool NeedsReauth { get; init; }
}
