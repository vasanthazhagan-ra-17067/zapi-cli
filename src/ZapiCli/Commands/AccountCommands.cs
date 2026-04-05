using Spectre.Console;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Commands;

/// <summary>
/// All <c>account</c> subcommands. Command classes are thin: they validate flags,
/// delegate all business logic to <see cref="IAccountService"/>, and write output via
/// <see cref="IOutputWriter"/>. No domain logic lives here (ADR-0007).
/// </summary>
internal static class AccountCommands
{
    // ─── account list ─────────────────────────────────────────────────────────

    public sealed class AccountListCommand : AsyncCommand<GlobalSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public AccountListCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            GlobalSettings settings)
        {
            var accounts = await _service.ListAccountsAsync();
            _output.WriteJson(accounts);
            return 0;
        }
    }

    // ─── account show ─────────────────────────────────────────────────────────

    public sealed class AccountShowSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--email <EMAIL>")]
        public string? Email { get; init; }

        [CommandOption("--Zuid|--zuidstring <ZUIDSTRING>")]
        public string? Zuid { get; init; }

        public override ValidationResult Validate()
        {
            var count = (string.IsNullOrWhiteSpace(Name) ? 0 : 1)
                      + (string.IsNullOrWhiteSpace(Email) ? 0 : 1)
                      + (string.IsNullOrWhiteSpace(Zuid) ? 0 : 1);
            if (count == 0)
                return ValidationResult.Error("One of --name, --email, or --zuidstring is required.");
            if (count > 1)
                return ValidationResult.Error("Only one of --name, --email, or --zuidstring may be specified.");
            return ValidationResult.Success();
        }
    }

    public sealed class AccountShowCommand : AsyncCommand<AccountShowSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public AccountShowCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            AccountShowSettings settings)
        {
            var view = await _service.ShowAccountAsync(settings.Name, settings.Email, settings.Zuid);
            _output.WriteJson(view);
            return 0;
        }
    }

    // ─── account set-default ──────────────────────────────────────────────────

    public sealed class AccountSetDefaultSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--email <EMAIL>")]
        public string? Email { get; init; }

        [CommandOption("--Zuid|--zuidstring <ZUIDSTRING>")]
        public string? Zuid { get; init; }

        public override ValidationResult Validate()
        {
            var count = (string.IsNullOrWhiteSpace(Name) ? 0 : 1)
                      + (string.IsNullOrWhiteSpace(Email) ? 0 : 1)
                      + (string.IsNullOrWhiteSpace(Zuid) ? 0 : 1);
            if (count == 0)
                return ValidationResult.Error("One of --name, --email, or --zuidstring is required.");
            if (count > 1)
                return ValidationResult.Error("Only one of --name, --email, or --zuidstring may be specified.");
            return ValidationResult.Success();
        }
    }

    public sealed class AccountSetDefaultCommand : AsyncCommand<AccountSetDefaultSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public AccountSetDefaultCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            AccountSetDefaultSettings settings)
        {
            await _service.SetDefaultAsync(settings.Name, settings.Email, settings.Zuid);
            _output.WriteJson(new { status = "ok", data = new { name = settings.Name ?? settings.Email ?? settings.Zuid } });
            return 0;
        }
    }

    // ─── account remove ───────────────────────────────────────────────────────

    public sealed class AccountRemoveSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--email <EMAIL>")]
        public string? Email { get; init; }

        [CommandOption("--Zuid|--zuidstring <ZUIDSTRING>")]
        public string? Zuid { get; init; }

        public override ValidationResult Validate()
        {
            var count = (string.IsNullOrWhiteSpace(Name) ? 0 : 1)
                      + (string.IsNullOrWhiteSpace(Email) ? 0 : 1)
                      + (string.IsNullOrWhiteSpace(Zuid) ? 0 : 1);
            if (count == 0)
                return ValidationResult.Error("One of --name, --email, or --zuidstring is required.");
            if (count > 1)
                return ValidationResult.Error("Only one of --name, --email, or --zuidstring may be specified.");
            return ValidationResult.Success();
        }
    }

    public sealed class AccountRemoveCommand : AsyncCommand<AccountRemoveSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public AccountRemoveCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            AccountRemoveSettings settings)
        {
            await _service.RemoveAccountAsync(settings.Name, settings.Email, settings.Zuid);
            _output.WriteJson(new { status = "ok", data = new { name = settings.Name ?? settings.Email ?? settings.Zuid } });
            return 0;
        }
    }

    // ─── account refresh (was re-auth) ────────────────────────────────────────

    public sealed class AccountRefreshSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--email <EMAIL>")]
        public string? Email { get; init; }

        [CommandOption("--Zuid|--zuidstring <ZUIDSTRING>")]
        public string? Zuid { get; init; }

        public override ValidationResult Validate()
        {
            var count = (string.IsNullOrWhiteSpace(Name) ? 0 : 1)
                      + (string.IsNullOrWhiteSpace(Email) ? 0 : 1)
                      + (string.IsNullOrWhiteSpace(Zuid) ? 0 : 1);
            if (count == 0)
                return ValidationResult.Error("One of --name, --email, or --zuidstring is required.");
            if (count > 1)
                return ValidationResult.Error("Only one of --name, --email, or --zuidstring may be specified.");
            return ValidationResult.Success();
        }
    }

    public sealed class AccountRefreshCommand : AsyncCommand<AccountRefreshSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public AccountRefreshCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            AccountRefreshSettings settings)
        {
            await _service.ReAuthAsync(settings.Name, settings.Email, settings.Zuid);
            _output.WriteJson(new { status = "ok", data = new { name = settings.Name ?? settings.Email ?? settings.Zuid } });
            return 0;
        }
    }

    /// <summary>[Deprecated] Use 'account refresh' instead.</summary>
    public sealed class AccountReAuthDeprecatedCommand : AsyncCommand<AccountRefreshSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public AccountReAuthDeprecatedCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, AccountRefreshSettings settings)
        {
            DeprecationHelper.Warn("account re-auth", "account refresh");
            return await new AccountRefreshCommand(_service, _output)
                .ExecuteAsync(context, settings).ConfigureAwait(false);
        }
    }

    // ─── account rename ───────────────────────────────────────────────────────

    public sealed class AccountRenameSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--email <EMAIL>")]
        public string? Email { get; init; }

        [CommandOption("--Zuid|--zuidstring <ZUIDSTRING>")]
        public string? Zuid { get; init; }

        [CommandOption("--to|--new-name <NEW_NAME>")]
        public string? To { get; init; }

        public override ValidationResult Validate()
        {
            var count = (string.IsNullOrWhiteSpace(Name) ? 0 : 1)
                      + (string.IsNullOrWhiteSpace(Email) ? 0 : 1)
                      + (string.IsNullOrWhiteSpace(Zuid) ? 0 : 1);
            if (count == 0)
                return ValidationResult.Error("One of --name, --email, or --zuidstring is required.");
            if (count > 1)
                return ValidationResult.Error("Only one of --name, --email, or --zuidstring may be specified.");
            if (string.IsNullOrWhiteSpace(To))
                return ValidationResult.Error("--new-name is required.");
            if (To.IndexOfAny(['/', '\\', ':', '*', '?']) >= 0)
                return ValidationResult.Error("--new-name must not contain / \\ : * ? characters.");
            return ValidationResult.Success();
        }
    }

    public sealed class AccountRenameCommand : AsyncCommand<AccountRenameSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public AccountRenameCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, AccountRenameSettings settings)
        {
            var (oldName, newName) = await _service.RenameAccountAsync(
                settings.Name, settings.Email, settings.Zuid, settings.To!);
            _output.WriteJson(new { status = "ok", data = new { old_name = oldName, new_name = newName } });
            return 0;
        }
    }

    // ─── account use <NAME> ───────────────────────────────────────────────────

    public sealed class AccountUseSettings : CommandSettings
    {
        [CommandArgument(0, "<NAME>")]
        public string? Name { get; set; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("<NAME> is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class AccountUseCommand : AsyncCommand<AccountUseSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public AccountUseCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, AccountUseSettings settings)
        {
            await _service.SetDefaultAsync(settings.Name);
            _output.WriteJson(new { status = "ok", data = new { name = settings.Name } });
            return 0;
        }
    }

    // ─── account login ────────────────────────────────────────────────────────

    public sealed class AccountLoginSettings : GlobalSettings
    {
        /// <summary>
        /// Optional account name. When omitted, the name is derived from the email address
        /// returned by the Zoho user-info endpoint (replacing <c>@</c> and <c>.</c> with <c>_</c>).
        /// </summary>
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        /// <summary>
        /// Additional comma-separated OAuth scopes (additive to any configured scope-file).
        /// </summary>
        [CommandOption("--scope <SCOPE>")]
        public string? Scope { get; init; }

        public override ValidationResult Validate()
        {
            if (Name is not null && Name.IndexOfAny(['/', '\\', ':', '*', '?']) >= 0)
                return ValidationResult.Error("--name must not contain / \\ : * ? characters.");
            return ValidationResult.Success();
        }
    }

    public sealed class AccountLoginCommand : AsyncCommand<AccountLoginSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;
        private readonly ICliSettingsStore _settingsStore;

        public AccountLoginCommand(IAccountService service, IOutputWriter output, ICliSettingsStore settingsStore)
        {
            _service = service;
            _output = output;
            _settingsStore = settingsStore;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, AccountLoginSettings settings)
        {
            var clientId = Environment.GetEnvironmentVariable("ZOHO_CLIENT_ID");
            if (string.IsNullOrWhiteSpace(clientId))
                throw new ZapiCliException(
                    "ZOHO_CLIENT_ID is not set. Configure an env-file via 'zapi-cli config set env-file <path>'.",
                    ErrorCodes.ENV_FILE_NOT_CONFIGURED,
                    exitCode: 1);

            var cliSettings = await _settingsStore.LoadAsync(default).ConfigureAwait(false);

            // Collect scopes from --scope flag.
            var scopeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(settings.Scope))
            {
                foreach (var s in settings.Scope.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    scopeSet.Add(s);
            }

            // Collect scopes from configured scope-file.
            if (cliSettings.ScopeFile is not null)
            {
                if (!File.Exists(cliSettings.ScopeFile))
                    throw new ZapiCliException(
                        $"Scope file not found: {cliSettings.ScopeFile}. Update with 'zapi-cli config set scope-file <path>'.",
                        ErrorCodes.IO_ERROR,
                        exitCode: 1);

                var lines = await File.ReadAllLinesAsync(cliSettings.ScopeFile).ConfigureAwait(false);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                        continue;
                    foreach (var s in trimmed.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                        scopeSet.Add(s);
                }
            }

            if (scopeSet.Count == 0)
                throw new ZapiCliException(
                    "No scopes provided. Pass --scope or configure a scope file via 'zapi-cli config set scope-file <path>'.",
                    ErrorCodes.SCOPE_FILE_NOT_CONFIGURED,
                    exitCode: 1);

            var clientSecret = Environment.GetEnvironmentVariable("ZOHO_CLIENT_SECRET");

            var (name, dc) = await _service.MobileLoginAsync(
                settings.Name,
                clientId,
                scopeSet.ToArray(),
                clientSecret,
                default).ConfigureAwait(false);

            _output.WriteJson(new { status = "ok", data = new { name, dc } });
            return 0;
        }
    }
}

