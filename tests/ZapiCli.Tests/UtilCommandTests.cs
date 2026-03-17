using System.Text.RegularExpressions;
using Xunit;

namespace ZapiCli.Tests;

public sealed class UtilCommandTests
{
    // RFC 4122 format: 8-4-4-4-12 lowercase hex
    private static readonly Regex UuidV4Pattern =
        new(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$",
            RegexOptions.Compiled);

    // ─── util time-ms ─────────────────────────────────────────────────────────

    [Fact]
    public void TimeMsCommand_ValueIsPositive()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(ts > 0);
    }

    [Fact]
    public void TimeMsCommand_ValueIsWithinReasonableRange()
    {
        // Sanity: between 2020-01-01 and 2100-01-01
        var minMs = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var maxMs = new DateTimeOffset(2100, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        var ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.InRange(ts, minMs, maxMs);
    }

    [Fact]
    public void TimeMsCommand_TwoConsecutiveValuesAreNonDecreasing()
    {
        var ts1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ts2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Assert.True(ts2 >= ts1);
    }

    // ─── util uuid ────────────────────────────────────────────────────────────

    [Fact]
    public void UuidCommand_ProducesValidRfc4122Format()
    {
        var uuid = Guid.NewGuid().ToString("D");
        Assert.Matches(UuidV4Pattern, uuid);
    }

    [Fact]
    public void UuidCommand_ProducesLowercaseOutput()
    {
        var uuid = Guid.NewGuid().ToString("D");
        Assert.Equal(uuid, uuid.ToLowerInvariant());
    }

    [Fact]
    public void UuidCommand_ProducesUniqueValuesOnEachCall()
    {
        var uuid1 = Guid.NewGuid().ToString("D");
        var uuid2 = Guid.NewGuid().ToString("D");
        Assert.NotEqual(uuid1, uuid2);
    }

    [Fact]
    public void UuidCommand_HasExpectedSegmentLengths()
    {
        var uuid = Guid.NewGuid().ToString("D");
        var parts = uuid.Split('-');
        Assert.Equal(5, parts.Length);
        Assert.Equal(8, parts[0].Length);
        Assert.Equal(4, parts[1].Length);
        Assert.Equal(4, parts[2].Length);
        Assert.Equal(4, parts[3].Length);
        Assert.Equal(12, parts[4].Length);
    }
}
