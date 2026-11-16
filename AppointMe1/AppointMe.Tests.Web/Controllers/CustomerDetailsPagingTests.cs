using System.Net;
using System.Text.RegularExpressions;
using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>The customer's page: New Appointment sits above the appointment list, which shows 15 rows per page.</summary>
public class CustomerDetailsPagingTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public CustomerDetailsPagingTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoggedInClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);
        return client;
    }

    /// <summary>A customer of the default tenant with this many appointments; order numbers are PREFIX-01 (oldest) upwards.</summary>
    private Task<Guid> CustomerWithAppointmentsAsync(string prefix, int count) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var tenantId = CustomWebAppFactory.DefaultTenantId;
            var customer = new Customer
            {
                Id = Guid.NewGuid(), TenantId = tenantId, FirstName = "Paging", LastName = prefix,
                Email = $"{prefix.ToLowerInvariant()}@paging.test", PhoneNumber = "+38970123456",
                CustomerNumber = 1000 + Math.Abs(prefix.GetHashCode() % 1000)
            };
            db.Customers.Add(customer);

            var start = new DateTime(2027, 3, 1, 10, 0, 0);
            for (var i = 1; i <= count; i++)
            {
                db.Appointments.Add(new Appointment
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customer.Id,
                    OrderNumber = $"{prefix}-{i:00}", Description = "Paging", DurationMinutes = 30,
                    AppointmentDate = start.AddDays(i)
                });
            }

            await db.SaveChangesAsync();
            return customer.Id;
        });

    private static List<string> OrderNumbers(string html, string prefix) =>
        Regex.Matches(html, $@">({Regex.Escape(prefix)}-\d\d)<").Select(m => m.Groups[1].Value).ToList();

    [Fact]
    public async Task NewAppointmentButton_IsInTheAppointmentListHeader_Once()
    {
        var id = await CustomerWithAppointmentsAsync("BTN", 1);
        var client = await LoggedInClientAsync();

        var html = await (await client.GetAsync($"/Customers/Details/{id}")).Content.ReadAsStringAsync();

        Regex.Matches(html, @"/Appointments/Create\?customerId=").Count.Should().Be(1);
        var list = html[html.IndexOf("id=\"appointments\"", StringComparison.Ordinal)..];
        list.IndexOf("id=\"new-appointment\"", StringComparison.Ordinal)
            .Should().BeLessThan(list.IndexOf("<table", StringComparison.Ordinal), "the button is above the list");
    }

    [Fact]
    public async Task WithoutAppointments_TheButtonIsStillThere_AndThereIsNoPager()
    {
        var id = await CustomerWithAppointmentsAsync("NONE", 0);
        var client = await LoggedInClientAsync();

        var html = await (await client.GetAsync($"/Customers/Details/{id}")).Content.ReadAsStringAsync();

        html.Should().Contain("id=\"new-appointment\"").And.Contain("No appointments yet");
        html.Should().NotContain("aria-label=\"Appointment pages\"");
    }

    [Fact]
    public async Task FifteenOrFewer_ShowsThemAll_WithoutAPager()
    {
        var id = await CustomerWithAppointmentsAsync("FIT", 15);
        var client = await LoggedInClientAsync();

        var html = await (await client.GetAsync($"/Customers/Details/{id}")).Content.ReadAsStringAsync();

        OrderNumbers(html, "FIT").Should().HaveCount(15);
        html.Should().NotContain("aria-label=\"Appointment pages\"");
    }

    [Fact]
    public async Task MoreThanFifteen_ArePaged_NewestFirst()
    {
        var id = await CustomerWithAppointmentsAsync("PG", 32);
        var client = await LoggedInClientAsync();

        var first = await (await client.GetAsync($"/Customers/Details/{id}")).Content.ReadAsStringAsync();
        var numbers = OrderNumbers(first, "PG");
        numbers.Should().HaveCount(15);
        numbers[0].Should().Be("PG-32");
        first.Should().Contain("Showing 1–15 of 32");
        first.Should().Contain($"href=\"/Customers/Details/{id}?page=2#appointments\"");
        first.Should().Contain($"href=\"/Customers/Details/{id}?page=3#appointments\"");

        var third = await (await client.GetAsync($"/Customers/Details/{id}?page=3")).Content.ReadAsStringAsync();
        OrderNumbers(third, "PG").Should().Equal("PG-02", "PG-01");
        third.Should().Contain("Showing 31–32 of 32");
        third.Should().Contain("32 in total", "the statistics still count every appointment");
    }

    [Fact]
    public async Task APageBeyondTheLast_ShowsTheLastPage()
    {
        var id = await CustomerWithAppointmentsAsync("END", 20);
        var client = await LoggedInClientAsync();

        var response = await client.GetAsync($"/Customers/Details/{id}?page=50");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        OrderNumbers(await response.Content.ReadAsStringAsync(), "END").Should().HaveCount(5);
    }

    [Fact]
    public async Task AnotherTenantsCustomer_Is404_OnAnyPage()
    {
        var otherTenant = Guid.NewGuid();
        await TestData.EnsureTenantWithUserAsync(_factory, otherTenant, $"paging-other-{otherTenant:N}@test.com");
        var otherCustomer = await TestData.WithDbAsync(_factory, async db =>
        {
            var c = new Customer
            {
                Id = Guid.NewGuid(), TenantId = otherTenant, FirstName = "Other", LastName = "Tenant",
                Email = $"other-{otherTenant:N}@test.com", PhoneNumber = "+38970123456", CustomerNumber = 1
            };
            db.Customers.Add(c);
            await db.SaveChangesAsync();
            return c.Id;
        });
        var client = await LoggedInClientAsync();

        (await client.GetAsync($"/Customers/Details/{otherCustomer}?page=2")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
