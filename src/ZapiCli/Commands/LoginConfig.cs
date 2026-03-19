using System.Text.Json.Serialization;

namespace ZapiCli.Commands;

/// <summary>
/// CLI-layer POCO for the <c>--file</c> flag on <c>account login</c>.
/// Deserializes a JSON file using kebab-case field names.
/// Only consumed by <c>AccountLoginCommand</c> — never reaches <c>AccountService</c>.
/// </summary>
public sealed class LoginConfig
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("client-id")]
    public string? ClientId { get; init; }

    [JsonPropertyName("client-secret")]
    public string? ClientSecret { get; init; }

    [JsonPropertyName("scope")]
    public string[]? Scope { get; init; }

    [JsonPropertyName("dc")]
    public string Dc { get; init; } = "us";

    /// <summary>
    /// Validates that all required fields are populated.
    /// </summary>
    /// <returns>List of human-readable error strings. Empty list means all required fields are present.</returns>
    public List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(Name))
            errors.Add("'name' is required but was not provided.");
        if (string.IsNullOrWhiteSpace(ClientId))
            errors.Add("'client-id' is required but was not provided.");
        if (string.IsNullOrWhiteSpace(ClientSecret))
            errors.Add("'client-secret' is required but was not provided.");
        if (Scope is null || Scope.Length == 0)
            errors.Add("'scope' is required but was not provided.");

        return errors;
    }
}
