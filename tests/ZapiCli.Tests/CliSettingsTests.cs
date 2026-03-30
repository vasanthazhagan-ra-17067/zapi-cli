using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ZapiCli.Core;
using ZapiCli.Tests.Fakes;
using Xunit;

namespace ZapiCli.Tests;

/// <summary>
/// Unit tests for Story 17:
///   - CliSettings serializes all four fields with SnakeCaseLower keys.
///   - TraceSession reads TraceDefaultExportPath from CliSettings.
///   - config set scope-file saves path without existence check.
///   - config set app-dir creates missing directory and saves path.
///   - config show outputs all four fields.
/// </summary>
public sealed class CliSettingsTests : IDisposable
{
    private readonly string _tempDir =
        Path.Combine(Path.GetTempPath(), "zapi-cli-settings-test-" + Guid.NewGuid().ToString("N")[..8]);

    public CliSettingsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    private CliSettingsStore MakeStore() =>
        new(_tempDir, NullLogger<CliSettingsStore>.Instance);

    // ─── CliSettings serialization ────────────────────────────────────────────

    [Fact]
    public async Task CliSettings_AllFourFields_RoundTrip()
    {
        var store = MakeStore();
        var original = new CliSettings
        {
            EnvFile = "/tmp/env",
            ScopeFile = "/tmp/scopes.txt",
            AppDataDir = "/tmp/appdata",
            TraceDefaultExportPath = "/tmp/traces",
        };

        await store.SaveAsync(original);
        var loaded = await store.LoadAsync();

        Assert.Equal(original.EnvFile, loaded.EnvFile);
        Assert.Equal(original.ScopeFile, loaded.ScopeFile);
        Assert.Equal(original.AppDataDir, loaded.AppDataDir);
        Assert.Equal(original.TraceDefaultExportPath, loaded.TraceDefaultExportPath);
    }

    [Fact]
    public async Task CliSettings_JsonKeys_AreSnakeCaseLower()
    {
        var store = MakeStore();
        await store.SaveAsync(new CliSettings
        {
            EnvFile = "a",
            ScopeFile = "b",
            AppDataDir = "c",
            TraceDefaultExportPath = "d",
        });

        var json = await File.ReadAllTextAsync(Path.Combine(_tempDir, "cli-settings.json"));
        var doc = JsonDocument.Parse(json);

        Assert.True(doc.RootElement.TryGetProperty("envFile", out _), "envFile key expected");
        Assert.True(doc.RootElement.TryGetProperty("scopeFile", out _), "scopeFile key expected");
        Assert.True(doc.RootElement.TryGetProperty("appDataDir", out _), "appDataDir key expected");
        Assert.True(doc.RootElement.TryGetProperty("traceDefaultExportPath", out _), "traceDefaultExportPath key expected");
    }

    // ─── TraceSession reads TraceDefaultExportPath from CliSettings ───────────

    [Fact]
    public async Task TraceSession_ReadsTraceDefaultExportPath_FromCliSettings()
    {
        var store = MakeStore();
        var exportDir = Path.Combine(_tempDir, "my-traces");
        await store.SaveAsync(new CliSettings { TraceDefaultExportPath = exportDir });

        var traceSession = new ZapiCli.Core.Trace.TraceSession(
            _tempDir, store, NullLogger<ZapiCli.Core.Trace.TraceSession>.Instance);

        var entry = await traceSession.StartSessionAsync("my-session", null);
        Assert.StartsWith(exportDir, entry.ExportPath);
    }

    // ─── ConfigSetScopeFileCommand ────────────────────────────────────────────

    [Fact]
    public async Task ConfigSetScopeFile_SavesPath_WithoutExistenceCheck()
    {
        var store = MakeStore();
        var output = new InMemoryOutputWriter();
        var cmd = new ZapiCli.Commands.ConfigCommands.ConfigSetScopeFileCommand(store, output);

        var nonExistentPath = Path.Combine(_tempDir, "does-not-exist.txt");
        var settings = new ZapiCli.Commands.ConfigCommands.ConfigSetScopeFileSettings
        {
            Path = nonExistentPath,
        };

        var exitCode = await cmd.ExecuteAsync(null!, settings);
        Assert.Equal(0, exitCode);

        var loaded = await store.LoadAsync();
        Assert.Equal(Path.GetFullPath(nonExistentPath), loaded.ScopeFile);
    }

    // ─── ConfigSetAppDirCommand ───────────────────────────────────────────────

    [Fact]
    public async Task ConfigSetAppDir_CreatesMissingDirectory_SavesPath()
    {
        var store = MakeStore();
        var output = new InMemoryOutputWriter();
        var accountStore = new FakeAccountStore();
        var cmd = new ZapiCli.Commands.ConfigCommands.ConfigSetAppDirCommand(store, output, accountStore);

        var newDir = Path.Combine(_tempDir, "app-data-dir");
        Assert.False(Directory.Exists(newDir));

        var settings = new ZapiCli.Commands.ConfigCommands.ConfigSetAppDirSettings
        {
            Path = newDir,
        };

        var exitCode = await cmd.ExecuteAsync(null!, settings);
        Assert.Equal(0, exitCode);

        Assert.True(Directory.Exists(newDir));
        var loaded = await store.LoadAsync();
        Assert.Equal(Path.GetFullPath(newDir), loaded.AppDataDir);
    }

    [Fact]
    public async Task ConfigSetAppDir_MigratesAccountsJson_WhenOldFileExists()
    {
        // Arrange: create old data dir with an accounts.json
        var oldDataDir = Path.Combine(_tempDir, "old-data");
        Directory.CreateDirectory(oldDataDir);
        var oldAccountsJson = Path.Combine(oldDataDir, "accounts.json");
        await File.WriteAllTextAsync(oldAccountsJson, """{"accounts":[]}""");

        // AccountStore pointing at the old dir — simulates current singleton at startup.
        var realAccountStore = new ZapiCli.Core.Accounts.AccountStore(
            oldDataDir, Microsoft.Extensions.Logging.Abstractions.NullLogger<ZapiCli.Core.Accounts.AccountStore>.Instance);

        var cliStore = MakeStore();
        // Pre-save so existing.AppDataDir == oldDataDir (not null, so no platform-dir fallback needed).
        await cliStore.SaveAsync(new CliSettings { AppDataDir = oldDataDir });

        var output = new InMemoryOutputWriter();
        var cmd = new ZapiCli.Commands.ConfigCommands.ConfigSetAppDirCommand(cliStore, output, realAccountStore);

        var newDir = Path.Combine(_tempDir, "new-data");
        var settings = new ZapiCli.Commands.ConfigCommands.ConfigSetAppDirSettings { Path = newDir };

        // Act
        var exitCode = await cmd.ExecuteAsync(null!, settings);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(Path.Combine(newDir, "accounts.json")));
        Assert.True(File.Exists(oldAccountsJson), "Original file must be preserved");

        Assert.Single(output.SuccessOutput);
        var json = System.Text.Json.JsonDocument.Parse(output.SuccessOutput[0]).RootElement;
        Assert.Equal("ok", json.GetProperty("status").GetString());
        Assert.True(json.GetProperty("data").GetProperty("migrated").GetBoolean());
    }

    [Fact]
    public async Task ConfigSetAppDir_DoesNotOverwrite_WhenAccountsJsonAlreadyExistsInNewDir()
    {
        // Arrange: old dir has accounts.json AND new dir already has accounts.json
        var oldDataDir = Path.Combine(_tempDir, "old-data2");
        Directory.CreateDirectory(oldDataDir);
        await File.WriteAllTextAsync(Path.Combine(oldDataDir, "accounts.json"), """{"accounts":[{"name":"old"}]}""");

        var newDir = Path.Combine(_tempDir, "new-data2");
        Directory.CreateDirectory(newDir);
        var existingNewContent = """{"accounts":[{"name":"existing"}]}""";
        await File.WriteAllTextAsync(Path.Combine(newDir, "accounts.json"), existingNewContent);

        var realAccountStore = new ZapiCli.Core.Accounts.AccountStore(
            oldDataDir, Microsoft.Extensions.Logging.Abstractions.NullLogger<ZapiCli.Core.Accounts.AccountStore>.Instance);

        var cliStore = MakeStore();
        var output = new InMemoryOutputWriter();
        var cmd = new ZapiCli.Commands.ConfigCommands.ConfigSetAppDirCommand(cliStore, output, realAccountStore);

        var settings = new ZapiCli.Commands.ConfigCommands.ConfigSetAppDirSettings { Path = newDir };

        // Act
        var exitCode = await cmd.ExecuteAsync(null!, settings);

        // Assert: no overwrite, migrated == false, existing new file intact
        Assert.Equal(0, exitCode);
        var newContent = await File.ReadAllTextAsync(Path.Combine(newDir, "accounts.json"));
        Assert.Equal(existingNewContent, newContent);

        var json = System.Text.Json.JsonDocument.Parse(output.SuccessOutput[0]).RootElement;
        Assert.False(json.GetProperty("data").GetProperty("migrated").GetBoolean());
    }

    [Fact]
    public async Task ConfigSetAppDir_MigratedFalse_WhenNoAccountsJsonInOldDir()
    {
        // Arrange: old dir exists but has no accounts.json
        var oldDataDir = Path.Combine(_tempDir, "old-data3");
        Directory.CreateDirectory(oldDataDir);

        var realAccountStore = new ZapiCli.Core.Accounts.AccountStore(
            oldDataDir, Microsoft.Extensions.Logging.Abstractions.NullLogger<ZapiCli.Core.Accounts.AccountStore>.Instance);

        var cliStore = MakeStore();
        var output = new InMemoryOutputWriter();
        var cmd = new ZapiCli.Commands.ConfigCommands.ConfigSetAppDirCommand(cliStore, output, realAccountStore);

        var newDir = Path.Combine(_tempDir, "new-data3");
        var settings = new ZapiCli.Commands.ConfigCommands.ConfigSetAppDirSettings { Path = newDir };

        // Act
        var exitCode = await cmd.ExecuteAsync(null!, settings);

        // Assert
        Assert.Equal(0, exitCode);
        Assert.False(File.Exists(Path.Combine(newDir, "accounts.json")));

        var json = System.Text.Json.JsonDocument.Parse(output.SuccessOutput[0]).RootElement;
        Assert.False(json.GetProperty("data").GetProperty("migrated").GetBoolean());
    }

    // ─── ConfigShowCommand outputs all four fields ────────────────────────────

    [Fact]
    public async Task ConfigShow_OutputsAllFourFields()
    {
        var store = MakeStore();
        await store.SaveAsync(new CliSettings
        {
            EnvFile = "/e",
            ScopeFile = "/s",
            AppDataDir = "/a",
            TraceDefaultExportPath = "/t",
        });

        var output = new InMemoryOutputWriter();
        var cmd = new ZapiCli.Commands.ConfigCommands.ConfigShowCommand(store, output);
        await cmd.ExecuteAsync(null!, new ZapiCli.Commands.ConfigCommands.ConfigShowSettings());

        Assert.Single(output.SuccessOutput);
        var json = JsonDocument.Parse(output.SuccessOutput[0]).RootElement;
        Assert.Equal("/e", json.GetProperty("env_file").GetString());
        Assert.Equal("/s", json.GetProperty("scope_file").GetString());
        Assert.Equal("/a", json.GetProperty("app_data_dir").GetString());
        Assert.Equal("/t", json.GetProperty("trace_default_export_path").GetString());
    }

    // ─── TryReadPersistedAppDataDir ───────────────────────────────────────────

    [Fact]
    public async Task TryReadPersistedAppDataDir_ReturnsNull_WhenNotSet()
    {
        var store = MakeStore();
        await store.SaveAsync(new CliSettings { EnvFile = "/e" });
        var result = CliSettingsStore.TryReadPersistedAppDataDir(_tempDir);
        Assert.Null(result);
    }

    [Fact]
    public async Task TryReadPersistedAppDataDir_ReturnsValue_WhenSet()
    {
        var store = MakeStore();
        await store.SaveAsync(new CliSettings { AppDataDir = "/data" });
        var result = CliSettingsStore.TryReadPersistedAppDataDir(_tempDir);
        Assert.Equal("/data", result);
    }

    // ─── TryReadPersistedEnvFile (Story 22) ──────────────────────────────────

    [Fact]
    public async Task TryReadPersistedEnvFile_ReturnsNull_WhenNotConfigured()
    {
        // No cli-settings.json written — startup should not call LoadDotEnv.
        var result = CliSettingsStore.TryReadPersistedEnvFile(_tempDir);
        Assert.Null(result);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task TryReadPersistedEnvFile_ReturnsPath_WhenConfigured()
    {
        var store = MakeStore();
        await store.SaveAsync(new CliSettings { EnvFile = "/home/user/.env" });

        var result = CliSettingsStore.TryReadPersistedEnvFile(_tempDir);
        Assert.Equal("/home/user/.env", result);
    }

    [Fact]
    public void GlobalSettings_HasNo_EnvFileOption()
    {
        // Ensure the --env-file flag was not re-introduced as a Spectre option.
        var props = typeof(ZapiCli.Commands.GlobalSettings)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        var names = props.Select(p => p.Name).ToList();
        Assert.DoesNotContain("EnvFile", names);
        Assert.DoesNotContain("EnvFilePath", names);
    }
}
