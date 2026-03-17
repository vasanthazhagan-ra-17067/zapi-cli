namespace ZapiCli.Core.Accounts;

/// <summary>
/// Output DTO for account commands. The <see cref="Token"/> field is always
/// masked as <c>"***"</c> — the actual token value lives only in the OS keychain.
/// </summary>
public sealed record AccountOutputDto
{
    public required string Name { get; init; }
    public string Domain { get; init; } = "zoho.com";
    public string? Email { get; init; }
    public List<string> Scopes { get; init; } = [];
    public required string TokenType { get; init; }
    public string Token { get; init; } = "***";
    public bool IsDefault { get; init; }
    public bool NeedsReauth { get; init; }

    public static AccountOutputDto FromEntry(AccountEntry entry) => new()
    {
        Name = entry.Name,
        Domain = entry.Domain,
        Email = entry.Email,
        Scopes = entry.Scopes,
        TokenType = entry.TokenType,
        IsDefault = entry.IsDefault,
        NeedsReauth = entry.NeedsReauth,
    };
}
