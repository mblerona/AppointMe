using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>Expected failures return a friendly result instead of a 500.</summary>
public class ErrorHandlingTests : IClassFixture<HolidaysWebAppFactory>
{
    private readonly HolidaysWebAppFactory _factory;
    private static readonly Guid Tenant = CustomWebAppFactory.DefaultTenantId;

    public ErrorHandlingTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoggedInClientAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);
        return client;
    }

    private static Dictionary<string, string> CustomerForm(string email) => new()
    {
        ["FirstName"] = "Dup",
        ["LastName"] = "Licate",
        ["Email"] = email,
        ["PhoneNumber"] = "070999999",
        ["City"] = "Skopje",
        ["State"] = "North Macedonia"
    };

    // ---------- B1: duplicate customer email ----------

    [Fact]
    public async Task Customers_Create_WithDuplicateEmail_ReturnsFormWithError_NotServerError()
    {
        var client = await LoggedInClientAsync();

        var res = await client.PostFormAsync("/Customers/Create", "/Customers/Create", CustomerForm("ana@test.com"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Customer with email ana@test.com already exists");

        (await TestData.WithDbAsync(_factory, db =>
                db.Customers.CountAsync(c => c.TenantId == Tenant && c.Email == "ana@test.com")))
            .Should().Be(1);
    }

    [Fact]
    public async Task Customers_Edit_ToAnotherCustomersEmail_ReturnsFormWithError_NotServerError()
    {
        var client = await LoggedInClientAsync();

        var otherId = await TestData.WithDbAsync(_factory, async db =>
        {
            var other = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = Tenant,
                FirstName = "Other",
                LastName = "Customer",
                Email = "other-b1@test.com",
                PhoneNumber = "070111111",
                City = "Skopje",
                State = "North Macedonia",
                CustomerNumber = 900
            };
            db.Customers.Add(other);
            await db.SaveChangesAsync();
            return other.Id;
        });

        var res = await client.PostFormAsync($"/Customers/Edit/{otherId}", $"/Customers/Edit/{otherId}",
            CustomerForm("ana@test.com"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Customer with email ana@test.com already exists");
    }

    // ---------- B2: missing invoice ----------

    [Fact]
    public async Task Invoices_Details_WithUnknownId_Returns404()
    {
        var client = await LoggedInClientAsync();

        var res = await client.GetAsync($"/Invoices/Details/{Guid.NewGuid()}");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------- B3: deleting a service that appointments use ----------

    [Fact]
    public async Task Services_Delete_WhenUsedByAppointment_IsRefusedWithMessage_AndServiceIsKept()
    {
        var client = await LoggedInClientAsync();

        var serviceId = await TestData.WithDbAsync(_factory, async db =>
        {
            var svc = new ServiceOffering
            {
                Id = Guid.NewGuid(),
                BusinessId = Tenant,
                Name = "In use",
                Price = 100,
                IsActive = true
            };
            db.ServiceOfferings.Add(svc);

            var appt = await db.Appointments.FirstAsync(a => a.TenantId == Tenant);
            db.AppointmentServices.Add(new AppointmentServiceModel
            {
                AppointmentId = appt.Id,
                ServiceOfferingId = svc.Id,
                PriceAtBooking = 100
            });

            await db.SaveChangesAsync();
            return svc.Id;
        });

        var res = await client.PostFormAsync($"/Services/Delete/{serviceId}", $"/Services/Delete/{serviceId}",
            new Dictionary<string, string> { ["id"] = serviceId.ToString() });

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Contain("/Services");

        var page = await (await client.GetAsync(res.Headers.Location)).Content.ReadAsStringAsync();
        page.Should().Contain("used by existing appointments and cannot be deleted");

        (await TestData.WithDbAsync(_factory, db => db.ServiceOfferings.AnyAsync(s => s.Id == serviceId)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Services_Delete_WhenUnused_DeletesService()
    {
        var client = await LoggedInClientAsync();

        var serviceId = await TestData.WithDbAsync(_factory, async db =>
        {
            var svc = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = Tenant, Name = "Unused", Price = 10, IsActive = true };
            db.ServiceOfferings.Add(svc);
            await db.SaveChangesAsync();
            return svc.Id;
        });

        var res = await client.PostFormAsync($"/Services/Delete/{serviceId}", $"/Services/Delete/{serviceId}",
            new Dictionary<string, string> { ["id"] = serviceId.ToString() });

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await TestData.WithDbAsync(_factory, db => db.ServiceOfferings.AnyAsync(s => s.Id == serviceId)))
            .Should().BeFalse();
    }

    // ---------- B8: returnUrl on SetStatus ----------

    private async Task<HttpResponseMessage> PostSetStatusAsync(HttpClient client, string returnUrl)
    {
        var apptId = await TestData.WithDbAsync(_factory, db =>
            db.Appointments.Where(a => a.TenantId == Tenant && a.OrderNumber == "ORD-0001").Select(a => a.Id).FirstAsync());

        return await client.PostFormAsync($"/Appointments/Details/{apptId}", "/Appointments/SetStatus",
            new Dictionary<string, string>
            {
                ["id"] = apptId.ToString(),
                ["status"] = "Scheduled",
                ["returnUrl"] = returnUrl
            });
    }

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("//evil.example/phish")]
    public async Task Appointments_SetStatus_WithExternalReturnUrl_RedirectsToIndex_NotServerError(string returnUrl)
    {
        var client = await LoggedInClientAsync();

        var res = await PostSetStatusAsync(client, returnUrl);

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Be("/Appointments");
    }

    [Fact]
    public async Task Appointments_SetStatus_WithLocalReturnUrl_RedirectsThere()
    {
        var client = await LoggedInClientAsync();

        var res = await PostSetStatusAsync(client, "/Dashboard?range=today");

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Be("/Dashboard?range=today");
    }
}
