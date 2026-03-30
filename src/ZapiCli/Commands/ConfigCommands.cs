using Spectre.Console;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Commands;

internal static class ConfigCommands
{
    // ─── config set env-file ──────────────────────────────────────────────────

    public sealed class ConfigSetEnvFileSettings : CommandSettings
    {
        [CommandArgument(0, "<PATH>")]
        public required string Path { get; init; }

        public override ValidationResult Validate()
        {
            if (!File.Exists(Path))
                return ValidationResult.Error($"File not found: {Path}");
            return ValidationResult.Success();
        }
    }

    public sealed class ConfigSetEnvFileCommand : AsyncCommand<ConfigSetEnvFileSettings>
    {
        private readonly ICliSettingsStore _store;
        private readonly IOutputWriter _output;

        public ConfigSetEnvFileCommand(ICliSettingsStore store, IOutputWriter output)
        {
            _store = store;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ConfigSetEnvFileSettings settings)
        {
            var absPath = System.IO.Path.GetFullPath(settings.Path);
            var existing = await _store.LoadAsync().ConfigureAwait(false);
            var updated = existing with { EnvFile = absPath };
            await _store.SaveAsync(updated).ConfigureAwait(false);

            _output.WriteJson(new { status = "ok", data = new { env_file = absPath } });
            return 0;
        }
    }

    // ─── config set scope-file ────────────────────────────────────────────────

    public sealed class ConfigSetScopeFileSettings : CommandSettings
    {
        [CommandArgument(0, "<PATH>")]
        public required string Path { get; init; }

        // No File.Exists check — validated at login time (story-21).
        public override ValidationResult Validate() => ValidationResult.Success();
    }

    public sealed class ConfigSetScopeFileCommand : AsyncCommand<ConfigSetScopeFileSettings>
    {
        private readonly ICliSettingsStore _store;
        private readonly IOutputWriter _output;

        public ConfigSetScopeFileCommand(ICliSettingsStore store, IOutputWriter output)
        {
            _store = store;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ConfigSetScopeFileSettings settings)
        {
            var absPath = System.IO.Path.GetFullPath(settings.Path);
            var existing = await _store.LoadAsync().ConfigureAwait(false);
            var updated = existing with { ScopeFile = absPath };
            await _store.SaveAsync(updated).ConfigureAwait(false);

            _output.WriteJson(new { status = "ok", data = new { scope_file = absPath } });
            return 0;
        }
    }

    // ─── config set app-dir ───────────────────────────────────────────────────

    public sealed class ConfigSetAppDirSettings : CommandSettings
    {
        [CommandArgument(0, "<PATH>")]
        public required string Path { get; init; }

        public override ValidationResult Validate() => ValidationResult.Success();
    }

    public sealed class ConfigSetAppDirCommand : AsyncCommand<ConfigSetAppDirSettings>
    {
        private readonly ICliSettingsStore _store;
        private readonly IOutputWriter _output;
        private readonly IAccountStore _accountStore;

        public ConfigSetAppDirCommand(ICliSettingsStore store, IOutputWriter output, IAccountStore accountStore)
        {
            _store = store;
            _output = output;
            _accountStore = accountStore;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ConfigSetAppDirSettings settings)
        {
            var absPath = System.IO.Path.GetFullPath(settings.Path);
            Directory.CreateDirectory(absPath);

            var migrated = await _accountStore.MigrateToDirectoryAsync(absPath).ConfigureAwait(false);

            var existing = await _store.LoadAsync().ConfigureAwait(false);
            var updated = existing with { AppDataDir = absPath };
            await _store.SaveAsync(updated).ConfigureAwait(false);

            _output.WriteJson(new { status = "ok", data = new { app_data_dir = absPath, migrated } });
            return 0;
        }
    }

    // ─── config show ─────────────────────────────────────────────────────────

    public sealed class ConfigShowSettings : CommandSettings { }

    public sealed class ConfigShowCommand : AsyncCommand<ConfigShowSettings>
    {
        private readonly ICliSettingsStore _store;
        private readonly IOutputWriter _output;

        public ConfigShowCommand(ICliSettingsStore store, IOutputWriter output)
        {
            _store = store;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ConfigShowSettings settings)
        {
            var config = await _store.LoadAsync().ConfigureAwait(false);
            _output.WriteJson(new
            {
                env_file = config.EnvFile,
                scope_file = config.ScopeFile,
                app_data_dir = config.AppDataDir,
                trace_default_export_path = config.TraceDefaultExportPath,
            });
            return 0;
        }
    }
}
