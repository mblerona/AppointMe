using System.Net;
using System.Text.Json;
using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>Audit B11: appointments keep the length they were booked with, through the real forms and calendar feed.</summary>
public class AppointmentDurationTests : IClassFixture<HolidaysWebAppFactory>
{
    private readonly HolidaysWebAppFactory _factory;

    public AppointmentDurationTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    private static DateTime FutureWorkingDay(int daysAhead)
    {
        var d = DateTime.Today.AddDays(daysAhead);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
               || (d.Month == 1 && d.Day == 1) || (d.Month == 5 && d.Day == 1))
            d = d.AddDays(1);
        return d;
    }

    private async Task<(HttpClient Client, Guid CustomerId)> LoginWithCustomerAsync(Guid tenantId, string email)
    {
        await TestData.EnsureTenantWithUserAsync(_factory, tenantId, email);
        var customerId = await TestData.WithDbAsync(_factory, async db =>
        {
            var c = new Customer
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerNumber = Random.Shared.Next(1, int.MaxValue),
                FirstName = "Duration", LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com",
                PhoneNumber = "+38970123456", City = "Skopje", State = "North Macedonia"
            };
            db.Customers.Add(c);
            await db.SaveChangesAsync();
            return c.Id;
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(email, TestData.Password);
        return (client, customerId);
    }

    private Task SetSlotLengthAsync(Guid tenantId, int minutes) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.FirstAsync(b => b.Id == tenantId);
            business.DefaultSlotMinutes = minutes;
            return await db.SaveChangesAsync();
        });

    private static Task<HttpResponseMessage> BookAsync(HttpClient client, Guid customerId, DateTime when, string orderNumber) =>
        client.PostFormAsync("/Appointments/Create", "/Appointments/Create", new Dictionary<string, string>
        {
            ["CustomerId"] = customerId.ToString(),
            ["OrderNumber"] = orderNumber,
            ["AppointmentDate"] = when.ToString("yyyy-MM-ddTHH:mm"),
            ["Description"] = "Duration test"
        });

    [Fact]
    public async Task ChangingTheSlotLength_DoesNotChangeExistingAppointments()
    {
        var tenantId = Guid.Parse("0de50000-0000-0000-0000-00000000000a");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "duration-a@test.com");
        var day = FutureWorkingDay(30);

        await SetSlotLengthAsync(tenantId, 60);
        (await BookAsync(client, customerId, day.AddHours(10), "DUR-A1")).StatusCode.Should().Be(HttpStatusCode.Redirect);

        await SetSlotLengthAsync(tenantId, 15);

        var inside = await BookAsync(client, customerId, day.AddHours(10).AddMinutes(30), "DUR-A2");
        inside.StatusCode.Should().Be(HttpStatusCode.OK, "10:30 is inside the 60-minute booking");
        (await inside.Content.ReadAsStringAsync()).Should().Contain("overlaps with another appointment");

        (await BookAsync(client, customerId, day.AddHours(11), "DUR-A3")).StatusCode.Should().Be(HttpStatusCode.Redirect);

        var durations = await TestData.WithDbAsync(_factory, db => db.Appointments
            .Where(a => a.TenantId == tenantId)
            .OrderBy(a => a.AppointmentDate)
            .Select(a => a.DurationMinutes)
            .ToListAsync());
        durations.Should().Equal(60, 15);
    }

    private Task<Guid> AddAppointmentAsync(Guid tenantId, Guid customerId, DateTime when, AppointmentStatus status, string orderNumber) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var appointment = new Appointment
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, OrderNumber = orderNumber,
                AppointmentDate = when, Description = "Seeded", Status = status
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            return appointment.Id;
        });

    private Task<AppointmentStatus> StatusOfAsync(Guid id) =>
        TestData.WithDbAsync(_factory, db => db.Appointments.Where(a => a.Id == id).Select(a => a.Status).FirstAsync());

    [Fact]
    public async Task EndingAtClosingTime_IsAllowed_EndingAfterIt_ShowsTheError()
    {
        var tenantId = Guid.Parse("0de50000-0000-0000-0000-00000000000c");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "duration-c@test.com");
        var day = FutureWorkingDay(32);

        var service = await TestData.WithDbAsync(_factory, async db =>
        {
            var s = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = tenantId, Name = "One hour", Price = 100, DurationMinutes = 60 };
            db.ServiceOfferings.Add(s);
            await db.SaveChangesAsync();
            return s.Id;
        });

        Task<HttpResponseMessage> BookWithServiceAsync(DateTime when, string order) =>
            client.PostFormAsync("/Appointments/Create", "/Appointments/Create", new Dictionary<string, string>
            {
                ["CustomerId"] = customerId.ToString(),
                ["OrderNumber"] = order,
                ["AppointmentDate"] = when.ToString("yyyy-MM-ddTHH:mm"),
                ["Description"] = "Duration test",
                ["ServiceOfferingIds"] = service.ToString()
            });

        var tooLate = await BookWithServiceAsync(day.AddHours(16).AddMinutes(30), "DUR-C1");
        tooLate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await tooLate.Content.ReadAsStringAsync()).Should().Contain("would end after closing time");

        (await BookWithServiceAsync(day.AddHours(16), "DUR-C2")).StatusCode
            .Should().Be(HttpStatusCode.Redirect, "16:00 + 60 minutes ends exactly at closing time");
    }

    [Fact]
    public async Task StartingOffTheInterval_ShowsTheErrorWithSuggestions()
    {
        var tenantId = Guid.Parse("0de50000-0000-0000-0000-000000000011");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "duration-h@test.com");
        var day = FutureWorkingDay(36);

        var refused = await BookAsync(client, customerId, day.AddHours(10).AddMinutes(5), "DUR-H1");

        refused.StatusCode.Should().Be(HttpStatusCode.OK);
        (await refused.Content.ReadAsStringAsync()).Should()
            .Contain("Appointments start every 30 minutes from opening time (09:00). Try 10:00 or 10:30.");

        (await BookAsync(client, customerId, day.AddHours(10).AddMinutes(30), "DUR-H2")).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task SettingsPage_CallsItTheTimeSlotInterval()
    {
        var tenantId = Guid.Parse("0de50000-0000-0000-0000-000000000012");
        var (client, _) = await LoginWithCustomerAsync(tenantId, "duration-i@test.com");

        var html = await (await client.GetAsync("/Settings/Business")).Content.ReadAsStringAsync();

        html.Should().Contain("Time slot interval");
        html.Should().Contain("Appointments start at these intervals from opening time. Without services, this is also the appointment length.");
        html.Should().NotContain("Appointment slot duration");
        html.Should().NotContain("must start exactly on this interval");
    }

    [Fact]
    public async Task SetStatus_ReactivatingOntoATakenTime_ShowsTheErrorAndStaysCancelled()
    {
        var tenantId = Guid.Parse("0de50000-0000-0000-0000-00000000000d");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "duration-d@test.com");
        var when = FutureWorkingDay(33).AddHours(10);
        var cancelled = await AddAppointmentAsync(tenantId, customerId, when, AppointmentStatus.Cancelled, "DUR-D1");
        await AddAppointmentAsync(tenantId, customerId, when, AppointmentStatus.Scheduled, "DUR-D2");

        var response = await client.PostFormAsync("/Appointments/Create", $"/Appointments/SetStatus/{cancelled}",
            new Dictionary<string, string> { ["status"] = "Scheduled" });

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await StatusOfAsync(cancelled)).Should().Be(AppointmentStatus.Cancelled);

        var page = await client.GetAsync(response.Headers.Location);
        (await page.Content.ReadAsStringAsync()).Should().Contain("overlaps with another appointment");
    }

    [Fact]
    public async Task Edit_ReactivatingOntoATakenTime_ShowsTheErrorAndStaysCancelled()
    {
        var tenantId = Guid.Parse("0de50000-0000-0000-0000-00000000000e");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "duration-e@test.com");
        var when = FutureWorkingDay(34).AddHours(10);
        var cancelled = await AddAppointmentAsync(tenantId, customerId, when, AppointmentStatus.Cancelled, "DUR-E1");
        await AddAppointmentAsync(tenantId, customerId, when, AppointmentStatus.Scheduled, "DUR-E2");

        var response = await client.PostFormAsync("/Appointments/Create", $"/Appointments/Edit/{cancelled}",
            new Dictionary<string, string>
            {
                ["AppointmentDate"] = when.ToString("yyyy-MM-ddTHH:mm"),
                ["OrderNumber"] = "DUR-E1",
                ["Description"] = "Seeded",
                ["Status"] = "Scheduled"
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("overlaps with another appointment");
        (await StatusOfAsync(cancelled)).Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task SetStatus_AnotherTenantsAppointment_Returns404()
    {
        var tenantA = Guid.Parse("0de50000-0000-0000-0000-00000000000f");
        var tenantB = Guid.Parse("0de50000-0000-0000-0000-000000000010");
        var (_, customerA) = await LoginWithCustomerAsync(tenantA, "duration-f@test.com");
        var (clientB, _) = await LoginWithCustomerAsync(tenantB, "duration-g@test.com");
        var cancelled = await AddAppointmentAsync(tenantA, customerA, FutureWorkingDay(35).AddHours(10), AppointmentStatus.Cancelled, "DUR-F1");

        var response = await clientB.PostFormAsync("/Appointments/Create", $"/Appointments/SetStatus/{cancelled}",
            new Dictionary<string, string> { ["status"] = "Scheduled" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await StatusOfAsync(cancelled)).Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task CalendarEvents_EndAfterTheStoredLength()
    {
        var tenantId = Guid.Parse("0de50000-0000-0000-0000-00000000000b");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "duration-b@test.com");
        var start = FutureWorkingDay(31).AddHours(10);

        await TestData.WithDbAsync(_factory, async db =>
        {
            db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, OrderNumber = "DUR-B1",
                AppointmentDate = start, DurationMinutes = 45, Description = "Calendar", Status = AppointmentStatus.Scheduled
            });
            return await db.SaveChangesAsync();
        });

        var response = await client.GetAsync("/Appointments/CalendarEvents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var ev = json.RootElement.EnumerateArray().Single();
        ev.GetProperty("start").GetString().Should().Be(start.ToString("s"));
        ev.GetProperty("end").GetString().Should().Be(start.AddMinutes(45).ToString("s"));
    }
}
