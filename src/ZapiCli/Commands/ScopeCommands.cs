using Spectre.Console;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Commands;

/// <summary>
/// The <c>scope</c> command group: add, remove, list.
/// Modifying scopes (add or remove) sets <see cref="AccountEntry.NeedsReauth"/> = true,
/// which triggers an automatic token refresh on the next <c>api call</c> (ADR-0002).
/// ZohoCorpGuard fires in all three commands via <see cref="IAccountService"/>.
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
            var accountName = settings.Account
                ?? (await _accountStore.GetDefaultAsync().ConfigureAwait(false)).Name;

            // Parse comma-separated scope string.
            var incoming = settings.Scope!
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s));

            // Delegate to service (handles ZohoCorp block, dedup, NeedsReauth, save).
            var (name, updatedScopes) = await _accountService
                .AddScopesAsync(accountName, incoming)
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
            var accountName = settings.Account
                ?? (await _accountStore.GetDefaultAsync().ConfigureAwait(false)).Name;

            var scopes = await _accountService
                .GetScopesAsync(accountName)
                .ConfigureAwait(false);

            // Plain JSON array of scope strings (no wrapper envelope).
            _output.WriteJson(scopes);
            return 0;
        }
    }
}

