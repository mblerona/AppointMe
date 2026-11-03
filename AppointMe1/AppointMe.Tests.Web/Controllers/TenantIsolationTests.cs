using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using System.Net;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// A user of tenant B must get 404 for every record of tenant A. 404 (not 403) is deliberate:
/// it does not reveal that the id exists.
/// </summary>
public class TenantIsolationTests : IClassFixture<CustomWebAppFactory>
{
    private static readonly Guid TenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid TenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private const string UserBEmail = "userB@test.local";

    private readonly CustomWebAppFactory _factory;

    public TenantIsolationTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsTenantBAsync()
    {
        await TestData.EnsureTenantWithUserAsync(_factory, TenantA, "userA@test.local");
        await TestData.EnsureTenantWithUserAsync(_factory, TenantB, UserBEmail);

        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        await client.LoginAsync(UserBEmail, TestData.Password);
        return client;
    }

    private Task<Customer> SeedTenantACustomerAsync() =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                FirstName = "TenantA",
                LastName = "Customer",
                Email = $"a-{Guid.NewGuid():N}@test.com",
                PhoneNumber = "070000000",
                City = "Skopje",
                State = "North Macedonia",
                CustomerNumber = Random.Shared.Next(1000, int.MaxValue)
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
            return customer;
        });

    private async Task<Appointment> SeedTenantAAppointmentAsync()
    {
        var customer = await SeedTenantACustomerAsync();
        return await TestData.WithDbAsync(_factory, async db =>
        {
            var appt = new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                CustomerId = customer.Id,
                OrderNumber = $"ORD-A-{Guid.NewGuid():N}",
                Description = "Tenant A appointment",
                AppointmentDate = DateTime.UtcNow.AddDays(3),
                Status = AppointmentStatus.Scheduled
            };
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
            return appt;
        });
    }

    [Fact]
    public async Task Customers_Details_ShouldReturn404_ForOtherTenantCustomer()
    {
        var customer = await SeedTenantACustomerAsync();
        var client = await LoginAsTenantBAsync();

        var res = await client.GetAsync($"/Customers/Details/{customer.Id}");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Appointments_Details_ShouldReturn404_ForOtherTenantAppointment()
    {
        var appt = await SeedTenantAAppointmentAsync();
        var client = await LoginAsTenantBAsync();

        var res = await client.GetAsync($"/Appointments/Details/{appt.Id}");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Invoices_Details_ShouldReturn404_ForOtherTenantInvoice()
    {
        var appt = await SeedTenantAAppointmentAsync();
        var invoiceId = await TestData.WithDbAsync(_factory, async db =>
        {
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                AppointmentId = appt.Id,
                CustomerId = appt.CustomerId,
                InvoiceNumber = $"INV-A-{Guid.NewGuid():N}",
                CustomerNameSnapshot = "TenantA Customer",
                BusinessNameSnapshot = "Biz A"
            };
            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
            return invoice.Id;
        });
        var client = await LoginAsTenantBAsync();

        var res = await client.GetAsync($"/Invoices/Details/{invoiceId}");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Services_Edit_ShouldReturn404_ForOtherTenantService()
    {
        var serviceId = await TestData.WithDbAsync(_factory, async db =>
        {
            var svc = new ServiceOffering
            {
                Id = Guid.NewGuid(),
                BusinessId = TenantA,
                Name = "Tenant A service",
                Price = 100,
                IsActive = true
            };
            db.ServiceOfferings.Add(svc);
            await db.SaveChangesAsync();
            return svc.Id;
        });
        var client = await LoginAsTenantBAsync();

        var res = await client.GetAsync($"/Services/Edit/{serviceId}");

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
