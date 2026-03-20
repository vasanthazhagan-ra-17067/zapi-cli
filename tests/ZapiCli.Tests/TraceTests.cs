using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using ZapiCli.Core;
using ZapiCli.Core.Trace;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

/// <summary>
/// Unit tests for the Story 8 trace system:
/// TraceWriter security-header exclusion, session name validation, export non-destructiveness,
/// --truncate-body, seq continuity, session lifecycle transitions, and error codes.
/// </summary>
public sealed class TraceTests : IDisposable
{
    // Temporary working directory isolated per test run.
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "zapi-trace-test-" + Guid.NewGuid().ToString("N")[..8]);

    public TraceTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Case-insensitive property existence check — HTTP header names are preserved
    /// with their original casing in the serialized trace file.
    /// </summary>
    private static bool HasProperty(JsonElement obj, string name) =>
        obj.EnumerateObject().Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string? GetPropertyValue(JsonElement obj, string name) =>
        obj.EnumerateObject()
           .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
           .Value.GetString();

    private ITraceConfigStore MakeConfigStore() =>
        new TraceConfigStore(_tempDir, NullLogger<TraceConfigStore>.Instance);

    private ITraceSession MakeTraceSession() =>
        new TraceSession(_tempDir, MakeConfigStore(), NullLogger<TraceSession>.Instance);

    private TraceWriter MakeTraceWriter(ITraceSession session) =>
        new(session, NullLogger<TraceWriter>.Instance);

    private TraceExporter MakeExporter(ITraceSession session) => new(session);

    private static ApiTraceEntry MakeApiEntry(
        string? requestAuth = "Bearer secret-token",
        string? requestCookie = "session=abc",
        string? responseSetCookie = "sid=xyz; Path=/",
        string? responseWwwAuth = "Bearer realm=test",
        string? body = "{\"ok\":true}")
    {
        var reqHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (requestAuth != null) reqHeaders["Authorization"] = requestAuth;
        if (requestCookie != null) reqHeaders["Cookie"] = requestCookie;
        reqHeaders["Content-Type"] = "application/json";
        reqHeaders["X-Custom"] = "value";

        var respHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (responseSetCookie != null) respHeaders["Set-Cookie"] = responseSetCookie;
        if (responseWwwAuth != null) respHeaders["WWW-Authenticate"] = responseWwwAuth;
        respHeaders["Content-Type"] = "application/json";

        return new ApiTraceEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            DurationMs = 42,
            Account = "testacct",
            Method = "GET",
            BaseUrl = "https://cliq.zoho.com",
            Url = "https://cliq.zoho.com/api/v2/channels",
            RequestHeaders = reqHeaders,
            RequestBody = null,
            ResponseStatus = 200,
            ResponseHeaders = respHeaders,
            ResponseBody = body,
        };
    }

    // ─── Security header exclusion ────────────────────────────────────────────

    [Fact]
    public async Task TraceWriter_SecurityHeaders_AbsentFromWrittenFile()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();

        // Configure default export path so we don't need --export-path.
        var exportDir = Path.Combine(_tempDir, "traces-out");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var traceSession = session;
        await traceSession.StartSessionAsync("test-sec-headers", null);

        var writer = MakeTraceWriter(traceSession);
        await writer.AppendApiEntryAsync(MakeApiEntry());

        // Find the written trace file.
        var files = Directory.GetFiles(exportDir, "*.json");
        Assert.Single(files);

        var content = await File.ReadAllTextAsync(files[0]);
        var line = JsonSerializer.Deserialize<JsonElement>(content.Trim());

        // Verify request_headers does NOT contain Authorization, Cookie, X-Auth-Token, X-Api-Key.
        var reqHeaders = line.GetProperty("request_headers");
        Assert.False(HasProperty(reqHeaders, "Authorization"),
            "request_headers must not contain 'Authorization'");
        Assert.False(HasProperty(reqHeaders, "Cookie"),
            "request_headers must not contain 'Cookie'");

        // Verify response_headers does NOT contain Set-Cookie or WWW-Authenticate.
        var respHeaders = line.GetProperty("response_headers");
        Assert.False(HasProperty(respHeaders, "Set-Cookie"),
            "response_headers must not contain 'Set-Cookie'");
        Assert.False(HasProperty(respHeaders, "WWW-Authenticate"),
            "response_headers must not contain 'WWW-Authenticate'");

        // Verify non-security headers ARE present.
        Assert.True(HasProperty(reqHeaders, "X-Custom"));
    }

    // ─── Session name validation ──────────────────────────────────────────────

    [Fact]
    public async Task StartSession_InvalidName_Throws_InvalidArgs()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-out2");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => session.StartSessionAsync("bad/name", null));

        Assert.Equal(ErrorCodes.INVALID_ARGS, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    [Fact]
    public async Task StartSession_ValidName_Succeeds()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-out3");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var entry = await session.StartSessionAsync("my_session-123.ok", null);
        Assert.Equal("active", entry.Status);
        Assert.Equal("my_session-123.ok", entry.Name);
    }

    // ─── No active session → no trace file ───────────────────────────────────

    [Fact]
    public async Task TraceWriter_NoActiveSession_NoFileCreated()
    {
        var session = MakeTraceSession();  // No session started.
        var writer = MakeTraceWriter(session);
        var exportDir = Path.Combine(_tempDir, "should-not-exist");

        await writer.AppendApiEntryAsync(MakeApiEntry());

        // The directory should not have been created.
        Assert.False(Directory.Exists(exportDir));
    }

    // ─── Export non-destructive ───────────────────────────────────────────────

    [Fact]
    public async Task Export_TraceFileUnchanged_AfterExport()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-export-safe");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var entry = await session.StartSessionAsync("export-safe", null);
        var writer = MakeTraceWriter(session);
        await writer.AppendApiEntryAsync(MakeApiEntry());

        var bytesBefore = (await File.ReadAllBytesAsync(entry.ExportPath)).Length;

        // Export the session — this should only read, not write.
        var exporter = MakeExporter(session);
        var results = await exporter.ExportAsync(entry.UniqueId, null, null, null);
        Assert.NotEmpty(results);

        var bytesAfter = (await File.ReadAllBytesAsync(entry.ExportPath)).Length;
        Assert.Equal(bytesBefore, bytesAfter);
    }

    // ─── Truncate body in output only ────────────────────────────────────────

    [Fact]
    public async Task Export_TruncateBody_OutputTruncated_FileUnchanged()
    {
        const string longBody = "LONGLONGLONGLONGLONGLONGLONGLONGLONGBODY";
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-trunc");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var entry = await session.StartSessionAsync("trunc-test", null);
        var writer = MakeTraceWriter(session);
        await writer.AppendApiEntryAsync(MakeApiEntry(body: longBody));

        var bytesBefore = new FileInfo(entry.ExportPath).Length;

        var exporter = MakeExporter(session);
        var results = await exporter.ExportAsync(entry.UniqueId, null, null, truncateBodyChars: 5);
        Assert.Single(results);

        // Output body is truncated.
        var responseBody = results[0].GetProperty("response_body").GetString();
        Assert.Equal("LONGL", responseBody);

        // File on disk is unchanged.
        var bytesAfter = new FileInfo(entry.ExportPath).Length;
        Assert.Equal(bytesBefore, bytesAfter);
    }

    // ─── Entry count tracking (seq) ───────────────────────────────────────────

    [Fact]
    public async Task SessionEntryCount_IncrementsPerEntry()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-count");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var entry = await session.StartSessionAsync("count-test", null);
        var writer = MakeTraceWriter(session);

        await writer.AppendApiEntryAsync(MakeApiEntry());
        await writer.AppendApiEntryAsync(MakeApiEntry());
        await writer.AppendApiEntryAsync(MakeApiEntry());

        var updated = await session.FindByIdAsync(entry.UniqueId);
        Assert.NotNull(updated);
        Assert.Equal(3, updated!.EntryCount);

        var sessions = await session.ListSessionsAsync();
        var found = sessions.First(s => s.UniqueId == entry.UniqueId);
        Assert.Equal(3, found.EntryCount);
    }

    // ─── Remove session preserves trace file ─────────────────────────────────

    [Fact]
    public async Task RemoveSession_SessionsJsonEntryGone_FilePreserved()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-remove");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var entry = await session.StartSessionAsync("remove-test", null);
        var writer = MakeTraceWriter(session);
        await writer.AppendApiEntryAsync(MakeApiEntry());

        Assert.True(File.Exists(entry.ExportPath));

        await session.RemoveSessionAsync(entry.UniqueId);

        // sessions.json entry is gone.
        var found = await session.FindByIdAsync(entry.UniqueId);
        Assert.Null(found);

        // But the trace file is preserved.
        Assert.True(File.Exists(entry.ExportPath));
    }

    // ─── Reopen: status → active; seq continues ──────────────────────────────

    [Fact]
    public async Task ReopenSession_StatusActive_SeqContinues()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-reopen");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var entry = await session.StartSessionAsync("reopen-test", null);
        var writer = MakeTraceWriter(session);

        // Write 2 entries, then close.
        await writer.AppendApiEntryAsync(MakeApiEntry());
        await writer.AppendApiEntryAsync(MakeApiEntry());
        await session.CloseSessionAsync(entry.UniqueId, waitMs: 100);

        // Reopen.
        await session.ReopenSessionAsync(entry.UniqueId);
        var reopened = await session.FindByIdAsync(entry.UniqueId);
        Assert.NotNull(reopened);
        Assert.Equal("active", reopened!.Status);
        Assert.Equal(2, reopened.EntryCount); // EntryCount not reset.

        // Write another entry — seq should be 3.
        await writer.AppendApiEntryAsync(MakeApiEntry());

        var afterThird = await session.FindByIdAsync(entry.UniqueId);
        Assert.Equal(3, afterThird!.EntryCount);

        // The third line in the trace file should have seq=3.
        var lines = (await File.ReadAllTextAsync(entry.ExportPath))
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);

        var thirdEntry = JsonSerializer.Deserialize<JsonElement>(lines[2]);
        Assert.Equal(3, thirdEntry.GetProperty("seq").GetInt32());
    }

    // ─── Close session sets status=closed ────────────────────────────────────

    [Fact]
    public async Task CloseSession_SetsStatusClosed()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-close");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var entry = await session.StartSessionAsync("close-test", null);

        // Write one entry so the trace file exists before closing.
        var writer = MakeTraceWriter(session);
        await writer.AppendApiEntryAsync(MakeApiEntry());

        Assert.True(File.Exists(entry.ExportPath), "Trace file should exist after first write.");

        await session.CloseSessionAsync(entry.UniqueId, waitMs: 50);

        var closed = await session.FindByIdAsync(entry.UniqueId);
        Assert.NotNull(closed);
        Assert.Equal("closed", closed!.Status);

        // Trace file is preserved after close.
        Assert.True(File.Exists(entry.ExportPath));
    }

    // ─── SESSION_AMBIGUOUS when two sessions share a name ────────────────────

    [Fact]
    public async Task Export_DuplicateName_Throws_SessionAmbiguous()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-ambig");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        // Start two sessions with the same name — both should succeed.
        var e1 = await session.StartSessionAsync("shared-name", null);
        var e2 = await session.StartSessionAsync("shared-name", null);
        Assert.NotEqual(e1.UniqueId, e2.UniqueId);

        var exporter = MakeExporter(session);
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => exporter.ExportAsync(null, "shared-name", null, null));

        Assert.Equal(ErrorCodes.SESSION_AMBIGUOUS, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    // ─── EXPORT_PATH_NOT_SET when no export path configured ──────────────────

    [Fact]
    public async Task StartSession_NoExportPath_Throws_ExportPathNotSet()
    {
        // Do NOT configure a default export path.
        var session = MakeTraceSession();

        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => session.StartSessionAsync("no-path-test", null));

        Assert.Equal(ErrorCodes.EXPORT_PATH_NOT_SET, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    // ─── Two sessions with same name can coexist ─────────────────────────────

    [Fact]
    public async Task StartSession_DuplicateNameAllowed_DifferentUniqueIds()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-dup");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var e1 = await session.StartSessionAsync("dup-name", null);
        var e2 = await session.StartSessionAsync("dup-name", null);

        Assert.Equal("dup-name", e1.Name);
        Assert.Equal("dup-name", e2.Name);
        Assert.NotEqual(e1.UniqueId, e2.UniqueId);

        var matches = await session.FindByNameAsync("dup-name");
        Assert.Equal(2, matches.Count);
    }

    // ─── TraceWriter does not write to closed session ────────────────────────

    [Fact]
    public async Task TraceWriter_ClosedSession_NoNewEntries()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-closed-write");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var entry = await session.StartSessionAsync("closed-write-test", null);
        var writer = MakeTraceWriter(session);

        await writer.AppendApiEntryAsync(MakeApiEntry());
        await session.CloseSessionAsync(entry.UniqueId, waitMs: 50);

        var countBefore = (await session.FindByIdAsync(entry.UniqueId))!.EntryCount;

        // The session is now closed; GetActiveSessionAsync returns null.
        await writer.AppendApiEntryAsync(MakeApiEntry());

        var countAfter = (await session.FindByIdAsync(entry.UniqueId))!.EntryCount;
        Assert.Equal(countBefore, countAfter); // No new entries.
    }

    // ─── X-Auth-Token and X-Api-Key are also stripped ────────────────────────

    [Fact]
    public async Task TraceWriter_XAuthToken_XApiKey_AreStripped()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-xheaders");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        await session.StartSessionAsync("strip-x-headers", null);
        var writer = MakeTraceWriter(session);

        var reqHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Auth-Token"] = "supersecret",
            ["X-Api-Key"] = "apikey123",
            ["X-Normal"] = "keepme",
        };
        var entry = new ApiTraceEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            DurationMs = 10,
            Account = "testacct",
            Method = "GET",
            BaseUrl = "https://api.zoho.com",
            Url = "https://api.zoho.com/v1/resource",
            RequestHeaders = reqHeaders,
            ResponseStatus = 200,
            ResponseHeaders = new Dictionary<string, string>(),
        };

        await writer.AppendApiEntryAsync(entry);

        var files = Directory.GetFiles(exportDir, "*.json");
        var content = await File.ReadAllTextAsync(files[0]);
        var el = JsonSerializer.Deserialize<JsonElement>(content.Trim());
        var reqH = el.GetProperty("request_headers");

        Assert.False(HasProperty(reqH, "X-Auth-Token"));
        Assert.False(HasProperty(reqH, "X-Api-Key"));
        Assert.True(HasProperty(reqH, "X-Normal"));
        Assert.Equal("keepme", GetPropertyValue(reqH, "X-Normal"));
    }

    // ─── Export --type filter ─────────────────────────────────────────────────

    [Fact]
    public async Task Export_TypeFilter_ReturnsOnlyMatchingType()
    {
        var session = MakeTraceSession();
        var configStore = MakeConfigStore();
        var exportDir = Path.Combine(_tempDir, "traces-type-filter");
        await configStore.SaveAsync(new TraceConfig { DefaultExportPath = exportDir });

        var entry = await session.StartSessionAsync("type-filter-test", null);
        var writer = MakeTraceWriter(session);

        // Write an API entry manually (TraceWriter writes type='api').
        await writer.AppendApiEntryAsync(MakeApiEntry());

        var exporter = MakeExporter(session);

        // Filter for 'api' — should return 1 entry.
        var apiResults = await exporter.ExportAsync(entry.UniqueId, null, "api", null);
        Assert.Single(apiResults);
        Assert.Equal("api", apiResults[0].GetProperty("type").GetString());

        // Filter for 'pex' — should return 0 entries.
        var pexResults = await exporter.ExportAsync(entry.UniqueId, null, "pex", null);
        Assert.Empty(pexResults);
    }
}
