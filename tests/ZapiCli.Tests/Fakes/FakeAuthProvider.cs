using ZapiCli.Core;
using ZapiCli.Core.Auth;

namespace ZapiCli.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IAuthProvider"/> for unit tests.
/// Tracks how many times keychain-write operations are called so tests can assert
/// that no write occurred for security-block scenarios.
/// </summary>
public sealed class FakeAuthProvider : IAuthProvider
{
    private readonly Dictionary<string, string> _tokens = new(StringComparer.Ordinal);

    public int StoreTokenCallCount { get; private set; }
    public int ClearTokenCallCount { get; private set; }
    public int RefreshTokenCallCount { get; private set; }

    /// <summary>Directly injects a token into the fake keychain (test helper).</summary>
    public void SetToken(string accountName, string token) => _tokens[accountName] = token;

    public Task<string> GetTokenAsync(string accountName, CancellationToken ct = default)
    {
        if (_tokens.TryGetValue(accountName, out var token))
            return Task.FromResult(token);

        throw new ZapiCliException(
            $"No credentials found in keychain for account '{accountName}'.",
            ErrorCodes.KEYCHAIN_ERROR,
            exitCode: 2);
    }

    public Task StoreTokenAsync(
        string accountName,
        string accessToken,
        string refreshToken,
        string clientId,
        string clientSecret,
        CancellationToken ct = default)
    {
        StoreTokenCallCount++;
        _tokens[accountName] = accessToken;
        return Task.CompletedTask;
    }

    public Task ClearTokenAsync(string accountName, CancellationToken ct = default)
    {
        ClearTokenCallCount++;
        _tokens.Remove(accountName);
        return Task.CompletedTask;
    }

    public Task<string> RefreshTokenAsync(
        string accountName,
        IEnumerable<string> scopes,
        string dc,
        CancellationToken ct = default)
    {
        RefreshTokenCallCount++;
        var newToken = $"refreshed_{accountName}";
        _tokens[accountName] = newToken;
        return Task.FromResult(newToken);
    }

    public int GetScopeEnhancementTokenCallCount { get; private set; }
    public string PresetEnhanceToken { get; set; } = "fake-enhance-token";
    public string PresetEnhanceClientId { get; set; } = "fake-client-id";

    public Task<(string EnhanceToken, string ClientId)> GetScopeEnhancementTokenAsync(
        string accountName,
        string dc,
        CancellationToken ct = default)
    {
        GetScopeEnhancementTokenCallCount++;
        return Task.FromResult((PresetEnhanceToken, PresetEnhanceClientId));
    }
}
