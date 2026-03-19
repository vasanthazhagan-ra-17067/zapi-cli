using System.Text;
using System.Text.Json;
using ZapiCli.Commands;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

public sealed class AccountLoginCommandTests : IDisposable
{
    private readonly string _tmpDir;

    public AccountLoginCommandTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"zapi-login-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static FakeAccountService BuildService(string? expectName = null, string expectDc = "us")
    {
        return new FakeAccountService(expectName ?? "dev", expectDc);
    }

    private static AccountCommands.AccountLoginCommand BuildCommand(
        IAccountService? service = null,
        InMemoryOutputWriter? output = null)
    {
        return new AccountCommands.AccountLoginCommand(
            service ?? BuildService(),
            output ?? new InMemoryOutputWriter());
    }

    private string WriteLoginFile(object config)
    {
        var path = Path.Combine(_tmpDir, "login.json");
        File.WriteAllText(path, JsonSerializer.Serialize(config,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower }));
        return path;
    }

    // ── Settings.Validate ─────────────────────────────────────────────────────

    [Fact]
    public void Validate_Succeeds_WhenAllFlagsProvided()
    {
        var settings = new AccountCommands.AccountLoginSettings
        {
            Name = "dev",
            ClientId = "cid",
            ClientSecret = "csecret",
            Scope = "ZohoAPI.READ",
        };

        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_Fails_WhenScopeMissingAndNoFile()
    {
        var settings = new AccountCommands.AccountLoginSettings
        {
            Name = "dev",
            ClientId = "cid",
            ClientSecret = "csecret",
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("scope", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Fails_WhenFileMissing()
    {
        var settings = new AccountCommands.AccountLoginSettings
        {
            JsonFile = Path.Combine(_tmpDir, "does-not-exist.json"),
        };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains("not found", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Succeeds_WhenFileHasAllRequiredFields()
    {
        var path = WriteLoginFile(new
        {
            name = "acc",
            ClientId = "cid",
            ClientSecret = "cs",
            scope = new[] { "ZohoAPI.READ" },
            dc = "us",
        });

        // Write with the correct kebab-case keys that LoginConfig expects.
        File.WriteAllText(path, """
            {
              "name": "acc",
              "client-id": "cid",
              "client-secret": "cs",
              "scope": ["ZohoAPI.READ"],
              "dc": "us"
            }
            """);

        var settings = new AccountCommands.AccountLoginSettings { JsonFile = path };
        var result = settings.Validate();

        Assert.True(result.Successful);
    }

    [Fact]
    public void Validate_Fails_WhenFileIsMissingRequiredFields()
    {
        var path = Path.Combine(_tmpDir, "incomplete.json");
        File.WriteAllText(path, """{ "dc": "us" }""");

        var settings = new AccountCommands.AccountLoginSettings { JsonFile = path };
        var result = settings.Validate();

        Assert.False(result.Successful);
        // Should mention missing fields.
        Assert.NotEmpty(result.Message ?? string.Empty);
    }

    // ── ExecuteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CallsLoginAsync_WithAllFlagValues()
    {
        var fakeService = new FakeAccountService("dev", "us");
        var output = new InMemoryOutputWriter();
        var command = new AccountCommands.AccountLoginCommand(fakeService, output);

        var settings = new AccountCommands.AccountLoginSettings
        {
            Name = "dev",
            ClientId = "cid",
            ClientSecret = "csecret",
            Scope = "ZohoAPI.READ,ZohoAPI.WRITE",
            Dc = "us",
        };

        var code = await command.ExecuteAsync(null!, settings);

        Assert.Equal(0, code);
        Assert.NotNull(fakeService.LastLoginArgs);
        Assert.Equal("dev", fakeService.LastLoginArgs!.Value.Name);
        Assert.Equal("cid", fakeService.LastLoginArgs!.Value.ClientId);
        Assert.Equal("csecret", fakeService.LastLoginArgs!.Value.ClientSecret);
        Assert.Equal(["ZohoAPI.READ", "ZohoAPI.WRITE"], fakeService.LastLoginArgs!.Value.Scopes);
        Assert.Equal("us", fakeService.LastLoginArgs!.Value.Dc);
    }

    [Fact]
    public async Task ExecuteAsync_Writes_StatusOkOutput()
    {
        var fakeService = new FakeAccountService("dev", "us");
        var output = new InMemoryOutputWriter();
        var command = new AccountCommands.AccountLoginCommand(fakeService, output);

        var settings = new AccountCommands.AccountLoginSettings
        {
            Name = "dev",
            ClientId = "cid",
            ClientSecret = "csecret",
            Scope = "ZohoAPI.READ",
        };

        await command.ExecuteAsync(null!, settings);

        Assert.Single(output.SuccessOutput);
        var json = output.SuccessOutput[0];
        Assert.Contains("\"status\"", json);
        Assert.Contains("\"ok\"", json);
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"dc\"", json);
    }

    [Fact]
    public async Task ExecuteAsync_CallsLoginAsync_WithFileValues()
    {
        var path = Path.Combine(_tmpDir, "login.json");
        File.WriteAllText(path, """
            {
              "name": "fromfile",
              "client-id": "file-cid",
              "client-secret": "file-cs",
              "scope": ["ZohoAPI.READ"],
              "dc": "eu"
            }
            """);

        var fakeService = new FakeAccountService("fromfile", "eu");
        var output = new InMemoryOutputWriter();
        var command = new AccountCommands.AccountLoginCommand(fakeService, output);

        var settings = new AccountCommands.AccountLoginSettings { JsonFile = path };

        var code = await command.ExecuteAsync(null!, settings);

        Assert.Equal(0, code);
        Assert.NotNull(fakeService.LastLoginArgs);
        Assert.Equal("fromfile", fakeService.LastLoginArgs!.Value.Name);
        Assert.Equal("file-cid", fakeService.LastLoginArgs!.Value.ClientId);
    }

    [Fact]
    public async Task ExecuteAsync_CliNameOverridesFileValue()
    {
        var path = Path.Combine(_tmpDir, "login.json");
        File.WriteAllText(path, """
            {
              "name": "fromfile",
              "client-id": "cid",
              "client-secret": "cs",
              "scope": ["ZohoAPI.READ"]
            }
            """);

        var fakeService = new FakeAccountService("staging", "us");
        var output = new InMemoryOutputWriter();
        var command = new AccountCommands.AccountLoginCommand(fakeService, output);

        var settings = new AccountCommands.AccountLoginSettings
        {
            JsonFile = path,
            Name = "staging",  // CLI overrides file
        };

        await command.ExecuteAsync(null!, settings);

        Assert.Equal("staging", fakeService.LastLoginArgs!.Value.Name);
    }

    [Fact]
    public async Task ExecuteAsync_ScopeStringIsSplitAndTrimmed()
    {
        var fakeService = new FakeAccountService("dev", "us");
        var output = new InMemoryOutputWriter();
        var command = new AccountCommands.AccountLoginCommand(fakeService, output);

        var settings = new AccountCommands.AccountLoginSettings
        {
            Name = "dev",
            ClientId = "cid",
            ClientSecret = "cs",
            Scope = " ZohoAPI.READ , ZohoAPI.WRITE ",
        };

        await command.ExecuteAsync(null!, settings);

        Assert.Equal(["ZohoAPI.READ", "ZohoAPI.WRITE"],
            fakeService.LastLoginArgs!.Value.Scopes);
    }

    [Fact]
    public async Task ExecuteAsync_PropagatesAccountAlreadyExistsException()
    {
        var fakeService = new FakeAccountService("dev", "us",
            throwException: new ZapiCliException(
                "Account 'dev' already exists.",
                ErrorCodes.ACCOUNT_ALREADY_EXISTS,
                exitCode: 1));
        var output = new InMemoryOutputWriter();
        var command = new AccountCommands.AccountLoginCommand(fakeService, output);

        var settings = new AccountCommands.AccountLoginSettings
        {
            Name = "dev",
            ClientId = "cid",
            ClientSecret = "cs",
            Scope = "ZohoAPI.READ",
        };

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            command.ExecuteAsync(null!, settings));

        Assert.Equal(ErrorCodes.ACCOUNT_ALREADY_EXISTS, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }
}

/// <summary>
/// Fake <see cref="IAccountService"/> for AccountLoginCommandTests.
/// Records LoginAsync call parameters and returns a preset (name, dc) tuple.
/// </summary>
internal sealed class FakeAccountService : IAccountService
{
    private readonly string _returnName;
    private readonly string _returnDc;
    private readonly ZapiCliException? _throwException;

    public (string Name, string ClientId, string ClientSecret, string[] Scopes, string Dc)? LastLoginArgs { get; private set; }

    public FakeAccountService(string returnName, string returnDc, ZapiCliException? throwException = null)
    {
        _returnName = returnName;
        _returnDc = returnDc;
        _throwException = throwException;
    }

    public Task<(string Name, string Dc)> LoginAsync(
        string name, string clientId, string clientSecret, string[] scopes, string dc,
        int callbackPort = 8085, CancellationToken ct = default)
    {
        if (_throwException is not null)
            throw _throwException;

        LastLoginArgs = (name, clientId, clientSecret, scopes, dc);
        return Task.FromResult((_returnName, _returnDc));
    }

    // ── Unused stubs ──────────────────────────────────────────────────────────
    public Task<(string Name, string Dc)> AddAccountAsync(string name, string code, string redirectUri,
        string clientId, string clientSecret, string dc, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<AccountListView>> ListAccountsAsync(CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<AccountShowView> ShowAccountAsync(string name, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task SetDefaultAsync(string name, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task RemoveAccountAsync(string name, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task ReAuthAsync(string name, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
