using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ZapiCli.Core;
using ZapiCli.Core.Api;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Tests;

/// <summary>
/// Unit tests for <see cref="ApiRegistry"/> and the <c>api endpoints</c> command group (Story 9).
/// Uses a temporary directory so each test is fully isolated.
/// </summary>
public sealed class ApiRegistryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ApiRegistry _registry;

    public ApiRegistryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"zapi-registry-test-{Guid.NewGuid():N}");
        _registry = new ApiRegistry(_tempDir, NullLogger<ApiRegistry>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static ApiRegistryEntry MakeEntry(
        string id = "cliq-channels",
        string url = "https://cliq.zoho.com/api/v2/channels",
        string method = "GET",
        string purpose = "List all Cliq channels")
        => new() { Id = id, Url = url, Method = method, Purpose = purpose };

    private string RegistryPath => Path.Combine(_tempDir, "registry.json");

    // ─── Add ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Add_StoresEntryInRegistryJson()
    {
        var entry = MakeEntry();
        await _registry.AddAsync(entry);

        Assert.True(File.Exists(RegistryPath));
        var json = await File.ReadAllTextAsync(RegistryPath);
        Assert.Contains("cliq-channels", json);
        Assert.Contains("cliq.zoho.com", json);
    }

    [Fact]
    public async Task Add_DuplicateId_ThrowsRegistryEntryAlreadyExists()
    {
        await _registry.AddAsync(MakeEntry());

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            _registry.AddAsync(MakeEntry()));

        Assert.Equal(ErrorCodes.ENDPOINT_ALREADY_EXISTS, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    // ─── List ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task List_ReturnsAllEntries()
    {
        await _registry.AddAsync(MakeEntry("entry-1", "https://cliq.zoho.com/api/v2/channels"));
        await _registry.AddAsync(MakeEntry("entry-2", "https://desk.zohoapis.com/api/v1/tickets", "POST", "Create ticket"));

        var root = await _registry.LoadAsync();
        Assert.Equal(2, root.Apis.Count);
        Assert.Contains(root.Apis, e => e.Id == "entry-1");
        Assert.Contains(root.Apis, e => e.Id == "entry-2");
    }

    [Fact]
    public async Task List_EmptyRegistry_ReturnsEmptyArray()
    {
        var root = await _registry.LoadAsync();
        Assert.Empty(root.Apis);
    }

    // ─── Show ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Show_ExistingId_ReturnsSingleEntry()
    {
        await _registry.AddAsync(MakeEntry());
        var found = await _registry.FindByIdAsync("cliq-channels");

        Assert.NotNull(found);
        Assert.Equal("cliq-channels", found!.Id);
        Assert.Equal("https://cliq.zoho.com/api/v2/channels", found.Url);
        Assert.Equal("GET", found.Method);
    }

    [Fact]
    public async Task Show_NonExistentId_ReturnsNull()
    {
        var found = await _registry.FindByIdAsync("nonexistent");
        Assert.Null(found);
    }

    // ─── Update ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Update_ChangesOnlySuppliedFields()
    {
        await _registry.AddAsync(MakeEntry(
            url: "https://cliq.zoho.com/api/v2/channels",
            method: "GET",
            purpose: "Original purpose"));

        // Update only purpose.
        var updated = new ApiRegistryEntry
        {
            Id = "cliq-channels",
            Url = "https://cliq.zoho.com/api/v2/channels",
            Method = "GET",
            Purpose = "Updated purpose",
        };
        await _registry.UpdateAsync(updated);

        var found = await _registry.FindByIdAsync("cliq-channels");
        Assert.NotNull(found);
        Assert.Equal("Updated purpose", found!.Purpose);
        Assert.Equal("https://cliq.zoho.com/api/v2/channels", found.Url);
        Assert.Equal("GET", found.Method);
    }

    [Fact]
    public async Task Update_NonExistentId_ThrowsRegistryEntryNotFound()
    {
        var entry = new ApiRegistryEntry
        {
            Id = "nonexistent",
            Url = "https://cliq.zoho.com/api/v2/channels",
            Method = "GET",
            Purpose = "x",
        };

        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            _registry.UpdateAsync(entry));

        Assert.Equal(ErrorCodes.ENDPOINT_NOT_FOUND, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    // ─── Remove ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_DeletesEntry()
    {
        await _registry.AddAsync(MakeEntry());
        await _registry.RemoveAsync("cliq-channels");

        var root = await _registry.LoadAsync();
        Assert.Empty(root.Apis);
    }

    [Fact]
    public async Task Remove_NonExistentId_ThrowsRegistryEntryNotFound()
    {
        var ex = await Assert.ThrowsAsync<ZapiCliException>(() =>
            _registry.RemoveAsync("nonexistent"));

        Assert.Equal(ErrorCodes.ENDPOINT_NOT_FOUND, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    // ─── Host allowlist (HostValidator) ───────────────────────────────────────

    [Fact]
    public void HostValidator_RejectsNonZohoUrl()
    {
        var uri = new Uri("https://evil.com/api");
        var ex = Assert.Throws<ZapiCliException>(() => HostValidator.ValidateHost(uri));
        Assert.Equal(ErrorCodes.HOST_NOT_ALLOWED, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public void HostValidator_AcceptsZohoComSubdomain()
    {
        var uri = new Uri("https://cliq.zoho.com/api/v2/channels");
        var ex = Record.Exception(() => HostValidator.ValidateHost(uri));
        Assert.Null(ex);
    }

    [Fact]
    public void HostValidator_AcceptsZohoApisComSubdomain()
    {
        var uri = new Uri("https://desk.zohoapis.com/api/v1/tickets");
        var ex = Record.Exception(() => HostValidator.ValidateHost(uri));
        Assert.Null(ex);
    }

    // ─── File permissions ─────────────────────────────────────────────────────

    [Fact]
    public async Task RegistryJson_Has0600Permissions_OnUnix()
    {
        if (OperatingSystem.IsWindows())
            return; // Skip on Windows — ACL path is different.

        await _registry.AddAsync(MakeEntry());

        var mode = File.GetUnixFileMode(RegistryPath);
        // Must have UserRead + UserWrite and no group or other permissions.
        Assert.True(mode.HasFlag(UnixFileMode.UserRead));
        Assert.True(mode.HasFlag(UnixFileMode.UserWrite));
        Assert.False(mode.HasFlag(UnixFileMode.GroupRead));
        Assert.False(mode.HasFlag(UnixFileMode.OtherRead));
    }

    // ─── JSON serialization format ────────────────────────────────────────────

    [Fact]
    public async Task RegistryJson_UsesSnakeCaseLowerKeys()
    {
        await _registry.AddAsync(MakeEntry(purpose: "List all channels"));

        var json = await File.ReadAllTextAsync(RegistryPath);
        // Keys should be snake_case_lower.
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"url\"", json);
        Assert.Contains("\"method\"", json);
        Assert.Contains("\"purpose\"", json);
        Assert.Contains("\"apis\"", json);
    }
}
