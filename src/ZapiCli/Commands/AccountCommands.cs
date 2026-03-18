using Spectre.Console;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Commands;

/// <summary>
/// All six <c>account</c> subcommands. Command classes are thin: they validate flags,
/// delegate all business logic to <see cref="IAccountService"/>, and write output via
/// <see cref="IOutputWriter"/>. No domain logic lives here (ADR-0007).
/// </summary>
internal static class AccountCommands
{
    // ─── account add ─────────────────────────────────────────────────────────

    public sealed class AccountAddSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--code <CODE>")]
        public string? Code { get; init; }

        [CommandOption("--client-id <CLIENT_ID>")]
        public string? ClientId { get; init; }

        [CommandOption("--client-secret <CLIENT_SECRET>")]
        public string? ClientSecret { get; init; }

        /// <summary>
        /// Redirect URI registered in the Zoho Developer Console Self-Client app.
        /// Must match exactly what was registered. Not actually redirected to.
        /// Defaults to <c>https://www.zoho.com</c>.
        /// </summary>
        [CommandOption("--redirect-uri <REDIRECT_URI>")]
        public string RedirectUri { get; init; } = "https://www.zoho.com";

        /// <summary>Zoho datacenter short name. Defaults to <c>us</c>.</summary>
        [CommandOption("--dc <DC>")]
        public string Dc { get; init; } = "us";

        private static readonly HashSet<string> ValidDcs =
            ["us", "eu", "in", "au", "cn", "jp", "sa", "uk", "ca"];

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("--name is required.");
            if (string.IsNullOrWhiteSpace(Code))
                return ValidationResult.Error("--code is required. Generate one from the Zoho Developer Console (Self-Client → Generate Code).");
            if (string.IsNullOrWhiteSpace(ClientId))
                return ValidationResult.Error("--client-id is required.");
            if (string.IsNullOrWhiteSpace(ClientSecret))
                return ValidationResult.Error("--client-secret is required.");
            if (!ValidDcs.Contains(Dc))
                return ValidationResult.Error(
                    $"--dc '{Dc}' is not valid. Valid values: {string.Join(", ", ValidDcs)}.");
            return ValidationResult.Success();
        }
    }

    public sealed class AccountAddCommand : AsyncCommand<AccountAddSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public AccountAddCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            AccountAddSettings settings)
        {
            var (name, dc) = await _service.AddAccountAsync(
                settings.Name!,
                settings.Code!,
                settings.RedirectUri,
                settings.ClientId!,
                settings.ClientSecret!,
                settings.Dc);

            _output.WriteJson(new { status = "ok", data = new { name, dc } });
            return 0;
        }
    }

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

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("--name is required.");
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
            var view = await _service.ShowAccountAsync(settings.Name!);
            _output.WriteJson(view);
            return 0;
        }
    }

    // ─── account set-default ──────────────────────────────────────────────────

    public sealed class AccountSetDefaultSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("--name is required.");
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
            await _service.SetDefaultAsync(settings.Name!);
            _output.WriteJson(new { status = "ok", data = new { name = settings.Name } });
            return 0;
        }
    }

    // ─── account remove ───────────────────────────────────────────────────────

    public sealed class AccountRemoveSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("--name is required.");
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
            await _service.RemoveAccountAsync(settings.Name!);
            _output.WriteJson(new { status = "ok", data = new { name = settings.Name } });
            return 0;
        }
    }

    // ─── account re-auth ──────────────────────────────────────────────────────

    public sealed class AccountReAuthSettings : GlobalSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("--name is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class AccountReAuthCommand : AsyncCommand<AccountReAuthSettings>
    {
        private readonly IAccountService _service;
        private readonly IOutputWriter _output;

        public AccountReAuthCommand(IAccountService service, IOutputWriter output)
        {
            _service = service;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(
            CommandContext context,
            AccountReAuthSettings settings)
        {
            await _service.ReAuthAsync(settings.Name!);
            _output.WriteJson(new { status = "ok", data = new { name = settings.Name } });
            return 0;
        }
    }
}
