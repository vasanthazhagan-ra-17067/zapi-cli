using System.ComponentModel;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Commands;

// ─── Settings ────────────────────────────────────────────────────────────────

public sealed class ScopeAddSettings : GlobalSettings
{
    [CommandOption("--scope|-s")]
    [Description("Scope string to add (e.g. ZohoDesk.Tickets.READ).")]
    public required string Scope { get; init; }
}

public sealed class ScopeRemoveSettings : GlobalSettings
{
    [CommandOption("--scope|-s")]
    [Description("Scope string to remove.")]
    public required string Scope { get; init; }
}

// ScopeListCommand uses GlobalSettings directly — no additional options needed.

// ─── Commands ─────────────────────────────────────────────────────────────────

public sealed class ScopeAddCommand : AsyncCommand<ScopeAddSettings>
{
    private readonly ScopeService _service;
    private readonly IOutputWriter _output;

    public ScopeAddCommand(ScopeService service, IOutputWriter output)
    {
        _service = service;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ScopeAddSettings settings)
    {
        var scopes = await _service.AddScopeAsync(settings.Account, settings.Scope);
        _output.WriteSuccess(new { scopes });
        return 0;
    }
}

public sealed class ScopeRemoveCommand : AsyncCommand<ScopeRemoveSettings>
{
    private readonly ScopeService _service;
    private readonly IOutputWriter _output;

    public ScopeRemoveCommand(ScopeService service, IOutputWriter output)
    {
        _service = service;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, ScopeRemoveSettings settings)
    {
        var scopes = await _service.RemoveScopeAsync(settings.Account, settings.Scope);
        _output.WriteSuccess(new { removed = settings.Scope, scopes });
        return 0;
    }
}

public sealed class ScopeListCommand : AsyncCommand<GlobalSettings>
{
    private readonly ScopeService _service;
    private readonly IOutputWriter _output;

    public ScopeListCommand(ScopeService service, IOutputWriter output)
    {
        _service = service;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var scopes = await _service.ListScopesAsync(settings.Account);
        _output.WriteSuccess(new { scopes });
        return 0;
    }
}
