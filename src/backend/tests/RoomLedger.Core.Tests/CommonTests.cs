using RoomLedger.Core.Common;

namespace RoomLedger.Core.Tests;

/**
 * Test tiện ích dùng chung: UUID v7, kỳ YYYY-MM.
 *
 * @author Tuấn
 * @version 1.0
 */
public class CommonTests
{
    [Fact]
    public void UuidV7_IsVersion7AndSortsByTime()
    {
        var earlier = UuidV7.NewGuid(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var later = UuidV7.NewGuid(new DateTimeOffset(2026, 1, 1, 0, 0, 1, TimeSpan.Zero));
        Assert.Equal('7', earlier.ToString()[14]);
        Assert.True(string.CompareOrdinal(earlier.ToString(), later.ToString()) < 0);
    }

    [Fact]
    public void Period_ParsesYearMonth() => Assert.Equal("2026-11", Period.Parse("2026-11"));

    [Theory]
    [InlineData("2026-13")]
    [InlineData("11/2026")]
    [InlineData("2026-1")]
    [InlineData(null)]
    public void Period_RejectsInvalid(string? input) => Assert.Throws<ValidationException>(() => Period.Parse(input));
}
