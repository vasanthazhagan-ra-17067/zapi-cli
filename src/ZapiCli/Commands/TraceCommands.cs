using Spectre.Console;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Trace;

namespace ZapiCli.Commands;

/// <summary>
/// The <c>trace</c> command group: <c>trace session</c> and <c>trace config</c> subgroups.
/// All commands are thin — they validate flags, call <see cref="ITraceSession"/> /
/// <see cref="ICliSettingsStore"/> / <see cref="TraceExporter"/>, and write output via
/// <see cref="IOutputWriter"/>. No domain logic here.
/// </summary>
internal static class TraceCommands
{
    // ─── Shared resolution helper ─────────────────────────────────────────────

    /// <summary>
    /// Resolves a session from --id or --name. Throws SESSION_NOT_FOUND or SESSION_AMBIGUOUS as needed.
    /// </summary>
    private static async Task<(string sessionId, string sessionName)> ResolveSessionAsync(
        ITraceSession traceSession,
        string? id,
        string? name,
        CancellationToken ct = default)
    {
        if (id != null)
        {
            var session = await traceSession.FindByIdAsync(id, ct).ConfigureAwait(false);
            if (session is null)
                throw new ZapiCliException($"Session '{id}' not found.", ErrorCodes.SESSION_NOT_FOUND);
            return (session.UniqueId, session.Name);
        }

        var matches = await traceSession.FindByNameAsync(name!, ct).ConfigureAwait(false);
        if (matches.Count == 0)
            throw new ZapiCliException($"Session '{name}' not found.", ErrorCodes.SESSION_NOT_FOUND);
        if (matches.Count > 1)
            throw new ZapiCliException(
                $"Multiple sessions named '{name}'. Use --id with one of: " +
                string.Join(", ", matches.Select(m => m.UniqueId)) + ".",
                ErrorCodes.SESSION_AMBIGUOUS);
        return (matches[0].UniqueId, matches[0].Name);
    }

    // ─── trace session start ──────────────────────────────────────────────────

    public sealed class StartSessionSettings : CommandSettings
    {
        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--export-path <PATH>")]
        public string? ExportPath { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(Name))
                return ValidationResult.Error("--name is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class StartSessionCommand : AsyncCommand<StartSessionSettings>
    {
        private readonly ITraceSession _traceSession;
        private readonly IOutputWriter _output;

        public StartSessionCommand(ITraceSession traceSession, IOutputWriter output)
        {
            _traceSession = traceSession;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, StartSessionSettings settings)
        {
            var entry = await _traceSession.StartSessionAsync(settings.Name!, settings.ExportPath)
                .ConfigureAwait(false);

            _output.WriteJson(new
            {
                status = "ok",
                data = new
                {
                    unique_id = entry.UniqueId,
                    name = entry.Name,
                    export_path = entry.ExportPath,
                    start_time = entry.StartTime,
                    status = entry.Status,
                },
            });
            return 0;
        }
    }

    // ─── trace session list ───────────────────────────────────────────────────

    public sealed class ListSessionsSettings : CommandSettings { }

    public sealed class ListSessionsCommand : AsyncCommand<ListSessionsSettings>
    {
        private readonly ITraceSession _traceSession;
        private readonly IOutputWriter _output;

        public ListSessionsCommand(ITraceSession traceSession, IOutputWriter output)
        {
            _traceSession = traceSession;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ListSessionsSettings settings)
        {
            var sessions = await _traceSession.ListSessionsAsync().ConfigureAwait(false);

            _output.WriteJson(sessions.Select(s => new
            {
                unique_id = s.UniqueId,
                name = s.Name,
                start_time = s.StartTime,
                entry_count = s.EntryCount,
                status = s.Status,
                export_path = s.ExportPath,
            }).ToList());

            return 0;
        }
    }

    // ─── trace session export ─────────────────────────────────────────────────

    public sealed class ExportSessionSettings : CommandSettings
    {
        [CommandOption("--id <ID>")]
        public string? Id { get; init; }

        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--truncate-body <CHARS>")]
        public int? TruncateBody { get; init; }

        [CommandOption("--type <TYPE>")]
        public string? Type { get; init; }

        public override ValidationResult Validate()
        {
            if (Id is null && Name is null)
                return ValidationResult.Error("Either --id or --name is required.");
            if (Id is not null && Name is not null)
                return ValidationResult.Error("Cannot specify both --id and --name.");
            if (Type is not null && Type != "api" && Type != "pex")
                return ValidationResult.Error("--type must be 'api' or 'pex'.");
            return ValidationResult.Success();
        }
    }

    public sealed class ExportSessionCommand : AsyncCommand<ExportSessionSettings>
    {
        private readonly TraceExporter _exporter;
        private readonly IOutputWriter _output;

        public ExportSessionCommand(TraceExporter exporter, IOutputWriter output)
        {
            _exporter = exporter;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ExportSessionSettings settings)
        {
            var entries = await _exporter.ExportAsync(
                settings.Id,
                settings.Name,
                settings.Type,
                settings.TruncateBody).ConfigureAwait(false);

            _output.WriteJson(entries);
            return 0;
        }
    }

    // ─── trace session close ──────────────────────────────────────────────────

    public sealed class CloseSessionSettings : CommandSettings
    {
        [CommandOption("--id <ID>")]
        public string? Id { get; init; }

        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        [CommandOption("--wait-ms <MS>")]
        public int WaitMs { get; init; } = 5000;

        public override ValidationResult Validate()
        {
            if (Id is null && Name is null)
                return ValidationResult.Error("Either --id or --name is required.");
            if (Id is not null && Name is not null)
                return ValidationResult.Error("Cannot specify both --id and --name.");
            return ValidationResult.Success();
        }
    }

    public sealed class CloseSessionCommand : AsyncCommand<CloseSessionSettings>
    {
        private readonly ITraceSession _traceSession;
        private readonly IOutputWriter _output;

        public CloseSessionCommand(ITraceSession traceSession, IOutputWriter output)
        {
            _traceSession = traceSession;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, CloseSessionSettings settings)
        {
            var (sessionId, sessionName) = await ResolveSessionAsync(_traceSession, settings.Id, settings.Name)
                .ConfigureAwait(false);

            await _traceSession.CloseSessionAsync(sessionId, settings.WaitMs).ConfigureAwait(false);

            _output.WriteJson(new
            {
                status = "ok",
                data = new { unique_id = sessionId, name = sessionName },
            });
            return 0;
        }
    }

    // ─── trace session reopen ─────────────────────────────────────────────────

    public sealed class ReopenSessionSettings : CommandSettings
    {
        [CommandOption("--id <ID>")]
        public string? Id { get; init; }

        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        public override ValidationResult Validate()
        {
            if (Id is null && Name is null)
                return ValidationResult.Error("Either --id or --name is required.");
            if (Id is not null && Name is not null)
                return ValidationResult.Error("Cannot specify both --id and --name.");
            return ValidationResult.Success();
        }
    }

    public sealed class ReopenSessionCommand : AsyncCommand<ReopenSessionSettings>
    {
        private readonly ITraceSession _traceSession;
        private readonly IOutputWriter _output;

        public ReopenSessionCommand(ITraceSession traceSession, IOutputWriter output)
        {
            _traceSession = traceSession;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, ReopenSessionSettings settings)
        {
            var (sessionId, sessionName) = await ResolveSessionAsync(_traceSession, settings.Id, settings.Name)
                .ConfigureAwait(false);

            await _traceSession.ReopenSessionAsync(sessionId).ConfigureAwait(false);

            _output.WriteJson(new
            {
                status = "ok",
                data = new { unique_id = sessionId, name = sessionName, status = "active" },
            });
            return 0;
        }
    }

    // ─── trace session remove ─────────────────────────────────────────────────

    public sealed class RemoveSessionSettings : CommandSettings
    {
        [CommandOption("--id <ID>")]
        public string? Id { get; init; }

        [CommandOption("--name <NAME>")]
        public string? Name { get; init; }

        public override ValidationResult Validate()
        {
            if (Id is null && Name is null)
                return ValidationResult.Error("Either --id or --name is required.");
            if (Id is not null && Name is not null)
                return ValidationResult.Error("Cannot specify both --id and --name.");
            return ValidationResult.Success();
        }
    }

    public sealed class RemoveSessionCommand : AsyncCommand<RemoveSessionSettings>
    {
        private readonly ITraceSession _traceSession;
        private readonly IOutputWriter _output;

        public RemoveSessionCommand(ITraceSession traceSession, IOutputWriter output)
        {
            _traceSession = traceSession;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, RemoveSessionSettings settings)
        {
            var (sessionId, sessionName) = await ResolveSessionAsync(_traceSession, settings.Id, settings.Name)
                .ConfigureAwait(false);

            await _traceSession.RemoveSessionAsync(sessionId).ConfigureAwait(false);

            _output.WriteJson(new
            {
                status = "ok",
                data = new { unique_id = sessionId, name = sessionName },
            });
            return 0;
        }
    }

    // ─── trace config set ─────────────────────────────────────────────────────

    public sealed class TraceConfigSetSettings : CommandSettings
    {
        [CommandOption("--default-export-path <PATH>")]
        public string? DefaultExportPath { get; init; }

        public override ValidationResult Validate()
        {
            if (string.IsNullOrWhiteSpace(DefaultExportPath))
                return ValidationResult.Error("--default-export-path is required.");
            return ValidationResult.Success();
        }
    }

    public sealed class TraceConfigSetCommand : AsyncCommand<TraceConfigSetSettings>
    {
        private readonly ICliSettingsStore _settingsStore;
        private readonly IOutputWriter _output;

        public TraceConfigSetCommand(ICliSettingsStore settingsStore, IOutputWriter output)
        {
            _settingsStore = settingsStore;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, TraceConfigSetSettings settings)
        {
            var absPath = Path.GetFullPath(settings.DefaultExportPath!);
            var existing = await _settingsStore.LoadAsync().ConfigureAwait(false);
            var updated = existing with { TraceDefaultExportPath = absPath };
            await _settingsStore.SaveAsync(updated).ConfigureAwait(false);

            _output.WriteJson(new
            {
                status = "ok",
                data = new { trace_default_export_path = absPath },
            });
            return 0;
        }
    }

    // ─── trace config show ────────────────────────────────────────────────────

    public sealed class TraceConfigShowSettings : CommandSettings { }

    public sealed class TraceConfigShowCommand : AsyncCommand<TraceConfigShowSettings>
    {
        private readonly ICliSettingsStore _settingsStore;
        private readonly IOutputWriter _output;

        public TraceConfigShowCommand(ICliSettingsStore settingsStore, IOutputWriter output)
        {
            _settingsStore = settingsStore;
            _output = output;
        }

        public override async Task<int> ExecuteAsync(CommandContext context, TraceConfigShowSettings settings)
        {
            var config = await _settingsStore.LoadAsync().ConfigureAwait(false);
            _output.WriteJson(new { trace_default_export_path = config.TraceDefaultExportPath });
            return 0;
        }
    }
}
