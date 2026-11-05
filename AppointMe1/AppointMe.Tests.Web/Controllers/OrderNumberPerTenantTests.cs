using System.Net;
using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>Audit S3: order numbers are unique per tenant, not across all tenants.</summary>
public class OrderNumberPerTenantTests : IClassFixture<HolidaysWebAppFactory>
{
    private static readonly Guid TenantA = Guid.Parse("0de40000-0000-0000-0000-00000000000a");
    private static readonly Guid TenantB = Guid.Parse("0de40000-0000-0000-0000-00000000000b");
    private const string SharedOrderNumber = "ORD-SHARED-1";

    private readonly HolidaysWebAppFactory _factory;

    public OrderNumberPerTenantTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    private static DateTime FutureWorkingSlot(int daysAhead, int hour)
    {
        var d = DateTime.Today.AddDays(daysAhead);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
               || (d.Month == 1 && d.Day == 1) || (d.Month == 5 && d.Day == 1))
            d = d.AddDays(1);
        return d.AddHours(hour);
    }

    private Task<Guid> AddCustomerAsync(Guid tenantId) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var c = new Customer
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerNumber = Random.Shared.Next(1, int.MaxValue),
                FirstName = "Order", LastName = "Test", Email = $"{Guid.NewGuid():N}@test.com",
                PhoneNumber = "+38970123456", City = "Skopje", State = "North Macedonia"
            };
            db.Customers.Add(c);
            await db.SaveChangesAsync();
            return c.Id;
        });

    private static Dictionary<string, string> Form(Guid customerId, DateTime when) => new()
    {
        ["CustomerId"] = customerId.ToString(),
        ["OrderNumber"] = SharedOrderNumber,
        ["AppointmentDate"] = when.ToString("yyyy-MM-ddTHH:mm"),
        ["Description"] = "Order number test"
    };

    [Fact]
    public async Task AnotherTenantsOrderNumber_CanBeReused_ButNotTwiceInTheSameTenant()
    {
        await TestData.EnsureTenantWithUserAsync(_factory, TenantA, "orders-a@test.com");
        await TestData.EnsureTenantWithUserAsync(_factory, TenantB, "orders-b@test.com");
        var customerA = await AddCustomerAsync(TenantA);
        var customerB = await AddCustomerAsync(TenantB);
        await TestData.WithDbAsync(_factory, async db =>
        {
            db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(), TenantId = TenantA, CustomerId = customerA, OrderNumber = SharedOrderNumber,
                AppointmentDate = FutureWorkingSlot(20, 10), Description = "Tenant A", Status = AppointmentStatus.Scheduled
            });
            return await db.SaveChangesAsync();
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync("orders-b@test.com", TestData.Password);

        var first = await client.PostFormAsync("/Appointments/Create", "/Appointments/Create",
            Form(customerB, FutureWorkingSlot(21, 10)));
        first.StatusCode.Should().Be(HttpStatusCode.Redirect, "tenant A's order number doesn't block tenant B");

        var second = await client.PostFormAsync("/Appointments/Create", "/Appointments/Create",
            Form(customerB, FutureWorkingSlot(21, 14)));
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Contain("Order number already exists");

        (await TestData.WithDbAsync(_factory, db => db.Appointments.CountAsync(a => a.OrderNumber == SharedOrderNumber)))
            .Should().Be(2, "one in each tenant");
    }
}
