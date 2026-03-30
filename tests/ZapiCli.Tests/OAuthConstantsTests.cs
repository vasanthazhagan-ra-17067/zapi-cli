using ZapiCli.Core;
using Xunit;

namespace ZapiCli.Tests;

/// <summary>
/// Guards against accidental mutation of compile-time OAuth constant values.
/// These tests exist to make the CI pipeline fail loudly if someone changes a constant
/// value without updating the Zoho Developer Console registrations and documentation.
/// </summary>
public sealed class OAuthConstantsTests
{
    [Fact]
    public void RequiredProfileScope_Equals_AaaServerProfileRead()
    {
        Assert.Equal("AaaServer.profile.READ", OAuthConstants.RequiredProfileScope);
    }

    [Fact]
    public void DefaultCallbackPort_Equals_8085()
    {
        Assert.Equal(8085, OAuthConstants.DefaultCallbackPort);
    }
}
