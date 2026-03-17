using System.ComponentModel;
using Spectre.Console.Cli;
using ZapiCli.Core;
using ZapiCli.Core.Trace;

namespace ZapiCli.Commands;

// ─── Settings ────────────────────────────────────────────────────────────────

public sealed class StartSessionSettings : CommandSettings
{
    [CommandOption("--name|-n")]
    [Description("Unique name for the trace session (e.g. desk-tickets-2026-03-17). " +
                 "Allowed characters: [a-zA-Z0-9_.-], max 64 characters.")]
    public required string Name { get; init; }
}

public sealed class ExportSessionSettings : CommandSettings
{
    [CommandOption("--name|-n")]
    [Description("Name of the trace session to export.")]
    public required string Name { get; init; }

    [CommandOption("--truncate-body")]
    [Description("Truncate request_body and response_body to this many UTF-8 bytes in the output. " +
                 "The JSONL file on disk is not modified.")]
    public int? TruncateBody { get; init; }

    [CommandOption("--type")]
    [Description("Filter by entry type. Accepted values: 'api', 'pex'.")]
    public string? Type { get; init; }
}

public sealed class CloseSessionSettings : CommandSettings
{
    [CommandOption("--name|-n")]
    [Description("Name of the trace session to close.")]
    public required string Name { get; init; }
}

public sealed class RemoveSessionSettings : CommandSettings
{
    [CommandOption("--name|-n")]
    [Description("Name of the trace session to remove.")]
    public required string Name { get; init; }
}

// ─── Commands ─────────────────────────────────────────────────────────────────

/// <summary>
/// Starts a new named trace session. Subsequent <c>api call</c> invocations will
/// automatically append entries to this session's <c>trace.jsonl</c> file (ADR-0007).
/// </summary>
public sealed class StartSessionCommand : AsyncCommand<StartSessionSettings>
{
    private readonly TraceSession _traceSession;
    private readonly IOutputWriter _output;

    public StartSessionCommand(TraceSession traceSession, IOutputWriter output)
    {
        _traceSession = traceSession;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, StartSessionSettings settings)
    {
        var entry = await _traceSession.StartSessionAsync(settings.Name);
        _output.WriteSuccess(entry);
        return 0;
    }
}

/// <summary>Returns a JSON array of all trace sessions.</summary>
public sealed class ListSessionsCommand : AsyncCommand<CommandSettings>
{
    private readonly TraceSession _traceSession;
    private readonly IOutputWriter _output;

    public ListSessionsCommand(TraceSession traceSession, IOutputWriter output)
    {
        _traceSession = traceSession;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, CommandSettings settings)
    {
        var sessions = await _traceSession.ListSessionsAsync();
        _output.WriteSuccess(sessions);
        return 0;
    }
}

/// <summary>
/// Exports all entries from the named session as a JSON array.
/// The JSONL file is not modified (non-destructive, ADR-0007).
/// </summary>
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
            settings.Name,
            settings.TruncateBody,
            settings.Type);
        _output.WriteSuccess(entries);
        return 0;
    }
}

/// <summary>
/// Marks the named trace session as closed. The JSONL file is preserved for future export.
/// </summary>
public sealed class CloseSessionCommand : AsyncCommand<CloseSessionSettings>
{
    private readonly TraceSession _traceSession;
    private readonly IOutputWriter _output;

    public CloseSessionCommand(TraceSession traceSession, IOutputWriter output)
    {
        _traceSession = traceSession;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, CloseSessionSettings settings)
    {
        var entry = await _traceSession.CloseSessionAsync(settings.Name);
        _output.WriteSuccess(entry);
        return 0;
    }
}

/// <summary>
/// Removes the named session entry from the index and deletes its trace directory.
/// Throws <c>SESSION_NOT_FOUND</c> if the session does not exist.
/// </summary>
public sealed class RemoveSessionCommand : AsyncCommand<RemoveSessionSettings>
{
    private readonly TraceSession _traceSession;
    private readonly IOutputWriter _output;

    public RemoveSessionCommand(TraceSession traceSession, IOutputWriter output)
    {
        _traceSession = traceSession;
        _output = output;
    }

    public override async Task<int> ExecuteAsync(CommandContext context, RemoveSessionSettings settings)
    {
        await _traceSession.RemoveSessionAsync(settings.Name);
        _output.WriteSuccess(new { removed = settings.Name });
        return 0;
    }
}
