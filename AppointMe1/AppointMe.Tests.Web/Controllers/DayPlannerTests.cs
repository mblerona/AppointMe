using System.Net;
using System.Text.Json;
using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>The day planner on the Create Appointment page and its JSON endpoint.</summary>
public class DayPlannerTests : IClassFixture<HolidaysWebAppFactory>
{
    private const string Url = "/Appointments/DayAvailability";

    private readonly HolidaysWebAppFactory _factory;

    public DayPlannerTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    private static DateOnly NextWeekday(DateOnly from, DayOfWeek day)
    {
        while (from.DayOfWeek != day) from = from.AddDays(1);
        return from;
    }

    // Far enough ahead that no slot is in the past; the fake holidays are 1 January and 1 May.
    private static readonly DateOnly Tuesday = NextWeekday(new DateOnly(2031, 3, 1), DayOfWeek.Tuesday);
    private static string Ymd(DateOnly d) => d.ToString("yyyy-MM-dd");

    /// <summary>A tenant of its own (Mon–Fri, 09–17, 30-minute interval) with one customer, logged in.</summary>
    private async Task<(HttpClient Client, Guid TenantId, Guid CustomerId)> TenantAsync()
    {
        var tenantId = Guid.NewGuid();
        var email = await TestData.EnsureTenantWithUserAsync(_factory, tenantId, $"planner-{tenantId:N}@test.com");
        var customerId = await TestData.WithDbAsync(_factory, async db =>
        {
            var c = new Customer
            {
                Id = Guid.NewGuid(), TenantId = tenantId, FirstName = "Ana", LastName = "Planner",
                Email = $"c-{tenantId:N}@test.com", PhoneNumber = "+38970123456", CustomerNumber = 1
            };
            db.Customers.Add(c);
            await db.SaveChangesAsync();
            return c.Id;
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(email, TestData.Password);
        return (client, tenantId, customerId);
    }

    private Task BookAsync(Guid tenantId, Guid customerId, DateOnly day, int hour, int duration, string order,
        AppointmentStatus status = AppointmentStatus.Scheduled) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, OrderNumber = order,
                Description = "Planner", DurationMinutes = duration, Status = status,
                AppointmentDate = day.ToDateTime(new TimeOnly(hour, 0))
            });
            await db.SaveChangesAsync();
            return 0;
        });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static string StateAt(JsonElement day, int minute) =>
        day.GetProperty("slots").EnumerateArray()
            .Single(s => s.GetProperty("startMinute").GetInt32() == minute)
            .GetProperty("state").GetString()!;

    [Fact]
    public async Task CreatePage_HasThePlanner_PointingAtTheEndpoint()
    {
        var (client, _, _) = await TenantAsync();

        var html = await (await client.GetAsync("/Appointments/Create")).Content.ReadAsStringAsync();

        html.Should().Contain("id=\"dayPlanner\"").And.Contain($"data-url=\"{Url}\"");
        html.Should().Contain("/js/day-planner.js").And.Contain("/css/day-planner.css");
    }

    [Fact]
    public async Task AnOpenDay_ListsTheSlots_AndTheBookingsThatBlockThem()
    {
        var (client, tenantId, customerId) = await TenantAsync();
        await BookAsync(tenantId, customerId, Tuesday, 10, 60, "PL-1");
        await BookAsync(tenantId, customerId, Tuesday, 14, 30, "PL-CANCELLED", AppointmentStatus.Cancelled);

        var day = await JsonAsync(await client.GetAsync($"{Url}?date={Ymd(Tuesday)}"));

        day.GetProperty("date").GetString().Should().Be(Ymd(Tuesday));
        day.GetProperty("isOpen").GetBoolean().Should().BeTrue();
        day.GetProperty("openingMinute").GetInt32().Should().Be(540);
        day.GetProperty("closingMinute").GetInt32().Should().Be(1020);
        day.GetProperty("slotMinutes").GetInt32().Should().Be(30);
        day.GetProperty("slots").GetArrayLength().Should().Be(16);

        StateAt(day, 600).Should().Be("booked");
        StateAt(day, 630).Should().Be("booked");
        StateAt(day, 660).Should().Be("free");
        StateAt(day, 840).Should().Be("free", "a cancelled appointment doesn't block");

        var booking = day.GetProperty("bookings").EnumerateArray().Single();
        booking.GetProperty("orderNumber").GetString().Should().Be("PL-1");
        booking.GetProperty("customerName").GetString().Should().Be("Ana Planner");
        booking.GetProperty("startMinute").GetInt32().Should().Be(600);
        booking.GetProperty("endMinute").GetInt32().Should().Be(660);
        booking.GetProperty("url").GetString().Should().StartWith("/Appointments/Details/");
    }

    [Fact]
    public async Task AnotherTenantsBookings_AreNeverShown()
    {
        var (client, _, _) = await TenantAsync();
        var (_, otherTenant, otherCustomer) = await TenantAsync();
        await BookAsync(otherTenant, otherCustomer, Tuesday, 11, 30, "PL-OTHER");

        var day = await JsonAsync(await client.GetAsync($"{Url}?date={Ymd(Tuesday)}"));

        day.GetProperty("bookings").GetArrayLength().Should().Be(0);
        StateAt(day, 660).Should().Be("free");
    }

    [Fact]
    public async Task AClosedDay_AndAHoliday_HaveNoSlots()
    {
        var (client, _, _) = await TenantAsync();

        var saturday = await JsonAsync(await client.GetAsync($"{Url}?date={Ymd(Tuesday.AddDays(4))}"));
        saturday.GetProperty("isOpen").GetBoolean().Should().BeFalse();
        saturday.GetProperty("holidayName").ValueKind.Should().Be(JsonValueKind.Null);
        saturday.GetProperty("slots").GetArrayLength().Should().Be(0);

        var newYear = new DateOnly(2031, 1, 1);
        newYear.DayOfWeek.Should().Be(DayOfWeek.Wednesday, "an open weekday, so only the holiday closes it");
        var holiday = await JsonAsync(await client.GetAsync($"{Url}?date={Ymd(newYear)}"));
        holiday.GetProperty("isOpen").GetBoolean().Should().BeFalse();
        holiday.GetProperty("holidayName").GetString().Should().Be("Нова Година");
    }

    [Theory]
    [InlineData("")]
    [InlineData("tomorrow")]
    [InlineData("2031-13-01")]
    [InlineData("01.03.2031")]
    public async Task AnInvalidDate_IsABadRequest(string date)
    {
        var (client, _, _) = await TenantAsync();

        var response = await client.GetAsync($"{Url}?date={Uri.EscapeDataString(date)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Anonymous_IsSentToLogin()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.GetAsync($"{Url}?date={Ymd(Tuesday)}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Identity/Account/Login");
    }
}
