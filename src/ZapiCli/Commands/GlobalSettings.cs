using System.ComponentModel;
using Spectre.Console.Cli;

namespace ZapiCli.Commands;

/// <summary>
/// Base settings shared by all zapi-cli commands.
/// </summary>
public class GlobalSettings : CommandSettings
{
    [CommandOption("--account|-a")]
    [Description("The account name to use for this operation.")]
    public string? Account { get; init; }

    [CommandOption("--json")]
    [Description("Output result as JSON (default: true).")]
    [DefaultValue(true)]
    public bool Json { get; init; } = true;

    [CommandOption("--no-input")]
    [Description("Disable all interactive prompts. Exit 1 with structured error instead.")]
    public bool NoInput { get; init; }
}
