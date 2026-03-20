using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace ZapiCli.Core.Trace;

/// <summary>
/// Manages <c>trace-config.json</c> in the platform config directory.
/// </summary>
public sealed class TraceConfigStore : ITraceConfigStore
{
    private readonly string _configFilePath;
    private readonly ILogger<TraceConfigStore> _logger;

    public TraceConfigStore(string configDir, ILogger<TraceConfigStore> logger)
    {
        _configFilePath = Path.Combine(configDir, "trace-config.json");
        _logger = logger;
    }

    public async Task<TraceConfig> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_configFilePath))
            return new TraceConfig();

        try
        {
            var json = await File.ReadAllTextAsync(_configFilePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<TraceConfig>(json, OutputJsonOptions.Shared)
                   ?? new TraceConfig();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read trace-config.json; using defaults.");
            return new TraceConfig();
        }
    }

    public async Task SaveAsync(TraceConfig config, CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(_configFilePath)!;
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(config, OutputJsonOptions.Shared);
        await File.WriteAllTextAsync(_configFilePath, json, ct).ConfigureAwait(false);
    }
}
