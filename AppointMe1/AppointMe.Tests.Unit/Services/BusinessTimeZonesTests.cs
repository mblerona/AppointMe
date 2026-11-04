using AppointMe.Domain.TimeZones;
using FluentAssertions;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

public class BusinessTimeZonesTests
{
    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }

    [Fact]
    public void Default_IsSkopje_AndComesFirstInTheList()
    {
        BusinessTimeZones.Default.Should().Be("Europe/Skopje");
        BusinessTimeZones.Supported[0].Should().Be("Europe/Skopje");
        BusinessTimeZones.Supported.Should().OnlyHaveUniqueItems();
    }

    public static IEnumerable<object[]> AllZones() => BusinessTimeZones.Supported.Select(z => new object[] { z });

    [Theory]
    [MemberData(nameof(AllZones))]
    public void EverySupportedZone_ReallyExistsOnThisMachine(string id)
    {
        // Find falls back silently, so ask directly. Otherwise a typo would quietly become Skopje.
        var act = () => TimeZoneInfo.FindSystemTimeZoneById(id);

        act.Should().NotThrow();
        id.Length.Should().BeLessThanOrEqualTo(BusinessTimeZones.MaxIdLength);
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("")]
    [InlineData(null)]
    public void Find_WithAnUnknownId_FallsBackToTheDefault(string? id)
    {
        var skopje = TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje");

        BusinessTimeZones.Find(id).BaseUtcOffset.Should().Be(skopje.BaseUtcOffset);
        BusinessTimeZones.Find(id).Id.Should().Be(skopje.Id);
    }

    [Theory]
    [InlineData("Europe/Skopje", 2026, 12, 1, 21, 0)]    // winter, UTC+1
    [InlineData("Asia/Tokyo", 2026, 12, 2, 5, 0)]        // the next day
    [InlineData("America/Los_Angeles", 2026, 12, 1, 12, 0)]
    [InlineData("UTC", 2026, 12, 1, 20, 0)]
    public void Now_IsTheBusinessesClock(string id, int year, int month, int day, int hour, int minute)
    {
        var clock = new FixedClock(new DateTime(2026, 12, 1, 20, 0, 0));

        BusinessTimeZones.Now(clock, id).Should().Be(new DateTime(year, month, day, hour, minute, 0));
    }

    [Fact]
    public void Now_FollowsDaylightSaving()
    {
        var summer = new FixedClock(new DateTime(2026, 7, 1, 20, 0, 0));

        BusinessTimeZones.Now(summer, "Europe/Skopje").Should().Be(new DateTime(2026, 7, 1, 22, 0, 0));
    }

    [Theory]
    [InlineData("Europe/Skopje", 2026, 12, 1, 10, 2026, 12, 1, 9)]       // UTC+1 in winter
    [InlineData("Europe/Skopje", 2026, 7, 1, 10, 2026, 7, 1, 8)]         // UTC+2 in summer
    [InlineData("Asia/Tokyo", 2026, 7, 1, 8, 2026, 6, 30, 23)]           // the previous day in UTC
    [InlineData("America/New_York", 2026, 7, 1, 22, 2026, 7, 2, 2)]      // the next day in UTC
    public void ToUtc_ConvertsClockTimes(string id, int y, int m, int d, int h, int uy, int um, int ud, int uh)
    {
        var result = BusinessTimeZones.ToUtc(new DateTime(y, m, d, h, 0, 0), id);

        result.Should().Be(new DateTime(uy, um, ud, uh, 0, 0));
        result.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void ToUtc_ATimeSkippedWhenClocksGoForward_MovesForwardInsteadOfThrowing()
    {
        // On 29 March 2026 clocks in Skopje jump from 02:00 to 03:00, so 02:30 doesn't exist.
        var skipped = new DateTime(2026, 3, 29, 2, 30, 0);
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje").IsInvalidTime(skipped).Should().BeTrue();

        BusinessTimeZones.ToUtc(skipped, "Europe/Skopje").Should().Be(new DateTime(2026, 3, 29, 1, 30, 0), "03:30 at UTC+2");
    }

    [Fact]
    public void ToUtc_ATimeRepeatedWhenClocksGoBack_IsTheLaterOne()
    {
        // On 25 October 2026 clocks in Skopje go from 03:00 back to 02:00, so 02:30 happens twice.
        var repeated = new DateTime(2026, 10, 25, 2, 30, 0);
        TimeZoneInfo.FindSystemTimeZoneById("Europe/Skopje").IsAmbiguousTime(repeated).Should().BeTrue();

        BusinessTimeZones.ToUtc(repeated, "Europe/Skopje").Should().Be(new DateTime(2026, 10, 25, 1, 30, 0), "02:30 at UTC+1");
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc)]
    public void ToUtc_AlwaysTreatsTheValueAsAClockTime(DateTimeKind kind)
    {
        var value = DateTime.SpecifyKind(new DateTime(2026, 12, 1, 10, 0, 0), kind);

        BusinessTimeZones.ToUtc(value, "Asia/Tokyo").Should().Be(new DateTime(2026, 12, 1, 1, 0, 0));
    }

    [Fact]
    public void DisplayName_ShowsTheIdAndOffset()
    {
        BusinessTimeZones.DisplayName("Europe/Skopje").Should().Be("Europe/Skopje (UTC+01:00)");
        BusinessTimeZones.DisplayName("America/New_York").Should().Be("America/New_York (UTC-05:00)");
        BusinessTimeZones.DisplayName("Asia/Kolkata").Should().Be("Asia/Kolkata (UTC+05:30)");
    }
}
