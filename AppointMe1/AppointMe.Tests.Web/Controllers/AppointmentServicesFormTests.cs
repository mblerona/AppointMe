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

/// <summary>Services on the appointment forms: which ones are offered, and what saving does to the duration.</summary>
public class AppointmentServicesFormTests : IClassFixture<HolidaysWebAppFactory>
{
    private readonly HolidaysWebAppFactory _factory;

    public AppointmentServicesFormTests(HolidaysWebAppFactory factory)
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
                FirstName = "Form", LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com",
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

    private Task<Guid> AddServiceAsync(Guid tenantId, string name, int? minutes = null, bool isActive = true) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var service = new ServiceOffering
            {
                Id = Guid.NewGuid(), BusinessId = tenantId, Name = name, Price = 100, DurationMinutes = minutes, IsActive = isActive
            };
            db.ServiceOfferings.Add(service);
            await db.SaveChangesAsync();
            return service.Id;
        });

    private Task<Guid> AddAppointmentAsync(Guid tenantId, Guid customerId, DateTime when, string orderNumber, int minutes = 30, params Guid[] services) =>
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

    private Task<Appointment> ReloadAsync(Guid id) =>
        TestData.WithDbAsync(_factory, db => db.Appointments.AsNoTracking().Include(a => a.AppointmentServices).FirstAsync(a => a.Id == id));

    private static List<string> OfferedServices(string html) =>
        Regex.Matches(html, "<div class=\"service-name\">(.*?)</div>").Select(m => m.Groups[1].Value).ToList();

    private static Dictionary<string, string> EditFields(DateTime when, string orderNumber) => new()
    {
        ["AppointmentDate"] = when.ToString("yyyy-MM-ddTHH:mm"),
        ["OrderNumber"] = orderNumber,
        ["Description"] = "Edited",
        ["Status"] = "Scheduled"
    };

    /// <summary>Posts a form that can repeat a field, which the checkboxes need.</summary>
    private static async Task<HttpResponseMessage> PostWithServicesAsync(
        HttpClient client, string formPage, string postUrl, Dictionary<string, string> fields, params Guid[] services)
    {
        var html = await (await client.GetAsync(formPage)).Content.ReadAsStringAsync();
        var token = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

        var pairs = fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value)).ToList();
        pairs.Add(new("__RequestVerificationToken", token));
        pairs.AddRange(services.Select(s => new KeyValuePair<string, string>("ServiceOfferingIds", s.ToString())));

        return await client.PostAsync(postUrl, new FormUrlEncodedContent(pairs));
    }

    [Fact]
    public async Task CreateForm_OffersOnlyActiveServices()
    {
        var tenant = Guid.Parse("0de80000-0000-0000-0000-00000000000a");
        var (client, _) = await LoginWithCustomerAsync(tenant, "form-a@test.com");
        await AddServiceAsync(tenant, "Active one");
        await AddServiceAsync(tenant, "Inactive one", isActive: false);

        var html = await (await client.GetAsync("/Appointments/Create")).Content.ReadAsStringAsync();

        var offered = OfferedServices(html);
        offered.Should().ContainSingle().Which.Should().StartWith("Active one");
    }

    [Fact]
    public async Task EditForm_AlsoOffersAnInactiveServiceAlreadyAttached_MarkedAsInactive()
    {
        var tenant = Guid.Parse("0de80000-0000-0000-0000-00000000000b");
        var (client, customer) = await LoginWithCustomerAsync(tenant, "form-b@test.com");
        await AddServiceAsync(tenant, "Active one");
        await AddServiceAsync(tenant, "Other inactive", isActive: false);
        var attached = await AddServiceAsync(tenant, "Attached inactive", isActive: false);
        var id = await AddAppointmentAsync(tenant, customer, FutureWorkingDay(40).AddHours(10), "FRM-B1", services: attached);

        var html = await (await client.GetAsync($"/Appointments/Edit/{id}")).Content.ReadAsStringAsync();

        var offered = OfferedServices(html);
        offered.Should().HaveCount(2);
        offered.Should().Contain(s => s.StartsWith("Active one") && !s.Contains("(inactive)"));
        offered.Should().Contain(s => s.StartsWith("Attached inactive") && s.Contains("(inactive)"));
    }

    [Fact]
    public async Task EditForm_AfterARefusedSave_StillOffersTheAttachedInactiveService()
    {
        var tenant = Guid.Parse("0de80000-0000-0000-0000-00000000000c");
        var (client, customer) = await LoginWithCustomerAsync(tenant, "form-c@test.com");
        var attached = await AddServiceAsync(tenant, "Attached inactive", isActive: false);
        var when = FutureWorkingDay(41).AddHours(10);
        var id = await AddAppointmentAsync(tenant, customer, when, "FRM-C1", services: attached);
        await AddAppointmentAsync(tenant, customer, when.AddHours(2), "FRM-C2");

        // Moving onto the other appointment is refused. The checkbox was unticked, but the service is still attached.
        var response = await PostWithServicesAsync(client, $"/Appointments/Edit/{id}", $"/Appointments/Edit/{id}",
            EditFields(when.AddHours(2), "FRM-C1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("overlaps");
        OfferedServices(html).Should().ContainSingle().Which.Should().StartWith("Attached inactive");
    }

    [Fact]
    public async Task Create_PostingAnInactiveService_ShowsTheError()
    {
        var tenant = Guid.Parse("0de80000-0000-0000-0000-00000000000d");
        var (client, customer) = await LoginWithCustomerAsync(tenant, "form-d@test.com");
        var inactive = await AddServiceAsync(tenant, "Inactive one", isActive: false);

        var response = await PostWithServicesAsync(client, "/Appointments/Create", "/Appointments/Create", new Dictionary<string, string>
        {
            ["CustomerId"] = customer.ToString(),
            ["OrderNumber"] = "FRM-D1",
            ["AppointmentDate"] = FutureWorkingDay(42).AddHours(10).ToString("yyyy-MM-ddTHH:mm"),
            ["Description"] = "Form test"
        }, inactive);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("selected services are invalid");
    }

    [Fact]
    public async Task Create_WithServices_StoresTheCalculatedDuration_AndDetailsShowsIt()
    {
        var tenant = Guid.Parse("0de80000-0000-0000-0000-00000000000e");
        var (client, customer) = await LoginWithCustomerAsync(tenant, "form-e@test.com");
        var a = await AddServiceAsync(tenant, "Forty-five", 45);
        var b = await AddServiceAsync(tenant, "No duration");

        var response = await PostWithServicesAsync(client, "/Appointments/Create", "/Appointments/Create", new Dictionary<string, string>
        {
            ["CustomerId"] = customer.ToString(),
            ["OrderNumber"] = "FRM-E1",
            ["AppointmentDate"] = FutureWorkingDay(43).AddHours(10).ToString("yyyy-MM-ddTHH:mm"),
            ["Description"] = "Form test"
        }, a, b);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var id = await TestData.WithDbAsync(_factory, db => db.Appointments.Where(x => x.OrderNumber == "FRM-E1").Select(x => x.Id).FirstAsync());
        (await ReloadAsync(id)).DurationMinutes.Should().Be(90);

        var details = await (await client.GetAsync($"/Appointments/Details/{id}")).Content.ReadAsStringAsync();
        details.Should().Contain("<span id=\"appointmentDuration\">1 h 30 min</span>");
    }

    [Fact]
    public async Task Edit_WithServicesDisabled_KeepsTheAppointmentsServices()
    {
        var tenant = Guid.Parse("0de80000-0000-0000-0000-00000000000f");
        var (client, customer) = await LoginWithCustomerAsync(tenant, "form-f@test.com");
        var service = await AddServiceAsync(tenant, "Kept", 60);
        var when = FutureWorkingDay(44).AddHours(10);
        var id = await AddAppointmentAsync(tenant, customer, when, "FRM-F1", 60, service);
        await TestData.WithDbAsync(_factory, async db =>
        {
            (await db.Businesses.FirstAsync(b => b.Id == tenant)).EnableServices = false;
            return await db.SaveChangesAsync();
        });

        var response = await PostWithServicesAsync(client, $"/Appointments/Edit/{id}", $"/Appointments/Edit/{id}", EditFields(when, "FRM-F1"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var saved = await ReloadAsync(id);
        saved.DurationMinutes.Should().Be(60);
        saved.AppointmentServices.Should().ContainSingle().Which.ServiceOfferingId.Should().Be(service);
    }

    [Fact]
    public async Task Edit_InvalidFormForAnotherTenantsAppointment_Returns404()
    {
        var tenantA = Guid.Parse("0de80000-0000-0000-0000-000000000010");
        var tenantB = Guid.Parse("0de80000-0000-0000-0000-000000000011");
        var (_, customerA) = await LoginWithCustomerAsync(tenantA, "form-g@test.com");
        var (clientB, _) = await LoginWithCustomerAsync(tenantB, "form-h@test.com");
        var when = FutureWorkingDay(45).AddHours(10);
        var id = await AddAppointmentAsync(tenantA, customerA, when, "FRM-G1");

        var fields = EditFields(when, "FRM-G1");
        fields["AppointmentDate"] = "not a date";
        var response = await PostWithServicesAsync(clientB, "/Appointments/Create", $"/Appointments/Edit/{id}", fields);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
