using Spectre.Console.Cli;

namespace ZapiCli.Commands;

/// <summary>
/// Base settings inherited by all command settings classes.
/// Declares the global flags available on every zapi-cli command.
/// </summary>
public class GlobalSettings : CommandSettings
{
    /// <summary>Named account to use. Overrides the default account.</summary>
    [CommandOption("--account|-a <ACCOUNT>")]
    public string? Account { get; init; }

    /// <summary>Account email to use. Alternative to --account.</summary>
    [CommandOption("--account-email <EMAIL>")]
    public string? AccountEmail { get; init; }

    /// <summary>Account ZUID string to use. Alternative to --account.</summary>
    [CommandOption("--account-zuidstring <ZUIDSTRING>")]
    public string? AccountZuidString { get; init; }

    /// <summary>
    /// Emit JSON output. Defaults to <c>true</c>; plain-text output is not supported in v1.
    /// </summary>
    [CommandOption("--json")]
    public bool Json { get; init; } = true;

    /// <summary>Disable any interactive prompts. Exit with INVALID_ARGS if input is required.</summary>
    [CommandOption("--no-input")]
    public bool NoInput { get; init; }
}
