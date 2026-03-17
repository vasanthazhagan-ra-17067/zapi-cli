using Xunit;
using ZapiCli.Core;
using ZapiCli.Core.Security;

namespace ZapiCli.Tests;

public sealed class ZohoCorpGuardTests
{
    [Theory]
    [InlineData("user@zohocorp.com")]
    [InlineData("user@zohocorp.eu")]
    [InlineData("user@zohocorp.in")]
    [InlineData("user@zohocorp.com.au")]
    public void AssertNotZohoCorp_ThrowsAccountDomainBlocked_ForZohoCorpDomains(string email)
    {
        var ex = Assert.Throws<ZapiCliException>(() => ZohoCorpGuard.AssertNotZohoCorp(email));
        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Theory]
    [InlineData("ADMIN@ZOHOCORP.COM")]
    [InlineData("admin@ZohoCorp.EU")]
    public void AssertNotZohoCorp_IsCaseInsensitive(string email)
    {
        var ex = Assert.Throws<ZapiCliException>(() => ZohoCorpGuard.AssertNotZohoCorp(email));
        Assert.Equal(ErrorCodes.AccountDomainBlocked, ex.Code);
    }

    [Fact]
    public void AssertNotZohoCorp_DoesNotThrow_ForZohoCom()
    {
        var ex = Record.Exception(() => ZohoCorpGuard.AssertNotZohoCorp("user@zoho.com"));
        Assert.Null(ex);
    }

    [Fact]
    public void AssertNotZohoCorp_DoesNotThrow_ForNullEmail()
    {
        var ex = Record.Exception(() => ZohoCorpGuard.AssertNotZohoCorp(null));
        Assert.Null(ex);
    }

    [Fact]
    public void AssertNotZohoCorp_DoesNotThrow_ForEmailWithNoAtSign()
    {
        var ex = Record.Exception(() => ZohoCorpGuard.AssertNotZohoCorp("notanemail"));
        Assert.Null(ex);
    }
}
