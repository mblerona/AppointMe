using System.Net;
using System.Text.RegularExpressions;
using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// The data the appointment forms give the duration script. These tests see the server's HTML and
/// don't run JavaScript, so they can't prove what the script does with it.
/// </summary>
public class AppointmentDurationHintTests : IClassFixture<HolidaysWebAppFactory>
{
    private readonly HolidaysWebAppFactory _factory;

    public AppointmentDurationHintTests(HolidaysWebAppFactory factory)
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
                FirstName = "Hint", LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com",
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

    private Task<Guid> AddServiceAsync(Guid tenantId, string name, int? minutes) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var service = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = tenantId, Name = name, Price = 100, DurationMinutes = minutes };
            db.ServiceOfferings.Add(service);
            await db.SaveChangesAsync();
            return service.Id;
        });

    private Task<Guid> AddAppointmentAsync(Guid tenantId, Guid customerId, DateTime when, string orderNumber, int minutes, params Guid[] services) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var appointment = new Appointment
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, OrderNumber = orderNumber,
                AppointmentDate = when, DurationMinutes = minutes, Description = "Seeded", Status = AppointmentStatus.Scheduled
            };
            foreach (var id in services)
                appointment.AppointmentServices.Add(new AppointmentServiceModel { AppointmentId = appointment.Id, ServiceOfferingId = id, PriceAtBooking = 100 });
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            return appointment.Id;
        });

    private Task SetBusinessAsync(Guid tenantId, Action<Business> change) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            change(await db.Businesses.FirstAsync(b => b.Id == tenantId));
            return await db.SaveChangesAsync();
        });

    private static string HintElement(string html)
    {
        var match = Regex.Match(html, "<div id=\"durationHint\"[^>]*>");
        match.Success.Should().BeTrue("the form has the duration hint");
        return match.Value;
    }

    private static string CheckboxFor(string html, Guid service)
    {
        var match = Regex.Match(html, $"<input[^>]*value=\"{service}\"[^>]*>");
        match.Success.Should().BeTrue("the form lists the service");
        return match.Value;
    }

    [Fact]
    public async Task CreateForm_GivesTheScriptTheSlotClosingTimeAndServiceDurations()
    {
        var tenant = Guid.Parse("0de90000-0000-0000-0000-00000000000a");
        var (client, _) = await LoginWithCustomerAsync(tenant, "hint-a@test.com");
        await SetBusinessAsync(tenant, b => { b.DefaultSlotMinutes = 20; b.WorkDayEnd = new TimeSpan(18, 30, 0); });
        var withDuration = await AddServiceAsync(tenant, "With", 45);
        var without = await AddServiceAsync(tenant, "Without", null);

        var html = await (await client.GetAsync("/Appointments/Create")).Content.ReadAsStringAsync();

        var hint = HintElement(html);
        hint.Should().Contain("data-slot-minutes=\"20\"");
        hint.Should().Contain("data-work-end=\"18:30\"");
        hint.Should().Contain("data-services-enabled=\"true\"");
        hint.Should().NotContain("data-stored-duration", "a new booking has nothing stored");

        CheckboxFor(html, withDuration).Should().Contain("data-duration-minutes=\"45\"");
        CheckboxFor(html, without).Should().Contain("data-duration-minutes=\"\"", "Razor keeps empty data- attributes; the script counts this as one slot");
    }

    [Fact]
    public async Task CreateForm_WithServicesDisabled_SaysSo()
    {
        var tenant = Guid.Parse("0de90000-0000-0000-0000-00000000000b");
        var (client, _) = await LoginWithCustomerAsync(tenant, "hint-b@test.com");
        await SetBusinessAsync(tenant, b => b.EnableServices = false);

        var html = await (await client.GetAsync("/Appointments/Create")).Content.ReadAsStringAsync();

        HintElement(html).Should().Contain("data-services-enabled=\"false\"");
    }

    [Fact]
    public async Task EditForm_GivesTheScriptWhatTheAppointmentHasStored()
    {
        var tenant = Guid.Parse("0de90000-0000-0000-0000-00000000000c");
        var (client, customer) = await LoginWithCustomerAsync(tenant, "hint-c@test.com");
        var service = await AddServiceAsync(tenant, "Long now", 240);
        var when = FutureWorkingDay(50).AddHours(10);
        var id = await AddAppointmentAsync(tenant, customer, when, "HNT-C1", 60, service);

        var html = await (await client.GetAsync($"/Appointments/Edit/{id}")).Content.ReadAsStringAsync();

        var hint = HintElement(html);
        hint.Should().Contain("data-stored-duration=\"60\"", "the snapshot, not the service's current 240");
        hint.Should().Contain($"data-stored-date=\"{when:yyyy-MM-ddTHH:mm}\"");
        hint.Should().Contain($"data-stored-services=\"{service}\"");
    }

    [Fact]
    public async Task EditForm_AfterARefusedSave_StillComparesAgainstWhatIsStored()
    {
        var tenant = Guid.Parse("0de90000-0000-0000-0000-00000000000d");
        var (client, customer) = await LoginWithCustomerAsync(tenant, "hint-d@test.com");
        var when = FutureWorkingDay(51).AddHours(10);
        var id = await AddAppointmentAsync(tenant, customer, when, "HNT-D1", 60);
        var attempted = when.AddHours(6).AddMinutes(45); // 16:45 + 60 minutes is past closing

        var response = await client.PostFormAsync($"/Appointments/Edit/{id}", $"/Appointments/Edit/{id}", new Dictionary<string, string>
        {
            ["AppointmentDate"] = attempted.ToString("yyyy-MM-ddTHH:mm"),
            ["OrderNumber"] = "HNT-D1",
            ["Description"] = "Edited",
            ["Status"] = "Scheduled"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("would end after closing time");
        HintElement(html).Should().Contain($"data-stored-date=\"{when:yyyy-MM-ddTHH:mm}\"", "not the date that was just refused");
    }

    [Theory]
    [InlineData("/Appointments/Create")]
    [InlineData("EDIT")]
    public async Task Forms_LoadTheSharedScript_AndLeaveTheSaveButtonToIt(string page)
    {
        var tenant = Guid.Parse("0de90000-0000-0000-0000-00000000000e");
        var (client, customer) = await LoginWithCustomerAsync(tenant, "hint-e@test.com");
        if (page == "EDIT")
        {
            var order = $"HNT-E{Guid.NewGuid():N}"[..20];
            page = $"/Appointments/Edit/{await AddAppointmentAsync(tenant, customer, FutureWorkingDay(52).AddHours(10), order, 30)}";
        }

        var html = await (await client.GetAsync(page)).Content.ReadAsStringAsync();

        html.Should().Contain("/js/appointment-form.js");
        html.Should().Contain("AppointmentForm.initDurationHint(input, saveGuard)");

        // Direct writes here would undo the shared script's block.
        Regex.IsMatch(html, @"submitBtn\.disabled\s*=").Should().BeFalse();
        html.Should().NotContain("setCustomValidity");
    }

    [Fact]
    public async Task SharedScript_IsServed()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/js/appointment-form.js");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("window.AppointmentForm");
    }
}
