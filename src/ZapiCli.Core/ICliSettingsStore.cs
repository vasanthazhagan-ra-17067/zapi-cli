using System.Text.Json.Serialization;

namespace ZapiCli.Core;

/// <summary>
/// Persistent CLI-wide settings (<c>cli-settings.json</c> in the platform config directory).
/// </summary>
public sealed record CliSettings
{
    /// <summary>
    /// Absolute path to a <c>.env</c> file that is auto-loaded at CLI startup.
    /// When set, the CLI loads environment variables from this file before processing any command.
    /// OS environment variables always take precedence over values in the file.
    /// </summary>
    [JsonPropertyName("envFile")]
    public string? EnvFile { get; init; }

    /// <summary>
    /// Absolute path to a newline- or comma-delimited OAuth scope list file.
    /// When set, <c>account login</c> reads the scope list from this file at login time.
    /// </summary>
    [JsonPropertyName("scopeFile")]
    public string? ScopeFile { get; init; }

    /// <summary>
    /// Absolute path to an app-owned data directory used by <see cref="IAccountStore"/> as the
    /// root for <c>accounts.json</c>. When set, overrides the default platform config directory.
    /// </summary>
    [JsonPropertyName("appDataDir")]
    public string? AppDataDir { get; init; }

    /// <summary>
    /// Default directory or file path used for trace export when <c>--export-path</c> is not
    /// supplied to <c>trace session start</c>.
    /// </summary>
    [JsonPropertyName("traceDefaultExportPath")]
    public string? TraceDefaultExportPath { get; init; }
}

/// <summary>
/// Manages the <c>cli-settings.json</c> file in the platform config directory.
/// </summary>
public interface ICliSettingsStore
{
    /// <summary>Loads the CLI settings, returning defaults if the file does not exist.</summary>
    Task<CliSettings> LoadAsync(CancellationToken ct = default);

    /// <summary>Persists the CLI settings to disk.</summary>
    Task SaveAsync(CliSettings settings, CancellationToken ct = default);
}
