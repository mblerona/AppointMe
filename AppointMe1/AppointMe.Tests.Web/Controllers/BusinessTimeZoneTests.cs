using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using AppointMe.Domain.TimeZones;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>Audit B5: the business's time zone, through the Settings page and the pages that use "now".</summary>
public class BusinessTimeZoneTests : IClassFixture<HolidaysWebAppFactory>
{
    private readonly HolidaysWebAppFactory _factory;

    public BusinessTimeZoneTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsync(Guid tenant, string email)
    {
        await TestData.EnsureTenantWithUserAsync(_factory, tenant, email);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(email, TestData.Password);
        return client;
    }

    private Task SetZoneAsync(Guid tenant, string zone) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            (await db.Businesses.FirstAsync(b => b.Id == tenant)).TimeZoneId = zone;
            return await db.SaveChangesAsync();
        });

    private Task<string> ZoneOfAsync(Guid tenant) =>
        TestData.WithDbAsync(_factory, db => db.Businesses.Where(b => b.Id == tenant).Select(b => b.TimeZoneId).FirstAsync());

    private static Dictionary<string, string> SettingsForm(Guid tenant, string? zone)
    {
        var form = new Dictionary<string, string>
        {
            ["Id"] = tenant.ToString(),
            ["Name"] = "Zone test",
            ["DefaultSlotMinutes"] = "30",
            ["WorkDayStart"] = "09:00",
            ["WorkDayEnd"] = "17:00",
            ["EnableServices"] = "true",
            ["OpenMon"] = "true"
        };
        if (zone != null) form["TimeZoneId"] = zone;
        return form;
    }

    private static string BusinessNowInForm(string html)
    {
        var match = Regex.Match(html, "data-business-now=\"([^\"]+)\"");
        match.Success.Should().BeTrue("the form gives its script the business's clock");
        return match.Groups[1].Value;
    }

    private static DateTime RealNowIn(string zone) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(zone)).DateTime;

    [Fact]
    public async Task SettingsPage_ListsTheZones_WithSkopjeFirst_AndSelectsTheCurrentOne()
    {
        var tenant = Guid.Parse("0dea0000-0000-0000-0000-00000000000a");
        var client = await LoginAsync(tenant, "zone-a@test.com");
        await SetZoneAsync(tenant, "Asia/Tokyo");

        var html = await (await client.GetAsync("/Settings/Business")).Content.ReadAsStringAsync();

        var select = Regex.Match(html, "<select[^>]*name=\"TimeZoneId\"[^>]*>(.*?)</select>", RegexOptions.Singleline).Groups[1].Value;
        var values = Regex.Matches(select, "<option[^>]*value=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
        values.Should().Equal(BusinessTimeZones.Supported);
        values[0].Should().Be("Europe/Skopje");
        Regex.Match(select, "<option[^>]*selected[^>]*>").Value.Should().Contain("Asia/Tokyo");
    }

    [Fact]
    public async Task Settings_SavesTheChosenZone_OnlyForThisTenant()
    {
        var tenant = Guid.Parse("0dea0000-0000-0000-0000-00000000000b");
        var other = Guid.Parse("0dea0000-0000-0000-0000-00000000000c");
        await TestData.EnsureTenantWithUserAsync(_factory, other, "zone-c@test.com");
        var client = await LoginAsync(tenant, "zone-b@test.com");

        var response = await client.PostFormAsync("/Settings/Business", "/Settings/Business", SettingsForm(tenant, "America/New_York"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await ZoneOfAsync(tenant)).Should().Be("America/New_York");
        (await ZoneOfAsync(other)).Should().Be("Europe/Skopje");
    }

    [Fact]
    public async Task Settings_WithAZoneNotInTheList_ShowsTheErrorAndKeepsTheOldOne()
    {
        var tenant = Guid.Parse("0dea0000-0000-0000-0000-00000000000d");
        var client = await LoginAsync(tenant, "zone-d@test.com");

        var response = await client.PostFormAsync("/Settings/Business", "/Settings/Business", SettingsForm(tenant, "Mars/Olympus"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Please choose a time zone from the list.");
        (await ZoneOfAsync(tenant)).Should().Be("Europe/Skopje");
    }

    [Theory]
    [InlineData("/Appointments/Create")]
    [InlineData("/Appointments/Calendar")]
    public async Task Pages_UseTheBusinessesClock(string page)
    {
        var tenant = Guid.Parse("0dea0000-0000-0000-0000-00000000000e");
        var client = await LoginAsync(tenant, "zone-e@test.com");
        await SetZoneAsync(tenant, "Asia/Tokyo");

        var html = await (await client.GetAsync(page)).Content.ReadAsStringAsync();

        var rendered = page.EndsWith("Calendar")
            ? Regex.Match(html, "now: \"([^\"]+)\"").Groups[1].Value
            : BusinessNowInForm(html);
        DateTime.ParseExact(rendered, "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
            .Should().BeCloseTo(RealNowIn("Asia/Tokyo"), TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task EditForm_UsesTheBusinessesClock_EvenAfterARefusedSave()
    {
        var tenant = Guid.Parse("0dea0000-0000-0000-0000-00000000000f");
        var client = await LoginAsync(tenant, "zone-f@test.com");
        await SetZoneAsync(tenant, "Asia/Tokyo");
        var when = RealNowIn("Asia/Tokyo").Date.AddDays(30).AddHours(10);
        while (when.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) when = when.AddDays(1);
        var id = await TestData.WithDbAsync(_factory, async db =>
        {
            var customer = new Domain.DomainModels.Customer
            {
                Id = Guid.NewGuid(), TenantId = tenant, CustomerNumber = 1, FirstName = "Zone", LastName = "Test",
                Email = "zone-customer@test.com", PhoneNumber = "+38970123456", City = "Skopje", State = "North Macedonia"
            };
            var appointment = new Domain.DomainModels.Appointment
            {
                Id = Guid.NewGuid(), TenantId = tenant, CustomerId = customer.Id, OrderNumber = "ZON-F1",
                AppointmentDate = when, Description = "Seeded", Status = Domain.DomainModels.AppointmentStatus.Scheduled
            };
            db.AddRange(customer, appointment);
            await db.SaveChangesAsync();
            return appointment.Id;
        });

        var form = await (await client.GetAsync($"/Appointments/Edit/{id}")).Content.ReadAsStringAsync();
        DateTime.ParseExact(BusinessNowInForm(form), "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)
            .Should().BeCloseTo(RealNowIn("Asia/Tokyo"), TimeSpan.FromMinutes(2));

        var refused = await client.PostFormAsync($"/Appointments/Edit/{id}", $"/Appointments/Edit/{id}", new Dictionary<string, string>
        {
            ["AppointmentDate"] = when.AddMinutes(5).ToString("yyyy-MM-ddTHH:mm"),
            ["OrderNumber"] = "ZON-F1",
            ["Description"] = "Edited",
            ["Status"] = "Scheduled"
        });

        refused.StatusCode.Should().Be(HttpStatusCode.OK);
        BusinessNowInForm(await refused.Content.ReadAsStringAsync()).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Dashboard_ShowsTheBusinessesDate()
    {
        var tenant = Guid.Parse("0dea0000-0000-0000-0000-000000000010");
        var client = await LoginAsync(tenant, "zone-g@test.com");
        await SetZoneAsync(tenant, "Pacific/Auckland");

        var html = await (await client.GetAsync("/Dashboard")).Content.ReadAsStringAsync();

        var shown = Regex.Match(html, "<span id=\"businessToday\">(.*?)</span>").Groups[1].Value;
        var today = RealNowIn("Pacific/Auckland");
        // Close to midnight there the date can change during the test, so accept the next day too.
        new[] { today, today.AddMinutes(2) }.Select(d => d.ToString("ddd, MMM d")).Should().Contain(shown);
    }

    [Fact]
    public async Task CalendarHolidays_WithoutAYear_StillAnswers()
    {
        var tenant = Guid.Parse("0dea0000-0000-0000-0000-000000000011");
        var client = await LoginAsync(tenant, "zone-h@test.com");

        var response = await client.GetAsync("/Appointments/CalendarHolidays");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
