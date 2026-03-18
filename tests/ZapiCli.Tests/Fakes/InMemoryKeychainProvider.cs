using ZapiCli.Keychain;

namespace ZapiCli.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IKeychainProvider"/> for use in unit tests.
/// No OS keychain or filesystem access is performed. Thread-safe.
/// </summary>
public sealed class InMemoryKeychainProvider : IKeychainProvider
{
    private readonly Dictionary<string, string> _store = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _store.TryGetValue(key, out var value);
            return Task.FromResult(value);
        }
    }

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _store[key] = value;
        }
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _store.Remove(key);
        }
        return Task.CompletedTask;
    }
}
