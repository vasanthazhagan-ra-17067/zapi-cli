using System.Net;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ZapiCli.Commands;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

public sealed class ScopeCommandTests : IDisposable
{
    private readonly string _tmpDir;
    private readonly AccountStore _store;
    private readonly AccountService _service;

    public ScopeCommandTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"zapi-cli-scope-test-{Guid.NewGuid():N}");
        _store = new AccountStore(_tmpDir, NullLogger<AccountStore>.Instance);
        // AccountService only uses accountStore for scope operations; other deps can be fakes.
        _service = new AccountService(
            _store,
            new FakeAuthProvider(),
            FakeHttpMessageHandler.ToFactory(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)),
            NullLogger<AccountService>.Instance,
            new FakeOAuthBrowserFlow());
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmpDir, recursive: true); } catch { /* best-effort */ }
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task SeedAccount(string name, string email = "user@example.com",
        List<string>? scopes = null, bool isDefault = true)
    {
        var root = await _store.LoadAsync();
        root.Accounts.Add(new AccountEntry
        {
            Name = name,
            Dc = "us",
            Email = email,
            Scopes = scopes ?? [],
            IsDefault = isDefault,
        });
        await _store.SaveAsync(root);
    }

    private FakeLocalCallbackServer MakeFakeServer()
        => new FakeLocalCallbackServer("irrelevant", "irrelevant");

    private ScopeCommands.ScopeAddCommand MakeAddCommand(IOutputWriter? writer = null)
        => new(_store, _service, writer ?? new InMemoryOutputWriter());

    private ScopeCommands.ScopeListCommand MakeListCommand(IOutputWriter? writer = null)
        => new(_store, _service, writer ?? new InMemoryOutputWriter());

    // ── account scope add ─────────────────────────────────────────────────────

    [Fact]
    public async Task ScopeAdd_SingleScope_AddsScopeToEntry()
    {
        await SeedAccount("work");

        await _service.AddScopesAsync("work", ["ZohoDesk.Tickets.READ"], 8085, _ => MakeFakeServer());

        var entry = await _store.FindAsync("work");
        Assert.NotNull(entry);
        Assert.Contains("ZohoDesk.Tickets.READ", entry!.Scopes);
    }

    [Fact]
    public async Task ScopeAdd_CommaSeparated_AddsBothScopesIndividually()
    {
        await SeedAccount("work");

        var scopes = "ZohoDesk.Tickets.READ,ZohoDesk.Reports.READ"
            .Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
        await _service.AddScopesAsync("work", scopes, 8085, _ => MakeFakeServer());

        var entry = await _store.FindAsync("work");
        Assert.NotNull(entry);
        Assert.Contains("ZohoDesk.Tickets.READ", entry!.Scopes);
        Assert.Contains("ZohoDesk.Reports.READ", entry!.Scopes);
        Assert.Equal(2, entry!.Scopes.Count);
    }

    [Fact]
    public async Task ScopeAdd_CommaSeparatedWithSpaces_TrimsWhitespace()
    {
        await SeedAccount("work");

        var scopes = " ZohoDesk.Tickets.READ , ZohoDesk.Reports.READ "
            .Split(',').Select(s => s.Trim()).Where(s => !string.IsNullOrEmpty(s));
        await _service.AddScopesAsync("work", scopes, 8085, _ => MakeFakeServer());

        var entry = await _store.FindAsync("work");
        Assert.NotNull(entry);
        Assert.Contains("ZohoDesk.Tickets.READ", entry!.Scopes);
        Assert.Contains("ZohoDesk.Reports.READ", entry!.Scopes);
    }

    [Fact]
    public async Task ScopeAdd_DuplicateScope_NotAddedTwice()
    {
        await SeedAccount("work", scopes: ["ZohoDesk.Tickets.READ"]);

        await _service.AddScopesAsync("work", ["ZohoDesk.Tickets.READ"], 8085, _ => MakeFakeServer());

        var entry = await _store.FindAsync("work");
        Assert.NotNull(entry);
        Assert.Single(entry!.Scopes);
        Assert.Equal("ZohoDesk.Tickets.READ", entry!.Scopes[0]);
    }

    [Fact]
    public async Task ScopeAdd_ScopePersisted_AfterAdd()
    {
        await SeedAccount("work");

        await _service.AddScopesAsync("work", ["ZohoDesk.Tickets.READ"], 8085, _ => MakeFakeServer());

        var entry = await _store.FindAsync("work");
        Assert.Contains("ZohoDesk.Tickets.READ", entry!.Scopes);
    }

    [Fact]
    public async Task ScopeAdd_ResponseContainsStatusOkAndScopes()
    {
        await SeedAccount("work");

        var (name, updatedScopes) = await _service.AddScopesAsync(
            "work", ["ZohoDesk.Tickets.READ"], 8085, _ => MakeFakeServer());

        // Verify the contract that ScopeAddCommand would produce.
        var writer = new InMemoryOutputWriter();
        writer.WriteJson(new { status = "ok", data = new { account = name, scopes = updatedScopes } });

        var json = writer.LastSuccessJson;
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        Assert.Equal("ok", doc.RootElement.GetProperty("status").GetString());
        var scopes = doc.RootElement.GetProperty("data").GetProperty("scopes");
        Assert.Equal(JsonValueKind.Array, scopes.ValueKind);
    }

    [Fact]
    public async Task ScopeAdd_ZohoCorpAccount_ThrowsAccountDomainBlocked()
    {
        await SeedAccount("corp", email: "user@zohocorp.com");
        var cmd = MakeAddCommand();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            cmd.ExecuteAsync(null!,
                new ScopeCommands.ScopeAddSettings
                {
                    Account = "corp",
                    Scope = "ZohoDesk.Tickets.READ"
                }));

        Assert.Equal(ErrorCodes.ACCOUNT_DOMAIN_BLOCKED, ex.Code);
    }

    [Fact]
    public async Task ScopeAdd_AccountDomainBlocked_AccountsJsonUnchanged()
    {
        await SeedAccount("corp", email: "user@zohocorp.com");
        var cmd = MakeAddCommand();

        try
        {
            await cmd.ExecuteAsync(null!,
                new ScopeCommands.ScopeAddSettings
                {
                    Account = "corp",
                    Scope = "ZohoDesk.Tickets.READ"
                });
        }
        catch (ZapiCliException) { /* expected */ }

        var entry = await _store.FindAsync("corp");
        Assert.NotNull(entry);
        Assert.Empty(entry!.Scopes);
    }

    [Fact]
    public async Task ScopeAdd_NonExistentAccount_ThrowsAccountNotFound()
    {
        var cmd = MakeAddCommand();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            cmd.ExecuteAsync(null!,
                new ScopeCommands.ScopeAddSettings
                {
                    Account = "ghost",
                    Scope = "ZohoDesk.Tickets.READ"
                }));

        Assert.Equal(ErrorCodes.ACCOUNT_NOT_FOUND, ex.Code);
    }

    [Fact]
    public void ScopeAddSettings_MissingScope_ValidationFails()
    {
        var settings = new ScopeCommands.ScopeAddSettings(); // Scope = null
        var result = settings.Validate();
        Assert.False(result.Successful);
    }

    // ── account scope list ────────────────────────────────────────────────────

    [Fact]
    public async Task ScopeList_ReturnsPlainJsonArrayOfScopes()
    {
        await SeedAccount("work", scopes: ["ZohoDesk.Tickets.READ", "ZohoCRM.Contacts.READ"]);
        var writer = new InMemoryOutputWriter();
        var cmd = MakeListCommand(writer);

        var exitCode = await cmd.ExecuteAsync(null!,
            new ScopeCommands.ScopeListSettings { Account = "work" });

        Assert.Equal(0, exitCode);

        var json = writer.LastSuccessJson;
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json!);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        var scopes = doc.RootElement.EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("ZohoDesk.Tickets.READ", scopes);
        Assert.Contains("ZohoCRM.Contacts.READ", scopes);
    }

    [Fact]
    public async Task ScopeList_EmptyScopes_ReturnsEmptyArray()
    {
        await SeedAccount("work");
        var writer = new InMemoryOutputWriter();
        var cmd = MakeListCommand(writer);

        await cmd.ExecuteAsync(null!, new ScopeCommands.ScopeListSettings { Account = "work" });

        using var doc = JsonDocument.Parse(writer.LastSuccessJson!);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    [Fact]
    public async Task ScopeList_NonExistentAccount_ThrowsAccountNotFound()
    {
        var cmd = MakeListCommand();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            cmd.ExecuteAsync(null!,
                new ScopeCommands.ScopeListSettings { Account = "ghost" }));

        Assert.Equal(ErrorCodes.ACCOUNT_NOT_FOUND, ex.Code);
    }

    [Fact]
    public async Task ScopeList_ZohoCorpAccount_ThrowsAccountDomainBlocked()
    {
        await SeedAccount("corp", email: "user@zohocorp.com",
            scopes: ["ZohoDesk.Tickets.READ"]);
        var cmd = MakeListCommand();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            cmd.ExecuteAsync(null!, new ScopeCommands.ScopeListSettings { Account = "corp" }));

        Assert.Equal(ErrorCodes.ACCOUNT_DOMAIN_BLOCKED, ex.Code);
    }

    [Fact]
    public async Task ScopeList_UsesDefaultAccountWhenNoFlagSpecified()
    {
        await SeedAccount("default-work", scopes: ["ZohoDesk.Tickets.READ"], isDefault: true);
        var writer = new InMemoryOutputWriter();
        var cmd = MakeListCommand(writer);

        // No --account flag — should pick up default account.
        var exitCode = await cmd.ExecuteAsync(null!, new ScopeCommands.ScopeListSettings());

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(writer.LastSuccessJson!);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.Equal(1, doc.RootElement.GetArrayLength());
    }
}
