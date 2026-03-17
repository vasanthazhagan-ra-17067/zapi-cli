using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;

namespace ZapiCli.Commands;

// ─── Settings ────────────────────────────────────────────────────────────────

public sealed class AccountAddSettings : CommandSettings
{
    [CommandArgument(0, "<name>")]
    [Description("Unique account name.")]
    public required string Name { get; init; }

    [CommandOption("--auth-type")]
    [Description("Authentication type. Must be 'pat' in v1.")]
    public string AuthType { get; init; } = "pat";

    [CommandOption("--token|-t")]
    [Description("Personal Access Token value.")]
    public string? Token { get; init; }

    [CommandOption("--domain|-d")]
    [Description("Zoho domain to use (default: zoho.com).")]
    public string Domain { get; init; } = "zoho.com";

    public override ValidationResult Validate()
    {
        if (!AuthType.Equals("pat", StringComparison.OrdinalIgnoreCase))
            return ValidationResult.Error(
                $"--auth-type '{AuthType}' is not supported in v1. Only 'pat' is accepted.");

        if (string.IsNullOrWhiteSpace(Token))
            return ValidationResult.Error("--token is required when --auth-type is pat.");

        return ValidationResult.Success();
    }
}

/// <summary>Shared settings for account sub-commands that operate on a named account.</summary>
public sealed class AccountNameSettings : CommandSettings
{
    [CommandArgument(0, "<name>")]
    [Description("Account name.")]
    public required string Name { get; init; }
}

// ─── Commands ─────────────────────────────────────────────────────────────────

public sealed class AccountAddCommand : AsyncCommand<AccountAddSettings>
{
    private readonly AccountService _service;
    private readonly IOutputWriter _output;

    public AccountAddCommand(AccountService service, IOutputWriter output)
    {
        _service = service;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AccountAddSettings settings)
    {
        var name = await _service.AddAsync(
            settings.Name,
            settings.Domain,
            settings.Token!,
            "pat");

        _output.WriteSuccess(new { name });
        return 0;
    }
}

public sealed class AccountListCommand : AsyncCommand
{
    private readonly AccountService _service;
    private readonly IOutputWriter _output;

    public AccountListCommand(AccountService service, IOutputWriter output)
    {
        _service = service;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context)
    {
        var accounts = await _service.ListAsync();
        _output.WriteSuccess(accounts);
        return 0;
    }
}

public sealed class AccountRemoveCommand : AsyncCommand<AccountNameSettings>
{
    private readonly AccountService _service;
    private readonly IOutputWriter _output;

    public AccountRemoveCommand(AccountService service, IOutputWriter output)
    {
        _service = service;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AccountNameSettings settings)
    {
        await _service.RemoveAsync(settings.Name);
        _output.WriteSuccess(new { removed = settings.Name });
        return 0;
    }
}

public sealed class AccountShowCommand : AsyncCommand<AccountNameSettings>
{
    private readonly AccountService _service;
    private readonly IOutputWriter _output;

    public AccountShowCommand(AccountService service, IOutputWriter output)
    {
        _service = service;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AccountNameSettings settings)
    {
        var dto = await _service.ShowAsync(settings.Name);
        _output.WriteSuccess(dto);
        return 0;
    }
}

public sealed class AccountSetDefaultCommand : AsyncCommand<AccountNameSettings>
{
    private readonly AccountService _service;
    private readonly IOutputWriter _output;

    public AccountSetDefaultCommand(AccountService service, IOutputWriter output)
    {
        _service = service;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, AccountNameSettings settings)
    {
        await _service.SetDefaultAsync(settings.Name);
        _output.WriteSuccess(new { default_account = settings.Name });
        return 0;
    }
}

public sealed class AccountReAuthCommand : AsyncCommand<AccountNameSettings>
{
    public AccountReAuthCommand(IOutputWriter _) { }

    public override Task<int> ExecuteAsync(CommandContext context, AccountNameSettings settings)
    {
        throw new ZapiCliException(
            "re-auth is not yet supported; will be activated in v2 with OAuth2",
            ErrorCodes.NotImplemented,
            exitCode: 1);
    }
}
