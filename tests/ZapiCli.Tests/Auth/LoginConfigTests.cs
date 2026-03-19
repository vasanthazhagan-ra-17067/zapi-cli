using System.Text;
using System.Text.Json;
using ZapiCli.Commands;

namespace ZapiCli.Tests.Auth;

public sealed class LoginConfigTests
{
    [Fact]
    public void Validate_ReturnsEmptyList_WhenAllRequiredFieldsPresent()
    {
        var config = new LoginConfig
        {
            Name = "myaccount",
            ClientId = "client-id-123",
            ClientSecret = "client-secret-456",
            Scope = ["ZohoAPI.Resource.READ"],
            Dc = "us",
        };

        var errors = config.Validate();

        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ReturnsError_WhenNameIsMissing()
    {
        var config = new LoginConfig
        {
            ClientId = "cid",
            ClientSecret = "cs",
            Scope = ["ZohoAPI.READ"],
        };

        var errors = config.Validate();

        Assert.Contains(errors, e => e.Contains("name"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenClientIdIsMissing()
    {
        var config = new LoginConfig
        {
            Name = "acc",
            ClientSecret = "cs",
            Scope = ["ZohoAPI.READ"],
        };

        var errors = config.Validate();

        Assert.Contains(errors, e => e.Contains("client-id"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenClientSecretIsMissing()
    {
        var config = new LoginConfig
        {
            Name = "acc",
            ClientId = "cid",
            Scope = ["ZohoAPI.READ"],
        };

        var errors = config.Validate();

        Assert.Contains(errors, e => e.Contains("client-secret"));
    }

    [Fact]
    public void Validate_ReturnsError_WhenScopeIsMissing()
    {
        var config = new LoginConfig
        {
            Name = "acc",
            ClientId = "cid",
            ClientSecret = "cs",
        };

        var errors = config.Validate();

        Assert.Contains(errors, e => e.Contains("scope"));
    }

    [Fact]
    public void Validate_ReturnsOneErrorPerMissingField_WhenAllMissing()
    {
        var config = new LoginConfig();

        var errors = config.Validate();

        Assert.Equal(4, errors.Count);
    }

    [Fact]
    public void Dc_DefaultsToUs_WhenNotProvidedInJson()
    {
        const string json = """
            {
              "name": "myacc",
              "client-id": "cid",
              "client-secret": "cs",
              "scope": ["ZohoAPI.READ"]
            }
            """;

        var config = JsonSerializer.Deserialize<LoginConfig>(json)!;

        Assert.Equal("us", config.Dc);
    }

    [Fact]
    public void Deserialization_ReadsKebabCaseFieldNames()
    {
        const string json = """
            {
              "name": "test-account",
              "client-id": "my-client-id",
              "client-secret": "my-secret",
              "scope": ["ZohoAPI.Resource.READ", "ZohoAPI.Resource.WRITE"],
              "dc": "eu"
            }
            """;

        var config = JsonSerializer.Deserialize<LoginConfig>(json)!;

        Assert.Equal("test-account", config.Name);
        Assert.Equal("my-client-id", config.ClientId);
        Assert.Equal("my-secret", config.ClientSecret);
        Assert.Equal("eu", config.Dc);
        Assert.Equal(2, config.Scope!.Length);
        Assert.Contains("ZohoAPI.Resource.READ", config.Scope);
        Assert.Contains("ZohoAPI.Resource.WRITE", config.Scope);
    }
}
