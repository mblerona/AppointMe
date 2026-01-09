using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class TenantIsolationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public TenantIsolationTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Customers_Details_ShouldNotReturnOtherTenantCustomer()
    {
        var tenantA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var tenantB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var userAEmail = "userA@test.local";
        var userBEmail = "userB@test.local";
        const string password = "Test123!Aa";

        Guid customerId;

        // Seed: businesses, customer (Tenant A), users (Tenant A and Tenant B)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();

            // Tenants (Businesses)
            db.Businesses.Add(new Business
            {
                Id = tenantA,
                Name = "Biz A",
                Address = "A",
                EnableInvoices = true,
                EnableServices = true,
                DefaultSlotMinutes = 30,
                WorkDayStart = new TimeSpan(9, 0, 0),
                WorkDayEnd = new TimeSpan(17, 0, 0),
                OpenMon = true,
                OpenTue = true,
                OpenWed = true,
                OpenThu = true,
                OpenFri = true
            });

            db.Businesses.Add(new Business
            {
                Id = tenantB,
                Name = "Biz B",
                Address = "B",
                EnableInvoices = true,
                EnableServices = true,
                DefaultSlotMinutes = 30,
                WorkDayStart = new TimeSpan(9, 0, 0),
                WorkDayEnd = new TimeSpan(17, 0, 0),
                OpenMon = true,
                OpenTue = true,
                OpenWed = true,
                OpenThu = true,
                OpenFri = true
            });

            // Customer belongs to Tenant A
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                FirstName = "TenantA",
                LastName = "Customer",
                Email = "a@test.com",
                PhoneNumber = "070000000",
                City = "Skopje",
                State = "North Macedonia",
                CustomerNumber = 1
            };
            customerId = customer.Id;

            db.Customers.Add(customer);
            await db.SaveChangesAsync();

            // Two users: one per tenant
            await EnsureUserAsync(userMgr, userAEmail, password, tenantA);
            await EnsureUserAsync(userMgr, userBEmail, password, tenantB);
        }

        // Act: login as Tenant B and try to view Tenant A customer
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        await client.LoginAsync(userBEmail, password);

        var res = await client.GetAsync($"/Customers/Details/{customerId}");

        // Your controller returns NotFound if customer not in tenant (it catches KeyNotFoundException)
        res.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Redirect);

    }

    private static async Task EnsureUserAsync(
        UserManager<AppointMeAppUser> userMgr,
        string email,
        string password,
        Guid tenantId)
    {
        var user = await userMgr.FindByEmailAsync(email);

        if (user == null)
        {
            user = new AppointMeAppUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = email,
                Email = email,
                FirstName = "Test",
                LastName = "User",
                TenantId = tenantId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                EmailConfirmed = true
            };

            var result = await userMgr.CreateAsync(user, password);
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }
        else
        {
            var changed = false;
            if (user.TenantId != tenantId) { user.TenantId = tenantId; changed = true; }
            if (!user.EmailConfirmed) { user.EmailConfirmed = true; changed = true; }
            if (string.IsNullOrWhiteSpace(user.FirstName)) { user.FirstName = "Test"; changed = true; }
            if (string.IsNullOrWhiteSpace(user.LastName)) { user.LastName = "User"; changed = true; }

            if (changed)
            {
                user.UpdatedAt = DateTime.UtcNow;
                await userMgr.UpdateAsync(user);
            }
        }
    }
}
