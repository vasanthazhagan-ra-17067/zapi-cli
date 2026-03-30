using System.Text.Json.Serialization;

namespace ZapiCli.Core.Accounts;

/// <summary>
/// Projection used by <c>account show</c>. Exposes all account fields.
/// Credentials are never included in output (ADR-0008).
/// </summary>
public sealed record AccountShowView
{
    public required string Name { get; init; }
    public required string Dc { get; init; }
    public string? Email { get; init; }

    public string? Zuid { get; init; }

    public List<string> Scopes { get; init; } = [];
    public bool IsDefault { get; init; }
}
