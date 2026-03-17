using Spectre.Console.Cli;
using ZapiCli.Core;

namespace ZapiCli.Commands;

// ─── util time-ms ─────────────────────────────────────────────────────────────

public sealed class UtilTimeMsCommand : AsyncCommand<GlobalSettings>
{
    private readonly IOutputWriter _output;

    public UtilTimeMsCommand(IOutputWriter output) => _output = output;

    public override Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _output.WriteSuccess(new { ts });
        return Task.FromResult(0);
    }
}

// ─── util uuid ────────────────────────────────────────────────────────────────

public sealed class UtilUuidCommand : AsyncCommand<GlobalSettings>
{
    private readonly IOutputWriter _output;

    public UtilUuidCommand(IOutputWriter output) => _output = output;

    public override Task<int> ExecuteAsync(CommandContext context, GlobalSettings settings)
    {
        var uuid = Guid.NewGuid().ToString("D");
        _output.WriteSuccess(new { uuid });
        return Task.FromResult(0);
    }
}
