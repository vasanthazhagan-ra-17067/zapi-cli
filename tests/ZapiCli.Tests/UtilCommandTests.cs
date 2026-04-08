using System.Text.Json;
using ZapiCli.Commands;
using ZapiCli.Core;

namespace ZapiCli.Tests;

public sealed class UtilCommandTests
{
    // ── util timestamp ───────────────────────────────────────────────────────

    [Fact]
    public async Task UtilTimeMsCommand_WritesPositiveLong()
    {
        var writer = new InMemoryOutputWriter();
        var cmd = new UtilCommands.UtilTimestampCommand(writer);

        var beforeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var exitCode = await cmd.ExecuteAsync(null!, new UtilCommands.UtilTimestampSettings());
        var afterMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.Equal(0, exitCode);

        var json = writer.LastSuccessJson;
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("ts", out var tsProp));
        var ts = tsProp.GetInt64();
        Assert.True(ts > 0, "ts must be positive");
        Assert.True(ts >= beforeMs && ts <= afterMs,
            $"ts {ts} is not within [{beforeMs}, {afterMs}]");
    }

    [Fact]
    public async Task UtilTimeMsCommand_ValueIsWithin5SecondsOfNow()
    {
        var writer = new InMemoryOutputWriter();
        var cmd = new UtilCommands.UtilTimestampCommand(writer);

        await cmd.ExecuteAsync(null!, new UtilCommands.UtilTimestampSettings());

        using var doc = JsonDocument.Parse(writer.LastSuccessJson!);
        var ts = doc.RootElement.GetProperty("ts").GetInt64();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        Assert.True(Math.Abs(nowMs - ts) < 5_000, "ts must be within 5 seconds of UtcNow");
    }

    // ── util uuid ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UtilUuidCommand_WritesValidGuid()
    {
        var writer = new InMemoryOutputWriter();
        var cmd = new UtilCommands.UtilUuidCommand(writer);

        var exitCode = await cmd.ExecuteAsync(null!, new UtilCommands.UtilUuidSettings());

        Assert.Equal(0, exitCode);

        var json = writer.LastSuccessJson;
        Assert.NotNull(json);

        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("uuid", out var uuidProp));
        var uuidStr = uuidProp.GetString();
        Assert.False(string.IsNullOrEmpty(uuidStr));
        Assert.True(Guid.TryParse(uuidStr, out _), $"'{uuidStr}' is not a valid Guid");
    }

    [Fact]
    public async Task UtilUuidCommand_EachCallReturnsDifferentUuid()
    {
        var w1 = new InMemoryOutputWriter();
        var w2 = new InMemoryOutputWriter();

        await new UtilCommands.UtilUuidCommand(w1).ExecuteAsync(null!, new UtilCommands.UtilUuidSettings());
        await new UtilCommands.UtilUuidCommand(w2).ExecuteAsync(null!, new UtilCommands.UtilUuidSettings());

        using var d1 = JsonDocument.Parse(w1.LastSuccessJson!);
        using var d2 = JsonDocument.Parse(w2.LastSuccessJson!);

        var uuid1 = d1.RootElement.GetProperty("uuid").GetString();
        var uuid2 = d2.RootElement.GetProperty("uuid").GetString();

        Assert.NotEqual(uuid1, uuid2);
    }

    // ── util now ───────────────────────────────────────────────────────────────────────

    private static readonly TimeZoneInfo Ist =
        TimeZoneInfo.CreateCustomTimeZone("IST", TimeSpan.FromHours(5.5),
            "India Standard Time", "India Standard Time");

    [Fact]
    public async Task UtilTimeNowCommand_WritesNonEmptyNowField()
    {
        var writer = new InMemoryOutputWriter();
        var cmd = new UtilCommands.UtilNowCommand(writer);

        var exitCode = await cmd.ExecuteAsync(null!, new UtilCommands.UtilNowSettings());

        Assert.Equal(0, exitCode);
        var json = writer.LastSuccessJson;
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("now", out var nowProp));
        Assert.False(string.IsNullOrEmpty(nowProp.GetString()));
    }

    [Fact]
    public async Task UtilTimeNowCommand_FormatMatchesIndianPattern()
    {
        var writer = new InMemoryOutputWriter();
        var cmd = new UtilCommands.UtilNowCommand(writer);

        await cmd.ExecuteAsync(null!, new UtilCommands.UtilNowSettings());

        using var doc = JsonDocument.Parse(writer.LastSuccessJson!);
        var nowStr = doc.RootElement.GetProperty("now").GetString()!;

        // Expected pattern: DD/MM/YY HH:mm:ss.fff (24-hour with milliseconds)
        Assert.Matches(@"^\d{2}/\d{2}/\d{2} \d{2}:\d{2}:\d{2}\.\d{3}$", nowStr);
    }

    [Fact]
    public async Task UtilTimeNowCommand_TimeIsWithin2SecondsOfIstNow()
    {
        var before = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);
        var writer = new InMemoryOutputWriter();
        var cmd = new UtilCommands.UtilNowCommand(writer);

        await cmd.ExecuteAsync(null!, new UtilCommands.UtilNowSettings());

        var after = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Ist);

        using var doc = JsonDocument.Parse(writer.LastSuccessJson!);
        var nowStr = doc.RootElement.GetProperty("now").GetString()!;
        var parsed = DateTimeOffset.ParseExact(
            nowStr,
            "dd/MM/yy HH:mm:ss.fff",
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.True(parsed >= before.AddSeconds(-2) && parsed <= after.AddSeconds(2),
            $"Parsed time {parsed} is not within 2 seconds of IST now [{before}, {after}]");
    }
}
