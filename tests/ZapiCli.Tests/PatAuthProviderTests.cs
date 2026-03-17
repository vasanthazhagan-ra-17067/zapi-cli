using Xunit;
using ZapiCli.Core;
using ZapiCli.Core.Auth;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

public sealed class PatAuthProviderTests
{
    [Fact]
    public async Task GetTokenAsync_ReturnsStoredToken()
    {
        var keychain = new InMemoryKeychainProvider();
        var provider = new PatAuthProvider(keychain);

        await provider.StoreTokenAsync("dev", "my-secret-token");
        var token = await provider.GetTokenAsync("dev");

        Assert.Equal("my-secret-token", token);
    }

    [Fact]
    public async Task GetTokenAsync_ThrowsAuthFailure_ForMissingToken()
    {
        var keychain = new InMemoryKeychainProvider();
        var provider = new PatAuthProvider(keychain);

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => provider.GetTokenAsync("nonexistent"));

        Assert.Equal(ErrorCodes.AuthFailure, ex.Code);
        Assert.Equal(2, ex.ExitCode);
    }

    [Fact]
    public async Task StoreTokenAsync_UsesCorrectKeyFormat()
    {
        var keychain = new InMemoryKeychainProvider();
        var provider = new PatAuthProvider(keychain);

        await provider.StoreTokenAsync("myaccount", "supersecret");

        // Must match "zapi-cli:<accountName>:pat" (ADR-0004)
        var stored = await keychain.GetAsync("zapi-cli:myaccount:pat");
        Assert.Equal("supersecret", stored);
    }

    [Fact]
    public async Task ClearTokenAsync_RemovesTokenFromKeychain()
    {
        var keychain = new InMemoryKeychainProvider();
        var provider = new PatAuthProvider(keychain);

        await provider.StoreTokenAsync("dev", "my-token");
        await provider.ClearTokenAsync("dev");

        var remaining = await keychain.GetAsync("zapi-cli:dev:pat");
        Assert.Null(remaining);
    }
}
