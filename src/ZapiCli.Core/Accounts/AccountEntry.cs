namespace ZapiCli.Core.Accounts;

/// <summary>
/// Persisted account record. Credentials (access_token, client_id, client_secret)
/// are never stored here — they live in the OS keychain only.
/// </summary>
public sealed record AccountEntry
{
    public required string Name { get; init; }

    /// <summary>Datacenter short name: us | eu | in | au | cn | jp | sa | uk | ca</summary>
    public required string Dc { get; init; }

    public string? Email { get; init; }

    public string? Zuid { get; init; }

    public List<string> Scopes { get; init; } = [];
    public bool IsDefault { get; init; }
    public bool NeedsReauth { get; init; }
}
