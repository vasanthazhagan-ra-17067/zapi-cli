using Spectre.Console.Cli;
using ZapiCli.Core;

namespace ZapiCli.Commands;

/// <summary>
/// The <c>util</c> command group. Stateless helpers — no account, auth, or network dependency.
/// </summary>
internal static class UtilCommands
{
    // ─── util timestamp ──────────────────────────────────────────────────────

    public sealed class UtilTimestampSettings : GlobalSettings { }

    public sealed class UtilTimestampCommand : AsyncCommand<UtilTimestampSettings>
    {
        private readonly IOutputWriter _output;

        public UtilTimestampCommand(IOutputWriter output)
        {
            _output = output;
        }

        public override Task<int> ExecuteAsync(
            CommandContext context,
            UtilTimestampSettings settings)
        {
            var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _output.WriteJson(new { ts });
            return Task.FromResult(0);
        }
    }

    /// <summary>[Deprecated] Use 'util timestamp' instead.</summary>
    public sealed class UtilTimeMsDeprecatedCommand : AsyncCommand<UtilTimestampSettings>
    {
        private readonly IOutputWriter _output;

        public UtilTimeMsDeprecatedCommand(IOutputWriter output)
        {
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, UtilTimestampSettings settings)
        {
            DeprecationHelper.Warn("util time-ms", "util timestamp");
            return await new UtilTimestampCommand(_output)
                .ExecuteAsync(context, settings).ConfigureAwait(false);
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

    // ─── util now ────────────────────────────────────────────────────────────

    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5),
            "India Standard Time", "India Standard Time");

    public sealed class UtilNowSettings : GlobalSettings { }

    public sealed class UtilNowCommand : AsyncCommand<UtilNowSettings>
    {
        private readonly IOutputWriter _output;

        public UtilNowCommand(IOutputWriter output)
        {
            _output = output;
        }

        public override Task<int> ExecuteAsync(
            CommandContext context,
            UtilNowSettings settings)
        {
            var istNow = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
            var now = istNow.ToString("dd/MM/yy HH:mm:ss.fff");
            _output.WriteJson(new { now });
            return Task.FromResult(0);
        }
    }

    /// <summary>[Deprecated] Use 'util now' instead.</summary>
    public sealed class UtilTimeNowDeprecatedCommand : AsyncCommand<UtilNowSettings>
    {
        private readonly IOutputWriter _output;

        public UtilTimeNowDeprecatedCommand(IOutputWriter output)
        {
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, UtilNowSettings settings)
        {
            DeprecationHelper.Warn("util time-now", "util now");
            return await new UtilNowCommand(_output)
                .ExecuteAsync(context, settings).ConfigureAwait(false);
        }
    }
}
