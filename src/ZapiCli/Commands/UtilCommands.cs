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

    // ─── util time-now ───────────────────────────────────────────────────────

    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5),
            "India Standard Time", "India Standard Time");

    public sealed class UtilTimeNowSettings : GlobalSettings { }

    public sealed class UtilTimeNowCommand : AsyncCommand<UtilTimeNowSettings>
    {
        private readonly IOutputWriter _output;

        public UtilTimeNowCommand(IOutputWriter output)
        {
            _output = output;
        }

        public override Task<int> ExecuteAsync(
            CommandContext context,
            UtilTimeNowSettings settings)
        {
            var istNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
            var now = istNow.ToString("dd/MM/yy HH:mm:ss.fff");
            _output.WriteJson(new { now });
            return Task.FromResult(0);
        }
    }
}
