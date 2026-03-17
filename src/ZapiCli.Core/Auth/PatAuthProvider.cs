using ZapiCli.Keychain;

namespace ZapiCli.Core.Auth;

/// <summary>
/// PAT-based auth provider (v1). Stores and retrieves tokens via the OS keychain.
/// Key format: <c>zapi-cli:&lt;accountName&gt;:pat</c> (ADR-0004).
/// </summary>
public sealed class PatAuthProvider : IAuthProvider
{
    private readonly IKeychainProvider _keychain;

    public PatAuthProvider(IKeychainProvider keychain) => _keychain = keychain;

    private static string Key(string accountName) => $"zapi-cli:{accountName}:pat";

    public async Task<string> GetTokenAsync(string accountName, CancellationToken ct = default)
    {
        var token = await _keychain.GetAsync(Key(accountName), ct);
        if (token is null)
            throw new ZapiCliException(
                $"Authentication failed for account '{accountName}'. Token not found in keychain.",
                ErrorCodes.AuthFailure,
                exitCode: 2);
        return token;
    }

    public Task StoreTokenAsync(string accountName, string token, CancellationToken ct = default) =>
        _keychain.SetAsync(Key(accountName), token, ct);

    public Task ClearTokenAsync(string accountName, CancellationToken ct = default) =>
        _keychain.DeleteAsync(Key(accountName), ct);
}
