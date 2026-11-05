using System.Net;
using System.Text.RegularExpressions;
using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// The Edit form must show the stored date, and saving without changing it must never move the appointment.
/// These tests see the server's HTML and don't run JavaScript. The script itself misbehaved here (it used to
/// rewrite the field when the page opened), so that part is only guarded by checking the script's text.
/// </summary>
public class AppointmentEditDateTests : IClassFixture<HolidaysWebAppFactory>
{
    private readonly HolidaysWebAppFactory _factory;

    public AppointmentEditDateTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    private static readonly DateTime PastDate = new(2025, 3, 10, 10, 0, 0); // a Monday

    private async Task<(HttpClient Client, Guid CustomerId)> LoginWithCustomerAsync(Guid tenantId, string email)
    {
        await TestData.EnsureTenantWithUserAsync(_factory, tenantId, email);
        var customerId = await TestData.WithDbAsync(_factory, async db =>
        {
            var c = new Customer
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerNumber = Random.Shared.Next(1, int.MaxValue),
                FirstName = "Edit", LastName = "Date", Email = $"{Guid.NewGuid():N}@test.com",
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

    private Task<Guid> AddAppointmentAsync(Guid tenantId, Guid customerId, DateTime when, string orderNumber) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var appointment = new Appointment
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, OrderNumber = orderNumber,
                AppointmentDate = when, Description = "Seeded", Status = AppointmentStatus.Scheduled
            };
            db.Appointments.Add(appointment);
            await db.SaveChangesAsync();
            return appointment.Id;
        });

    private Task<Appointment> ReloadAsync(Guid id) =>
        TestData.WithDbAsync(_factory, db => db.Appointments.AsNoTracking().FirstAsync(a => a.Id == id));

    private static Task<HttpResponseMessage> SaveAsync(HttpClient client, Guid id, string date, string orderNumber, string description = "Edited") =>
        client.PostFormAsync($"/Appointments/Edit/{id}", $"/Appointments/Edit/{id}", new Dictionary<string, string>
        {
            ["AppointmentDate"] = date,
            ["OrderNumber"] = orderNumber,
            ["Description"] = description,
            ["Status"] = "Scheduled"
        });

    private static string DateFieldValue(string html)
    {
        var input = Regex.Match(html, "<input[^>]*id=\"apptDate\"[^>]*>");
        input.Success.Should().BeTrue("the Edit form has a date field");
        return Regex.Match(input.Value, "value=\"([^\"]*)\"").Groups[1].Value;
    }

    [Fact]
    public async Task EditForm_ForAPastAppointment_ShowsItsStoredDate()
    {
        var tenantId = Guid.Parse("0de60000-0000-0000-0000-00000000000a");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "editdate-a@test.com");
        var id = await AddAppointmentAsync(tenantId, customerId, PastDate, "EDT-A1");

        var html = await (await client.GetAsync($"/Appointments/Edit/{id}")).Content.ReadAsStringAsync();

        DateFieldValue(html).Should().Be("2025-03-10T10:00");
    }

    [Fact]
    public async Task EditForm_Script_OnlyAppliesTheRulesToADateTheUserChanged()
    {
        var tenantId = Guid.Parse("0de60000-0000-0000-0000-00000000000b");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "editdate-b@test.com");
        var id = await AddAppointmentAsync(tenantId, customerId, PastDate, "EDT-B1");

        var html = await (await client.GetAsync($"/Appointments/Edit/{id}")).Content.ReadAsStringAsync();

        html.Should().Contain("const originalValue = input.value;");
        Regex.IsMatch(html, @"(?<!if \(input\.value !== originalValue\) )input\.value = clamp\(").Should()
            .BeFalse("the field may only be rewritten when the user picked a different date");
        Regex.IsMatch(html, @"(?<!if \(originalValue >= nowValue\) )input\.min =").Should()
            .BeFalse("a minimum after the stored date makes the browser refuse to submit the form");
    }

    [Fact]
    public async Task Save_PastAppointment_WithoutChangingTheDate_KeepsItAndSavesTheOtherChanges()
    {
        var tenantId = Guid.Parse("0de60000-0000-0000-0000-00000000000c");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "editdate-c@test.com");
        var id = await AddAppointmentAsync(tenantId, customerId, PastDate, "EDT-C1");

        var response = await SaveAsync(client, id, "2025-03-10T10:00", "EDT-C1", "Edited later");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var saved = await ReloadAsync(id);
        saved.AppointmentDate.Should().Be(PastDate);
        saved.Description.Should().Be("Edited later");
    }

    [Fact]
    public async Task Save_StoredDateWithSeconds_IsNotTreatedAsMoved()
    {
        // The form works in whole minutes. Rows with seconds must not count as moved just by being saved,
        // or this past appointment would be refused as "must be in the future".
        var tenantId = Guid.Parse("0de60000-0000-0000-0000-00000000000d");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "editdate-d@test.com");
        var stored = PastDate.AddSeconds(30);
        var id = await AddAppointmentAsync(tenantId, customerId, stored, "EDT-D1");

        var html = await (await client.GetAsync($"/Appointments/Edit/{id}")).Content.ReadAsStringAsync();
        DateFieldValue(html).Should().Be("2025-03-10T10:00");

        var response = await SaveAsync(client, id, "2025-03-10T10:00", "EDT-D1");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await ReloadAsync(id)).AppointmentDate.Should().Be(stored);
    }

    [Fact]
    public async Task Save_OnADayThatIsNowClosed_WithoutChangingTheDate_IsAllowed()
    {
        var tenantId = Guid.Parse("0de60000-0000-0000-0000-00000000000e");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "editdate-e@test.com");
        var sunday = DateTime.Today.AddDays(1);
        while (sunday.DayOfWeek != DayOfWeek.Sunday) sunday = sunday.AddDays(1);
        var stored = sunday.AddHours(18); // closed day and after hours
        var id = await AddAppointmentAsync(tenantId, customerId, stored, "EDT-E1");

        var response = await SaveAsync(client, id, stored.ToString("yyyy-MM-ddTHH:mm"), "EDT-E1");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await ReloadAsync(id)).AppointmentDate.Should().Be(stored);
    }

    [Fact]
    public async Task Save_ActuallyChangingToAPastDate_IsStillRefused()
    {
        var tenantId = Guid.Parse("0de60000-0000-0000-0000-00000000000f");
        var (client, customerId) = await LoginWithCustomerAsync(tenantId, "editdate-f@test.com");
        var id = await AddAppointmentAsync(tenantId, customerId, PastDate, "EDT-F1");

        var response = await SaveAsync(client, id, "2025-04-07T10:00", "EDT-F1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("must be in the future");
        (await ReloadAsync(id)).AppointmentDate.Should().Be(PastDate);
    }
}
