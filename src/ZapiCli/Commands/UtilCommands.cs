using Spectre.Console.Cli;
using ZapiCli.Core;

namespace ZapiCli.Commands;

/// <summary>
/// The <c>util</c> command group. Stateless helpers — no account, auth, or network dependency.
/// </summary>
internal static class UtilCommands
{
    // ─── util time-ms ────────────────────────────────────────────────────────

    public sealed class UtilTimeMsSettings : GlobalSettings { }

    public sealed class UtilTimeMsCommand : AsyncCommand<UtilTimeMsSettings>
    {
        private readonly IOutputWriter _output;

        public UtilTimeMsCommand(IOutputWriter output)
        {
            _output = output;
        }

        public override Task<int> ExecuteAsync(
            CommandContext context,
            UtilTimeMsSettings settings)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _output.WriteJson(new { ts });
            return Task.FromResult(0);
        }
    }

    // ─── util uuid ───────────────────────────────────────────────────────────

    public sealed class UtilUuidSettings : GlobalSettings { }

    public sealed class UtilUuidCommand : AsyncCommand<UtilUuidSettings>
    {
        private readonly IOutputWriter _output;

        public UtilUuidCommand(IOutputWriter output)
        {
            _output = output;
        }

        public override Task<int> ExecuteAsync(
            CommandContext context,
            UtilUuidSettings settings)
        {
            var uuid = Guid.NewGuid().ToString();
            _output.WriteJson(new { uuid });
            return Task.FromResult(0);
        }
    }
}
