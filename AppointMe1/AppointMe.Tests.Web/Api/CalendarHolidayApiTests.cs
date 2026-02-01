using System.Net;
using System.Text.Json;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;   
using FluentAssertions;
using Xunit;

namespace AppointMe.Tests.Web.Api;

public class CalendarHolidaysApiTests : IClassFixture<HolidaysWebAppFactory>
{
    private readonly HolidaysWebAppFactory _factory;

    public CalendarHolidaysApiTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CalendarHolidays_WhenLoggedIn_ReturnsHolidayEventsJsonWithExpectedShape()
    {
        // Arrange
        var client = _factory.CreateClient(new()
        {
            AllowAutoRedirect = false
        });

        
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);

        var year = 2026;

        // Act
        var response = await client.GetAsync($"/Appointments/CalendarHolidays?year={year}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().BeGreaterThan(0);

        var first = doc.RootElement[0];

        first.TryGetProperty("start", out var start).Should().BeTrue();
        first.TryGetProperty("end", out var end).Should().BeTrue();
        first.TryGetProperty("allDay", out var allDay).Should().BeTrue();
        first.TryGetProperty("extendedProps", out var ext).Should().BeTrue();

        start.GetString().Should().Be($"{year}-01-01");
        end.GetString().Should().Be($"{year}-01-02");
        allDay.GetBoolean().Should().BeTrue();

        ext.TryGetProperty("localName", out var localName).Should().BeTrue();
        ext.TryGetProperty("holidayName", out var holidayName).Should().BeTrue();

        localName.GetString().Should().NotBeNullOrWhiteSpace();
        holidayName.GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task CalendarHolidays_WhenYearMissing_StillReturnsArray()
    {
        // Arrange
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);

        // Act
        var response = await client.GetAsync("/Appointments/CalendarHolidays");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
    }
}
