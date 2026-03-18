using System.Text.Json.Serialization;

namespace ZapiCli.Core.Accounts;

/// <summary>
/// Projection used by <c>account show</c>. Exposes all account fields.
/// The <see cref="Token"/> property is unconditionally set to <c>"***"</c> — real credentials
/// are never included in output (ADR-0008).
/// </summary>
public sealed record AccountShowView
{
    public required string Name { get; init; }
    public required string Dc { get; init; }
    public string? Email { get; init; }

    public string? Zuid { get; init; }

    public List<string> Scopes { get; init; } = [];
    public bool IsDefault { get; init; }
    public bool NeedsReauth { get; init; }

    /// <summary>Always "***" — the real access token is never emitted in CLI output.</summary>
    public string Token { get; init; } = "***";
}
