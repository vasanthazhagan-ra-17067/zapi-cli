using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ZapiCli.Core;

/// <summary>
/// Manages <c>cli-settings.json</c> in the platform config directory.
/// </summary>
public sealed class CliSettingsStore : ICliSettingsStore
{
    private readonly string _filePath;
    private readonly ILogger<CliSettingsStore> _logger;

    public CliSettingsStore(string configDir, ILogger<CliSettingsStore> logger)
    {
        _filePath = Path.Combine(configDir, "cli-settings.json");
        _logger = logger;
    }

    public async Task<CliSettings> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_filePath))
            return new CliSettings();

        try
        {
            var json = await File.ReadAllTextAsync(_filePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<CliSettings>(json, OutputJsonOptions.Shared)
                   ?? new CliSettings();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read cli-settings.json; using defaults.");
            return new CliSettings();
        }
    }

    public async Task SaveAsync(CliSettings settings, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var json = JsonSerializer.Serialize(settings, OutputJsonOptions.Shared);
        await File.WriteAllTextAsync(_filePath, json, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Synchronous read used at startup before DI is built.
    /// Returns the persisted <c>EnvFile</c> path, or <c>null</c> if not configured.
    /// </summary>
    public static string? TryReadPersistedEnvFile(string configDir)
    {
        var path = Path.Combine(configDir, "cli-settings.json");
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<CliSettings>(json, OutputJsonOptions.Shared);
            return settings?.EnvFile;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Synchronous read used at startup before DI is built.
    /// Returns the persisted <c>AppDataDir</c> path, or <c>null</c> if not configured.
    /// </summary>
    public static string? TryReadPersistedAppDataDir(string configDir)
    {
        var path = Path.Combine(configDir, "cli-settings.json");
        if (!File.Exists(path)) return null;

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<CliSettings>(json, OutputJsonOptions.Shared);
            return settings?.AppDataDir;
        }
        catch
        {
            return null;
        }
    }
}
