using System.Net;
using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// The pages show dates stored in UTC in the business's time zone. 20:00 UTC on 1 June is already
/// 2 June in Tokyo, so a date still shown in UTC is visibly a day off. Also covers the pages for another tenant.
/// </summary>
public class BusinessLocalDatesPageTests : IClassFixture<HolidaysWebAppFactory>
{
    private static readonly Guid Tenant = Guid.Parse("0deb0000-0000-0000-0000-00000000000a");
    private static readonly Guid OtherTenant = Guid.Parse("0deb0000-0000-0000-0000-00000000000b");
    private static readonly DateTime StoredUtc = new(2026, 6, 1, 20, 0, 0);
    private static readonly DateTime AppointmentTime = new(2026, 7, 1, 10, 0, 0);

    private readonly HolidaysWebAppFactory _factory;

    public BusinessLocalDatesPageTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    private static string Day(int day) => new DateTime(2026, 6, day).ToString("dd MMM yyyy");

    private async Task<(HttpClient Client, Guid Customer, Guid Appointment, Guid Invoice)> SeedAsync()
    {
        await TestData.EnsureTenantWithUserAsync(_factory, Tenant, "dates@test.com");
        var ids = await TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.FirstAsync(b => b.Id == Tenant);
            business.TimeZoneId = "Asia/Tokyo";
            business.EnableInvoices = true;

            var existing = await db.Invoices.FirstOrDefaultAsync(i => i.TenantId == Tenant);
            if (existing != null)
                return (existing.CustomerId, existing.AppointmentId, existing.Id);

            var customer = new Customer
            {
                Id = Guid.NewGuid(), TenantId = Tenant, CustomerNumber = 1, FirstName = "Date", LastName = "Test",
                Email = "date-customer@test.com", PhoneNumber = "+38970123456", City = "Skopje", State = "North Macedonia",
                CreatedAt = StoredUtc, UpdatedAt = StoredUtc
            };
            var appointment = new Appointment
            {
                Id = Guid.NewGuid(), TenantId = Tenant, CustomerId = customer.Id, OrderNumber = "DAT-1", Description = "Seeded",
                AppointmentDate = AppointmentTime, Status = AppointmentStatus.Scheduled, CreatedAt = StoredUtc, UpdatedAt = StoredUtc
            };
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(), TenantId = Tenant, AppointmentId = appointment.Id, CustomerId = customer.Id,
                InvoiceNumber = "INV-2026-0001", InvoiceYear = 2026, InvoiceSequence = 1, IssuedAt = StoredUtc,
                CustomerNameSnapshot = "Date Test", BusinessNameSnapshot = "Biz", AppointmentDateSnapshot = AppointmentTime
            };
            db.AddRange(customer, appointment, invoice);
            await db.SaveChangesAsync();
            return (customer.Id, appointment.Id, invoice.Id);
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync("dates@test.com", TestData.Password);
        return (client, ids.Item1, ids.Item2, ids.Item3);
    }

    [Fact]
    public async Task CustomerDetails_ShowsTheBusinessesDate()
    {
        var (client, customer, _, _) = await SeedAsync();

        var html = await (await client.GetAsync($"/Customers/Details/{customer}")).Content.ReadAsStringAsync();

        html.Should().Contain($"Created: {Day(2)}");
        html.Should().Contain($"Updated: {Day(2)}");
        html.Should().NotContain(Day(1), "that would be the UTC date");
    }

    [Fact]
    public async Task AppointmentDetails_ShowsTheBusinessesDate()
    {
        var (client, _, appointment, _) = await SeedAsync();

        var html = await (await client.GetAsync($"/Appointments/Details/{appointment}")).Content.ReadAsStringAsync();

        html.Should().Contain($"Created: {Day(2)}");
        html.Should().Contain($"Updated: {Day(2)}");
        html.Should().NotContain(Day(1), "that would be the UTC date");
    }

    [Fact]
    public async Task InvoiceDetails_ShowsTheBusinessesIssueDate()
    {
        var (client, _, _, invoice) = await SeedAsync();

        var response = await client.GetAsync($"/Invoices/Details/{invoice}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain($"Issued: {Day(2)}");
        html.Should().NotContain($"Issued: {Day(1)}");
    }

    [Fact]
    public async Task AnotherTenantsInvoice_Returns404()
    {
        var (_, _, _, invoice) = await SeedAsync();
        await TestData.EnsureTenantWithUserAsync(_factory, OtherTenant, "dates-other@test.com");
        var other = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await other.LoginAsync("dates-other@test.com", TestData.Password);

        (await other.GetAsync($"/Invoices/Details/{invoice}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
