using System.Net;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// The feature switches are enforced on the server: while services are off every Services page is a 404,
/// and while invoicing is off (or services are off) every invoice page, the PDF and the email actions are.
/// Nothing is deleted: switching the feature back on shows the same data again.
/// </summary>
public class FeatureFlagTests : IClassFixture<HolidaysWebAppFactory>
{
    private readonly HolidaysWebAppFactory _factory;

    public FeatureFlagTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    private sealed record Tenant(HttpClient Client, Guid TenantId, Guid ServiceId, Guid AppointmentId, Guid InvoiceId)
    {
        /// <summary>Unique per tenant, so list pages can be checked for the right invoice.</summary>
        public string InvoiceNumber => NumberFor(TenantId);
    }

    private static string NumberFor(Guid tenantId) => $"INV-{DateTime.UtcNow.Year}-{tenantId:N}";

    /// <summary>A tenant with one service, one appointment using it and an invoice for that appointment.</summary>
    private async Task<Tenant> TenantAsync(bool services, bool invoices)
    {
        var tenantId = Guid.NewGuid();
        var email = $"flags-{tenantId:N}@test.com";
        await TestData.EnsureTenantWithUserAsync(_factory, tenantId, email);

        var ids = await TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.SingleAsync(b => b.Id == tenantId);
            business.EnableServices = services;
            business.EnableInvoices = invoices;

            var customer = new Customer
            {
                Id = Guid.NewGuid(), TenantId = tenantId, FirstName = "Ana", LastName = "Doe",
                Email = $"customer-{tenantId:N}@test.com", PhoneNumber = "+38970123456",
                City = "Skopje", State = "North Macedonia", CustomerNumber = 1
            };
            var service = new ServiceOffering
            {
                Id = Guid.NewGuid(), BusinessId = tenantId, Name = "Haircut", Price = 100, IsActive = true
            };
            var appointment = new Appointment
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customer.Id, OrderNumber = "FLAG-1",
                Description = "Feature flag test", DurationMinutes = 30,
                AppointmentDate = DateTime.UtcNow.AddDays(1).Date.AddHours(10)
            };
            appointment.AppointmentServices.Add(new AppointmentServiceModel
            {
                AppointmentId = appointment.Id, ServiceOfferingId = service.Id, PriceAtBooking = 100
            });

            var year = DateTime.UtcNow.Year;
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(), TenantId = tenantId, AppointmentId = appointment.Id, CustomerId = customer.Id,
                InvoiceNumber = NumberFor(tenantId), InvoiceYear = year, InvoiceSequence = 1,
                IssuedAt = DateTime.UtcNow, CustomerNameSnapshot = "Ana Doe", CustomerEmailSnapshot = customer.Email,
                BusinessNameSnapshot = business.Name, AppointmentOrderNumberSnapshot = appointment.OrderNumber,
                AppointmentDateSnapshot = appointment.AppointmentDate, Subtotal = 100, Total = 100
            };
            invoice.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(), InvoiceId = invoice.Id, NameSnapshot = "Haircut", Qty = 1, UnitPrice = 100, LineTotal = 100
            });

            db.AddRange(customer, service, appointment, invoice);
            await db.SaveChangesAsync();
            return (service.Id, appointment.Id, invoice.Id);
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(email, TestData.Password);
        return new Tenant(client, tenantId, ids.Item1, ids.Item2, ids.Item3);
    }

    private Task SetFlagsAsync(Guid tenantId, bool services, bool invoices) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.SingleAsync(b => b.Id == tenantId);
            business.EnableServices = services;
            business.EnableInvoices = invoices;
            return await db.SaveChangesAsync();
        });

    /// <summary>POSTs with a valid antiforgery token, taken from a page that is always there.</summary>
    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string url, Dictionary<string, string>? fields = null) =>
        client.PostFormAsync("/Customers", url, fields ?? new Dictionary<string, string>());

    private static string[] ServicePages(Tenant t) =>
    [
        "/Services",
        "/Services/Create",
        $"/Services/Edit/{t.ServiceId}",
        $"/Services/Delete/{t.ServiceId}"
    ];

    private static string[] InvoicePages(Tenant t) =>
    [
        "/Invoices",
        $"/Invoices/Details/{t.InvoiceId}"
    ];

    private static (string Url, Dictionary<string, string> Fields)[] InvoicePosts(Tenant t) =>
    [
        ("/Invoices/CreateFromAppointment", new() { ["appointmentId"] = t.AppointmentId.ToString() }),
        ($"/Invoices/SendByEmail/{t.InvoiceId}", new()),
        ($"/Invoices/SendPdfByEmail/{t.InvoiceId}", new())
    ];

    // ---------------- services ----------------

    [Fact]
    public async Task Services_On_PagesAreReachable_AndTheMenuShowsThem()
    {
        var t = await TenantAsync(services: true, invoices: false);

        foreach (var url in ServicePages(t))
            (await t.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, url);

        var html = await (await t.Client.GetAsync("/Customers")).Content.ReadAsStringAsync();
        html.Should().Contain("href=\"/Services\"");
    }

    [Fact]
    public async Task Services_Off_EveryPageAndPostIs404_AndNothingIsDeleted()
    {
        var t = await TenantAsync(services: false, invoices: false);

        foreach (var url in ServicePages(t))
            (await t.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);

        (await PostAsync(t.Client, "/Services/Create", new() { ["Name"] = "New", ["Price"] = "10" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(t.Client, $"/Services/Edit/{t.ServiceId}", new() { ["Name"] = "Changed", ["Price"] = "10" }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await PostAsync(t.Client, $"/Services/Delete/{t.ServiceId}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var (services, name, lines) = await TestData.WithDbAsync(_factory, async db => (
            await db.ServiceOfferings.CountAsync(s => s.BusinessId == t.TenantId),
            await db.ServiceOfferings.Where(s => s.Id == t.ServiceId).Select(s => s.Name).SingleAsync(),
            await db.AppointmentServices.CountAsync(x => x.AppointmentId == t.AppointmentId)));
        services.Should().Be(1, "nothing was created or deleted");
        name.Should().Be("Haircut", "nothing was edited");
        lines.Should().Be(1, "the appointment keeps its service");
    }

    [Fact]
    public async Task Services_TurnedBackOn_ShowsTheSameServiceAgain()
    {
        var t = await TenantAsync(services: false, invoices: false);
        (await t.Client.GetAsync($"/Services/Edit/{t.ServiceId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SetFlagsAsync(t.TenantId, services: true, invoices: false);

        var response = await t.Client.GetAsync($"/Services/Edit/{t.ServiceId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Haircut");
    }

    // ---------------- invoices ----------------

    [Fact]
    public async Task Invoices_On_PagesAreReachable_AndTheMenuShowsThem()
    {
        var t = await TenantAsync(services: true, invoices: true);

        foreach (var url in InvoicePages(t))
            (await t.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, url);

        var html = await (await t.Client.GetAsync("/Customers")).Content.ReadAsStringAsync();
        html.Should().Contain("href=\"/Invoices\"");
    }

    [Fact]
    public async Task Invoices_On_CreateFromAppointment_ReturnsTheExistingInvoice()
    {
        var t = await TenantAsync(services: true, invoices: true);

        var response = await PostAsync(t.Client, "/Invoices/CreateFromAppointment",
            new() { ["appointmentId"] = t.AppointmentId.ToString() });

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be($"/Invoices/Details/{t.InvoiceId}");
    }

    [Theory]
    [InlineData(true, false)]  // invoicing off
    [InlineData(false, true)]  // services off: invoices are off even though the stored choice is on
    [InlineData(false, false)]
    public async Task Invoices_Off_EveryPagePdfAndEmailActionIs404_AndTheInvoiceIsKept(bool services, bool invoices)
    {
        var t = await TenantAsync(services, invoices);

        foreach (var url in InvoicePages(t))
            (await t.Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);

        foreach (var (url, fields) in InvoicePosts(t))
            (await PostAsync(t.Client, url, fields)).StatusCode.Should().Be(HttpStatusCode.NotFound, url);

        var html = await (await t.Client.GetAsync("/Customers")).Content.ReadAsStringAsync();
        html.Should().NotContain("href=\"/Invoices\"");

        var (invoiceCount, lineCount) = await TestData.WithDbAsync(_factory, async db => (
            await db.Invoices.CountAsync(i => i.TenantId == t.TenantId),
            await db.InvoiceLines.CountAsync(l => l.InvoiceId == t.InvoiceId)));
        invoiceCount.Should().Be(1, "no invoice is created or deleted");
        lineCount.Should().Be(1);
    }

    [Fact]
    public async Task Invoices_ServicesTurnedBackOn_RestoresTheStoredInvoiceChoice_AndTheSameInvoice()
    {
        var t = await TenantAsync(services: false, invoices: true);
        (await t.Client.GetAsync($"/Invoices/Details/{t.InvoiceId}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SetFlagsAsync(t.TenantId, services: true, invoices: true);

        var response = await t.Client.GetAsync($"/Invoices/Details/{t.InvoiceId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain(t.InvoiceNumber);
    }

    [Fact]
    public async Task InvoiceList_ShowsTheTenantsOwnInvoices_AndNoOtherTenants()
    {
        var mine = await TenantAsync(services: true, invoices: true);
        var other = await TenantAsync(services: true, invoices: true);

        var html = await (await mine.Client.GetAsync("/Invoices")).Content.ReadAsStringAsync();

        html.Should().Contain(mine.InvoiceNumber);
        html.Should().Contain($"href=\"/Invoices/Details/{mine.InvoiceId}\"");
        html.Should().NotContain(other.InvoiceNumber);
    }

    // ---------------- filter order ----------------

    [Theory]
    [InlineData("/Services")]
    [InlineData("/Invoices")]
    public async Task UserWithoutABusiness_IsStillSentToSettings_NotGivenA404(string url)
    {
        var email = $"flags-nobiz-{Guid.NewGuid():N}@test.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            await TestData.EnsureUserAsync(users, email, tenantId: null);
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(email, TestData.Password);

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be("/Settings/Business");
    }
}
