using Microsoft.Extensions.Logging.Abstractions;
using ZapiCli.Commands;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

public sealed class AccountLoginCommandTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly CliSettingsStore _settingsStore;

    public AccountLoginCommandTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"zapi-login-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tmpDir);
        _settingsStore = new CliSettingsStore(_tmpDir, NullLogger<CliSettingsStore>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private AccountCommands.AccountLoginCommand BuildCommand(
        LoginFakeAccountService? service = null,
        InMemoryOutputWriter? output = null,
        ICliSettingsStore? settingsStore = null)
    {
        return new AccountCommands.AccountLoginCommand(
            service ?? new LoginFakeAccountService(),
            output ?? new InMemoryOutputWriter(),
            settingsStore ?? _settingsStore);
    }

    // ── AccountLoginSettings.Validate ─────────────────────────────────────────

    [Fact]
    public void Validate_Succeeds_WhenNameIsNull()
    {
        var settings = new AccountCommands.AccountLoginSettings { Name = null };
        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_Succeeds_WhenNameIsClean()
    {
        var settings = new AccountCommands.AccountLoginSettings { Name = "my-account" };
        Assert.True(settings.Validate().Successful);
    }

    [Fact]
    public void Validate_Fails_WhenNameContainsSlash()
    {
        var settings = new AccountCommands.AccountLoginSettings { Name = "bad/name" };
        var result = settings.Validate();
        Assert.False(result.Successful);
        Assert.Contains("--name", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── ExecuteAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_MissingClientId_ThrowsEnvFileNotConfigured()
    {
        Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", null);
        var command = BuildCommand();
        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            command.ExecuteAsync(null!, new AccountCommands.AccountLoginSettings()));
        Assert.Equal(ErrorCodes.ENV_FILE_NOT_CONFIGURED, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_NoScopeAndNoScopeFile_ThrowsScopeFileNotConfigured()
    {
        Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", "test-cid");
        try
        {
            var command = BuildCommand();
            var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
                command.ExecuteAsync(null!, new AccountCommands.AccountLoginSettings()));
            Assert.Equal(ErrorCodes.SCOPE_FILE_NOT_CONFIGURED, ex.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ScopeFileConfiguredButMissing_ThrowsIoError()
    {
        Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", "test-cid");
        try
        {
            var missingPath = Path.Combine(_tmpDir, "does-not-exist.txt");
            await _settingsStore.SaveAsync(new CliSettings { ScopeFile = missingPath });
            var command = BuildCommand();
            var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
                command.ExecuteAsync(null!, new AccountCommands.AccountLoginSettings()));
            Assert.Equal(ErrorCodes.IO_ERROR, ex.Code);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ScopeFromFlag_CallsMobileLoginWithCorrectScopes()
    {
        Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", "test-cid");
        try
        {
            var service = new LoginFakeAccountService();
            var command = BuildCommand(service);
            await command.ExecuteAsync(null!, new AccountCommands.AccountLoginSettings
            {
                Scope = "ZohoCRM.Contacts.READ,ZohoCRM.Deals.ALL",
            });
            Assert.NotNull(service.LastMobileLoginArgs);
            Assert.Contains("ZohoCRM.Contacts.READ", service.LastMobileLoginArgs!.Value.Scopes);
            Assert.Contains("ZohoCRM.Deals.ALL", service.LastMobileLoginArgs.Value.Scopes);
            Assert.Equal("test-cid", service.LastMobileLoginArgs.Value.ClientId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ScopeFromFile_CallsMobileLoginWithCorrectScopes()
    {
        Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", "test-cid");
        try
        {
            var scopeFile = Path.Combine(_tmpDir, "scopes.txt");
            File.WriteAllLines(scopeFile, ["ZohoCliq.Channels.READ", "# comment", "ZohoCliq.Messages.ALL"]);
            await _settingsStore.SaveAsync(new CliSettings { ScopeFile = scopeFile });
            var service = new LoginFakeAccountService();
            var command = BuildCommand(service);
            await command.ExecuteAsync(null!, new AccountCommands.AccountLoginSettings());
            Assert.NotNull(service.LastMobileLoginArgs);
            Assert.Contains("ZohoCliq.Channels.READ", service.LastMobileLoginArgs!.Value.Scopes);
            Assert.Contains("ZohoCliq.Messages.ALL", service.LastMobileLoginArgs.Value.Scopes);
            Assert.DoesNotContain("# comment", service.LastMobileLoginArgs.Value.Scopes);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ScopeFromFlagAndFile_MergedAndDeduped()
    {
        Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", "test-cid");
        try
        {
            var scopeFile = Path.Combine(_tmpDir, "scopes.txt");
            File.WriteAllLines(scopeFile, ["ZohoCliq.Channels.READ"]);
            await _settingsStore.SaveAsync(new CliSettings { ScopeFile = scopeFile });
            var service = new LoginFakeAccountService();
            var command = BuildCommand(service);
            await command.ExecuteAsync(null!, new AccountCommands.AccountLoginSettings
            {
                Scope = "ZohoCliq.Channels.READ,ZohoCRM.Contacts.ALL",
            });
            Assert.NotNull(service.LastMobileLoginArgs);
            var scopes = service.LastMobileLoginArgs!.Value.Scopes;
            Assert.Equal(2, scopes.Length);
            Assert.Contains("ZohoCliq.Channels.READ", scopes);
            Assert.Contains("ZohoCRM.Contacts.ALL", scopes);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_WritesOkOutput()
    {
        Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", "test-cid");
        try
        {
            var service = new LoginFakeAccountService(returnName: "myaccount", returnDc: "eu");
            var output = new InMemoryOutputWriter();
            var command = BuildCommand(service, output);
            await command.ExecuteAsync(null!, new AccountCommands.AccountLoginSettings
            {
                Scope = "ZohoCRM.Contacts.READ",
            });
            Assert.Single(output.SuccessOutput);
            var json = output.SuccessOutput[0];
            Assert.Contains("\"status\"", json);
            Assert.Contains("\"ok\"", json);
            Assert.Contains("\"name\"", json);
            Assert.Contains("\"myaccount\"", json);
            Assert.Contains("\"dc\"", json);
            Assert.Contains("\"eu\"", json);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NamePassedThrough()
    {
        Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", "test-cid");
        try
        {
            var service = new LoginFakeAccountService();
            var command = BuildCommand(service);
            await command.ExecuteAsync(null!, new AccountCommands.AccountLoginSettings
            {
                Name = "my-profile",
                Scope = "ZohoCRM.Contacts.READ",
            });
            Assert.Equal("my-profile", service.LastMobileLoginArgs!.Value.Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_NullName_PassedAsNull()
    {
        Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", "test-cid");
        try
        {
            var service = new LoginFakeAccountService();
            var command = BuildCommand(service);
            await command.ExecuteAsync(null!, new AccountCommands.AccountLoginSettings
            {
                Scope = "ZohoCRM.Contacts.READ",
            });
            Assert.Null(service.LastMobileLoginArgs!.Value.Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", null);
        }
    }

    [Fact]
    public async Task ExecuteAsync_ClientSecretFromEnv_PassedToMobileLogin()
    {
        Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", "test-cid");
        Environment.SetEnvironmentVariable("ZOHO_CLIENT_SECRET", "test-secret");
        try
        {
            var service = new LoginFakeAccountService();
            var command = BuildCommand(service);
            await command.ExecuteAsync(null!, new AccountCommands.AccountLoginSettings
            {
                Scope = "ZohoCRM.Contacts.READ",
            });
            Assert.Equal("test-secret", service.LastMobileLoginArgs!.Value.ClientSecret);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZOHO_CLIENT_ID", null);
            Environment.SetEnvironmentVariable("ZOHO_CLIENT_SECRET", null);
        }
    }
}

/// <summary>
/// Fake <see cref="IAccountService"/> for <see cref="AccountLoginCommandTests"/>.
/// Records MobileLoginAsync call parameters and returns a preset (name, dc) tuple.
/// </summary>
internal sealed class LoginFakeAccountService : IAccountService
{
    private readonly string _returnName;
    private readonly string _returnDc;
    private readonly ZapiCliException? _throwException;

    public (string? Name, string ClientId, string[] Scopes, string? ClientSecret)? LastMobileLoginArgs { get; private set; }

    public LoginFakeAccountService(string returnName = "dev", string returnDc = "us", ZapiCliException? throwException = null)
    {
        _returnName = returnName;
        _returnDc = returnDc;
        _throwException = throwException;
    }

    public Task<(string Name, string Dc)> MobileLoginAsync(
        string? name, string clientId, string[] scopes, string? clientSecret = null, CancellationToken ct = default)
    {
        if (_throwException is not null)
            throw _throwException;

        LastMobileLoginArgs = (name, clientId, scopes, clientSecret);
        return Task.FromResult((_returnName, _returnDc));
    }

    // ── Unused stubs ──────────────────────────────────────────────────────────
    public Task<IReadOnlyList<AccountListView>> ListAccountsAsync(CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<AccountShowView> ShowAccountAsync(string? name, string? email = null, string? zuidstring = null, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task SetDefaultAsync(string? name, string? email = null, string? zuidstring = null, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task RemoveAccountAsync(string? name, string? email = null, string? zuidstring = null, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task ReAuthAsync(string? name, string? email = null, string? zuidstring = null, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<(string OldName, string NewName)> RenameAccountAsync(
        string? name, string? email, string? zuidstring, string newName, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<(string AccountName, List<string> UpdatedScopes)> AddScopesAsync(
        string accountName, IEnumerable<string> scopesToAdd, int callbackPort = OAuthConstants.DefaultCallbackPort, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<List<string>> GetScopesAsync(string accountName, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
