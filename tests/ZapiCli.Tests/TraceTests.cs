using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZapiCli.Core;
using ZapiCli.Core.Accounts;
using ZapiCli.Core.Api;
using ZapiCli.Core.Auth;
using ZapiCli.Core.Trace;
using ZapiCli.Tests.Fakes;

namespace ZapiCli.Tests;

/// <summary>
/// Unit tests for the trace infrastructure (Story 07 acceptance criteria).
/// </summary>
public sealed class TraceTests : IDisposable
{
    // Each test uses an isolated temp directory so tests do not interfere with each other.
    private readonly string _tempDir;

    public TraceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"zapi-trace-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private TraceSession BuildSession() =>
        new(NullLogger<TraceSession>.Instance, _tempDir);

    private TraceWriter BuildWriter(TraceSession? session = null) =>
        new(NullLogger<TraceWriter>.Instance, session ?? BuildSession());

    private TraceExporter BuildExporter(TraceSession? session = null) =>
        new(session ?? BuildSession());

    private static ApiTraceEntry SampleEntry(Dictionary<string, string>? headers = null) =>
        new()
        {
            Timestamp = DateTimeOffset.UtcNow,
            DurationMs = 42,
            Account = "test-account",
            Method = "GET",
            BaseUrl = "https://cliq.zoho.com/api/v2",
            Url = "https://cliq.zoho.com/api/v2/channels",
            RequestHeaders = headers ?? new Dictionary<string, string>(),
            ResponseStatus = 200,
            ResponseBody = "{}",
        };

    private static ApiClient BuildApiClient(
        FakeHttpMessageHandler? handler = null,
        FakeAccountStore? store = null,
        TraceWriter? traceWriter = null,
        string accountName = "test-account")
    {
        var kc = new InMemoryKeychainProvider();
        kc.SetAsync($"zapi-cli:{accountName}:pat", "tok").Wait();
        var auth = new PatAuthProvider(kc);
        var storeToUse = store ?? new FakeAccountStore(new AccountsRoot
        {
            Accounts =
            [
                new AccountEntry
                {
                    Name = accountName,
                    TokenType = "pat",
                    Email = "user@zoho.com",
                    IsDefault = true,
                }
            ],
        });
        return new ApiClient(
            new HttpClient(handler ?? new FakeHttpMessageHandler()),
            auth,
            storeToUse,
            NullLogger<ApiClient>.Instance,
            traceWriter);
    }

    // ─── AC: Authorization header is excluded ─────────────────────────────────

    [Fact]
    public async Task TraceWriter_ExcludesAuthorizationHeader()
    {
        var ts = BuildSession();
        var tw = BuildWriter(ts);
        await ts.StartSessionAsync("auth-test");

        var entry = SampleEntry(new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json",
            ["Authorization"] = "Zoho-oauthtoken super-secret-token",
            ["X-Custom"] = "some-value",
        });

        await tw.AppendApiEntryAsync(entry);

        var traceFile = Path.Combine(_tempDir, "traces", "auth-test", "trace.jsonl");
        Assert.True(File.Exists(traceFile));
        var line = (await File.ReadAllLinesAsync(traceFile)).First(l => !string.IsNullOrWhiteSpace(l));

        using var doc = JsonDocument.Parse(line);
        var reqHeaders = doc.RootElement.GetProperty("request_headers");

        // Authorization must be absent from the JSONL entry.
        Assert.False(reqHeaders.TryGetProperty("Authorization", out _));
        Assert.False(reqHeaders.TryGetProperty("authorization", out _));

        // Other headers must be preserved.
        Assert.True(reqHeaders.TryGetProperty("Content-Type", out _));
        Assert.True(reqHeaders.TryGetProperty("X-Custom", out _));
    }

    [Fact]
    public async Task TraceWriter_ExcludesLowercaseAuthorizationHeader()
    {
        var ts = BuildSession();
        var tw = BuildWriter(ts);
        await ts.StartSessionAsync("auth-lower-test");

        var entry = SampleEntry(new Dictionary<string, string>
        {
            ["authorization"] = "Bearer some-token",
        });

        await tw.AppendApiEntryAsync(entry);

        var traceFile = Path.Combine(_tempDir, "traces", "auth-lower-test", "trace.jsonl");
        var line = (await File.ReadAllLinesAsync(traceFile)).First(l => !string.IsNullOrWhiteSpace(l));
        using var doc = JsonDocument.Parse(line);
        var reqHeaders = doc.RootElement.GetProperty("request_headers");
        Assert.False(reqHeaders.TryGetProperty("authorization", out _));
    }

    // ─── AC: Invalid session name ──────────────────────────────────────────────

    [Theory]
    [InlineData("bad/name")]
    [InlineData("bad\\name")]
    [InlineData("bad name")]
    [InlineData("")]
    [InlineData("toolongname-000000000000000000000000000000000000000000000000000000000")] // 65 chars
    public async Task StartSession_InvalidName_ThrowsInvalidArgs(string name)
    {
        var ts = BuildSession();
        var ex = await Assert.ThrowsAsync<ZapiCliException>(() => ts.StartSessionAsync(name));
        Assert.Equal(ErrorCodes.InvalidArgs, ex.Code);
        Assert.Equal(1, ex.ExitCode);
    }

    // ─── AC: No active session → api call succeeds, no trace file created ────

    [Fact]
    public async Task ApiCall_NoActiveSession_SucceedsWithoutTraceFile()
    {
        var ts = BuildSession();
        var tw = BuildWriter(ts);
        var client = BuildApiClient(traceWriter: tw);

        var response = await client.CallAsync(new ApiRequest
        {
            BaseUrl = "https://cliq.zoho.com/api/v2",
            Method = "GET",
            Path = "/channels",
            AccountName = "test-account",
        });

        Assert.Equal(200, response.StatusCode);

        // No traces directory should contain any session sub-directory.
        var tracesDir = Path.Combine(_tempDir, "traces");
        Assert.False(Directory.Exists(tracesDir) &&
                     Directory.EnumerateDirectories(tracesDir).Any());
    }

    // ─── AC: api call with active session appends exactly one entry ──────────

    [Fact]
    public async Task ApiCall_WithActiveSession_AppendsOneEntry()
    {
        var ts = BuildSession();
        var tw = BuildWriter(ts);
        await ts.StartSessionAsync("call-test");

        var client = BuildApiClient(traceWriter: tw);
        await client.CallAsync(new ApiRequest
        {
            BaseUrl = "https://cliq.zoho.com/api/v2",
            Method = "GET",
            Path = "/channels",
            AccountName = "test-account",
        });

        var traceFile = Path.Combine(_tempDir, "traces", "call-test", "trace.jsonl");
        var lines = (await File.ReadAllLinesAsync(traceFile))
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();

        Assert.Single(lines);

        using var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("api", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal("call-test", doc.RootElement.GetProperty("session").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("seq").GetInt32());
        Assert.Equal("GET", doc.RootElement.GetProperty("method").GetString());
        // base_url must be recorded (ADR-0003).
        Assert.Equal("https://cliq.zoho.com/api/v2",
            doc.RootElement.GetProperty("base_url").GetString());
    }

    // ─── AC: Export is non-destructive ────────────────────────────────────────

    [Fact]
    public async Task Export_IsNonDestructive()
    {
        var ts = BuildSession();
        var tw = BuildWriter(ts);
        var ex = BuildExporter(ts);

        await ts.StartSessionAsync("export-test");
        await tw.AppendApiEntryAsync(SampleEntry());

        var traceFile = Path.Combine(_tempDir, "traces", "export-test", "trace.jsonl");
        var before = await File.ReadAllTextAsync(traceFile);

        // Export twice.
        var entries1 = await ex.ExportAsync("export-test");
        var entries2 = await ex.ExportAsync("export-test");

        var after = await File.ReadAllTextAsync(traceFile);

        Assert.Equal(before, after);  // file unchanged
        Assert.Single(entries1);
        Assert.Single(entries2);
    }

    // ─── AC: --truncate-body truncates output but not JSONL ──────────────────

    [Fact]
    public async Task Export_TruncateBody_TruncatesOutputNotJSONL()
    {
        var ts = BuildSession();
        var tw = BuildWriter(ts);
        var exporter = BuildExporter(ts);

        await ts.StartSessionAsync("trunc-test");

        var longBody = new string('x', 500);
        var entry = SampleEntry() with { ResponseBody = longBody, RequestBody = longBody };
        await tw.AppendApiEntryAsync(entry);

        var traceFile = Path.Combine(_tempDir, "traces", "trunc-test", "trace.jsonl");
        var rawLine = (await File.ReadAllLinesAsync(traceFile))
            .First(l => !string.IsNullOrWhiteSpace(l));

        // JSONL must contain the full body.
        Assert.Contains(new string('x', 500), rawLine);

        // Export output must be truncated to 100 bytes.
        var exported = await exporter.ExportAsync("trunc-test", truncateBodyBytes: 100);
        Assert.Single(exported);
        var responseBodyBytes = System.Text.Encoding.UTF8.GetByteCount(exported[0].ResponseBody ?? "");
        Assert.Equal(100, responseBodyBytes);

        // JSONL remains unchanged after the export.
        var afterLine = (await File.ReadAllLinesAsync(traceFile))
            .First(l => !string.IsNullOrWhiteSpace(l));
        Assert.Equal(rawLine, afterLine);
    }

    // ─── AC: SESSION_NOT_FOUND for export / close / remove ───────────────────

    [Fact]
    public async Task Export_MissingSession_ThrowsSessionNotFound()
    {
        var exporter = BuildExporter();
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => exporter.ExportAsync("no-such-session"));
        Assert.Equal(ErrorCodes.SessionNotFound, ex.Code);
    }

    [Fact]
    public async Task Close_MissingSession_ThrowsSessionNotFound()
    {
        var ts = BuildSession();
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => ts.CloseSessionAsync("no-such-session"));
        Assert.Equal(ErrorCodes.SessionNotFound, ex.Code);
    }

    [Fact]
    public async Task Remove_MissingSession_ThrowsSessionNotFound()
    {
        var ts = BuildSession();
        var ex = await Assert.ThrowsAsync<ZapiCliException>(
            () => ts.RemoveSessionAsync("no-such-session"));
        Assert.Equal(ErrorCodes.SessionNotFound, ex.Code);
    }

    // ─── AC: Close marks status closed; JSONL preserved ──────────────────────

    [Fact]
    public async Task Close_MarksStatusClosed_JsonlPreserved()
    {
        var ts = BuildSession();
        var tw = BuildWriter(ts);
        await ts.StartSessionAsync("close-test");
        await tw.AppendApiEntryAsync(SampleEntry());

        var traceFile = Path.Combine(_tempDir, "traces", "close-test", "trace.jsonl");
        var before = await File.ReadAllTextAsync(traceFile);

        var closed = await ts.CloseSessionAsync("close-test");

        Assert.Equal("closed", closed.Status);
        Assert.Equal(before, await File.ReadAllTextAsync(traceFile));
    }

    // ─── AC: Remove deletes entry + trace directory ───────────────────────────

    [Fact]
    public async Task Remove_DeletesEntryAndDirectory()
    {
        var ts = BuildSession();
        await ts.StartSessionAsync("remove-test");

        var sessionDir = Path.Combine(_tempDir, "traces", "remove-test");
        Assert.True(Directory.Exists(sessionDir));

        await ts.RemoveSessionAsync("remove-test");

        Assert.False(Directory.Exists(sessionDir));
        var sessions = await ts.ListSessionsAsync();
        Assert.DoesNotContain(sessions, s => s.Name == "remove-test");
    }

    // ─── AC: Starting new session replaces active pointer; old data preserved ─

    [Fact]
    public async Task StartSession_WhenOneActive_ReplacesActivePointer_OldDataPreserved()
    {
        var ts = BuildSession();
        var tw = BuildWriter(ts);

        await ts.StartSessionAsync("session-a");
        await tw.AppendApiEntryAsync(SampleEntry());

        await ts.StartSessionAsync("session-b");

        // Active session is now B.
        var active = await ts.GetActiveSessionAsync();
        Assert.Equal("session-b", active?.Name);

        // Session A's JSONL is still on disk.
        var sessionAFile = Path.Combine(_tempDir, "traces", "session-a", "trace.jsonl");
        Assert.True(File.Exists(sessionAFile));
        var lines = (await File.ReadAllLinesAsync(sessionAFile))
            .Where(l => !string.IsNullOrWhiteSpace(l));
        Assert.Single(lines); // one entry written before switching
    }

    // ─── AC: TraceWriter failure does NOT fail the api call ──────────────────

    [Fact]
    public async Task TraceWriter_WriteFailure_DoesNotFailApiCall()
    {
        // Point the session at a read-only location to simulate write failure.
        // On macOS/Linux we can create a session directory, then remove write permission.
        var ts = BuildSession();
        var tw = BuildWriter(ts);
        await ts.StartSessionAsync("fail-test");

        var sessionDir = Path.Combine(_tempDir, "traces", "fail-test");
        var traceFile = Path.Combine(sessionDir, "trace.jsonl");

        // Make the JSONL file read-only so AppendAllTextAsync fails.
        File.SetAttributes(traceFile, FileAttributes.ReadOnly);

        try
        {
            var client = BuildApiClient(traceWriter: tw);
            // The api call must succeed even though trace writing fails.
            var result = await client.CallAsync(new ApiRequest
            {
                BaseUrl = "https://cliq.zoho.com/api/v2",
                Method = "GET",
                Path = "/channels",
                AccountName = "test-account",
            });
            Assert.Equal(200, result.StatusCode);
        }
        finally
        {
            // Restore permissions for cleanup.
            File.SetAttributes(traceFile, FileAttributes.Normal);
        }
    }

    // ─── AC: type filter ─────────────────────────────────────────────────────

    [Fact]
    public async Task Export_TypeFilter_FiltersEntries()
    {
        var ts = BuildSession();
        var tw = BuildWriter(ts);
        var exporter = BuildExporter(ts);

        await ts.StartSessionAsync("filter-test");

        // Write two entries: one normal, one with type overridden to 'pex'.
        await tw.AppendApiEntryAsync(SampleEntry()); // type = "api"
        await tw.AppendApiEntryAsync(SampleEntry() with { Type = "pex" });

        // Filter: only api
        var apiOnly = await exporter.ExportAsync("filter-test", typeFilter: "api");
        Assert.Single(apiOnly);
        Assert.Equal("api", apiOnly[0].Type);

        // Filter: only pex
        var pexOnly = await exporter.ExportAsync("filter-test", typeFilter: "pex");
        Assert.Single(pexOnly);
        Assert.Equal("pex", pexOnly[0].Type);

        // No filter: both entries
        var all = await exporter.ExportAsync("filter-test");
        Assert.Equal(2, all.Count);
    }

    // ─── AC: ApiTraceEntry records both BaseUrl and Url ─────────────────────

    [Fact]
    public async Task ApiCall_TraceEntry_RecordsBaseUrlAndUrl()
    {
        var ts = BuildSession();
        var tw = BuildWriter(ts);
        var exporter = BuildExporter(ts);

        await ts.StartSessionAsync("url-test");

        var client = BuildApiClient(traceWriter: tw);
        await client.CallAsync(new ApiRequest
        {
            BaseUrl = "https://desk.zoho.com/api/v1",
            Method = "GET",
            Path = "/tickets",
            AccountName = "test-account",
        });

        var entries = await exporter.ExportAsync("url-test");
        Assert.Single(entries);
        Assert.Equal("https://desk.zoho.com/api/v1", entries[0].BaseUrl);
        Assert.StartsWith("https://desk.zoho.com/api/v1/tickets", entries[0].Url);
    }

    // ─── AC: trace session start creates sessions.json and empty trace.jsonl ─

    [Fact]
    public async Task StartSession_CreatesSessionsJsonAndEmptyTraceJsonl()
    {
        var ts = BuildSession();
        var entry = await ts.StartSessionAsync("init-test");

        Assert.Equal("init-test", entry.Name);
        Assert.Equal("active", entry.Status);

        var sessionsFile = Path.Combine(_tempDir, "traces", "sessions.json");
        Assert.True(File.Exists(sessionsFile));

        var traceFile = Path.Combine(_tempDir, "traces", "init-test", "trace.jsonl");
        Assert.True(File.Exists(traceFile));
        Assert.Empty((await File.ReadAllTextAsync(traceFile)).Trim());
    }

    // ─── AC: List sessions returns empty array without error ─────────────────

    [Fact]
    public async Task ListSessions_NoSessions_ReturnsEmptyList()
    {
        var ts = BuildSession();
        var sessions = await ts.ListSessionsAsync();
        Assert.Empty(sessions);
    }
}
