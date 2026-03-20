using ZapiCli.Core.Trace;

namespace ZapiCli.Tests.Fakes;

/// <summary>
/// In-memory <see cref="ITraceWriter"/> for unit tests.
/// Records all append calls without touching the file system.
/// </summary>
public sealed class FakeTraceWriter : ITraceWriter
{
    public List<ApiTraceEntry> ApiEntries { get; } = [];
    public List<PexTraceEntry> PexEntries { get; } = [];

    public Task AppendApiEntryAsync(ApiTraceEntry entry, CancellationToken ct = default)
    {
        ApiEntries.Add(entry);
        return Task.CompletedTask;
    }

    public Task AppendPexEntryAsync(PexTraceEntry entry, CancellationToken ct = default)
    {
        PexEntries.Add(entry);
        return Task.CompletedTask;
    }
}
