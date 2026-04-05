using Spectre.Console;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Commands;

/// <summary>
/// The <c>scope</c> command group: add, remove, list.
/// ZohoCorpGuard fires in all scope commands via <see cref="IAccountService"/>.
/// </summary>
internal static class ScopeCommands
{
    // ─── scope add ────────────────────────────────────────────────────────────

    public sealed class ScopeAddSettings : GlobalSettings
    {
        /// <summary>
        /// One or more scopes to add. May be comma-separated (e.g. "ZohoDesk.Tickets.READ,ZohoDesk.Reports.READ").
        /// </summary>
        [CommandOption("--scope <SCOPE>")]
        public string? Scope { get; init; }

        /// <summary>
        /// Local port for the OAuth callback server. Must register
        /// http://localhost:{PORT}/callback as a redirect URI in the Zoho Developer Console.
        /// </summary>
        [CommandOption("--port <PORT>")]
        public int Port { get; init; } = OAuthConstants.DefaultCallbackPort;

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Scope))
                return ValidationResult.Error("--scope is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class ScopeAddCommand : AsyncCommand<ScopeAddSettings>
    {
        private readonly IAccountStore _accountStore;
        private readonly IAccountService _accountService;
        private readonly IOutputWriter _output;

        public ScopeAddCommand(
            IAccountStore accountStore,
            IAccountService accountService,
            IOutputWriter output)
        {
            _accountStore = accountStore;
            _accountService = accountService;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ScopeAddSettings settings)
        {
            // Resolve account name — explicit flag or configured default.
            string accountName;
            if (settings.Account is not null)
            {
                var acct = await _accountStore.FindAsync(settings.Account).ConfigureAwait(false)
                    ?? throw new ZapiCliException($"Account '{settings.Account}' not found.", ErrorCodes.ACCOUNT_NOT_FOUND, 1);
                accountName = acct.Name;
            }
            else if (settings.AccountEmail is not null)
            {
                var acct = await _accountStore.FindByEmailAsync(settings.AccountEmail).ConfigureAwait(false)
                    ?? throw new ZapiCliException($"No account found with email '{settings.AccountEmail}'.", ErrorCodes.ACCOUNT_NOT_FOUND, 1);
                accountName = acct.Name;
            }
            else if (settings.AccountZuidString is not null)
            {
                var acct = await _accountStore.FindByZuidAsync(settings.AccountZuidString).ConfigureAwait(false)
                    ?? throw new ZapiCliException($"No account found with ZUID '{settings.AccountZuidString}'.", ErrorCodes.ACCOUNT_NOT_FOUND, 1);
                accountName = acct.Name;
            }
            else
            {
                accountName = (await _accountStore.GetDefaultAsync().ConfigureAwait(false)).Name;
            }

            // Parse comma-separated scope string.
            var incoming = settings.Scope!
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s));

            // Delegate to service (handles ZohoCorp block, dedup, browser consent, save).
            var (name, updatedScopes) = await _accountService
                .AddScopesAsync(accountName, incoming, settings.Port)
                .ConfigureAwait(false);

            _output.WriteJson(new
            {
                status = "ok",
                data = new { account = name, scopes = updatedScopes }
            });
            return 0;
        }
    }

    // ─── scope list ───────────────────────────────────────────────────────────

    public sealed class ScopeListSettings : GlobalSettings { }

    public sealed class ScopeListCommand : AsyncCommand<ScopeListSettings>
    {
        private readonly IAccountStore _accountStore;
        private readonly IAccountService _accountService;
        private readonly IOutputWriter _output;

        public ScopeListCommand(
            IAccountStore accountStore,
            IAccountService accountService,
            IOutputWriter output)
        {
            _accountStore = accountStore;
            _accountService = accountService;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ScopeListSettings settings)
        {
            string accountName;
            if (settings.Account is not null)
            {
                var acct = await _accountStore.FindAsync(settings.Account).ConfigureAwait(false)
                    ?? throw new ZapiCliException($"Account '{settings.Account}' not found.", ErrorCodes.ACCOUNT_NOT_FOUND, 1);
                accountName = acct.Name;
            }
            else if (settings.AccountEmail is not null)
            {
                var acct = await _accountStore.FindByEmailAsync(settings.AccountEmail).ConfigureAwait(false)
                    ?? throw new ZapiCliException($"No account found with email '{settings.AccountEmail}'.", ErrorCodes.ACCOUNT_NOT_FOUND, 1);
                accountName = acct.Name;
            }
            else if (settings.AccountZuidString is not null)
            {
                var acct = await _accountStore.FindByZuidAsync(settings.AccountZuidString).ConfigureAwait(false)
                    ?? throw new ZapiCliException($"No account found with ZUID '{settings.AccountZuidString}'.", ErrorCodes.ACCOUNT_NOT_FOUND, 1);
                accountName = acct.Name;
            }
            else
            {
                accountName = (await _accountStore.GetDefaultAsync().ConfigureAwait(false)).Name;
            }

            var scopes = await _accountService
                .GetScopesAsync(accountName)
                .ConfigureAwait(false);

            // Plain JSON array of scope strings (no wrapper envelope).
            _output.WriteJson(scopes);
            return 0;
        }
    }

    // ─── deprecated shims (top-level 'scope' kept for backward compatibility) ─

    public sealed class ScopeAddDeprecatedCommand : AsyncCommand<ScopeAddSettings>
    {
        private readonly IAccountStore _accountStore;
        private readonly IAccountService _accountService;
        private readonly IOutputWriter _output;

        public ScopeAddDeprecatedCommand(
            IAccountStore accountStore,
            IAccountService accountService,
            IOutputWriter output)
        {
            _accountStore = accountStore;
            _accountService = accountService;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ScopeAddSettings settings)
        {
            DeprecationHelper.Warn("scope add", "account scope add");
            return await new ScopeAddCommand(_accountStore, _accountService, _output)
                .ExecuteAsync(context, settings).ConfigureAwait(false);
        }
    }

    public sealed class ScopeListDeprecatedCommand : AsyncCommand<ScopeListSettings>
    {
        private readonly IAccountStore _accountStore;
        private readonly IAccountService _accountService;
        private readonly IOutputWriter _output;

        public ScopeListDeprecatedCommand(
            IAccountStore accountStore,
            IAccountService accountService,
            IOutputWriter output)
        {
            _accountStore = accountStore;
            _accountService = accountService;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ScopeListSettings settings)
        {
            DeprecationHelper.Warn("scope list", "account scope list");
            return await new ScopeListCommand(_accountStore, _accountService, _output)
                .ExecuteAsync(context, settings).ConfigureAwait(false);
        }
    }
}

