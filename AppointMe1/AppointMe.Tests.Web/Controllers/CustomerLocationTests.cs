using System.Net;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>Customers have no State field any more, and City is optional.</summary>
public class CustomerLocationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public CustomerLocationTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoggedInClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);
        return client;
    }

    private static Dictionary<string, string> CustomerForm(string email, string city) => new()
    {
        ["FirstName"] = "City",
        ["LastName"] = "Test",
        ["Email"] = email,
        ["PhoneNumber"] = "070123456",
        ["City"] = city,
    };

    private Task<AppointMe.Domain.DomainModels.Customer?> FindAsync(string email) =>
        TestData.WithDbAsync(_factory, db => db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Email == email));

    [Theory]
    [InlineData("/Customers/Create")]
    [InlineData("/Customers/Edit/{id}")]
    public async Task Forms_HaveNoStateField_AndCityIsOptional(string url)
    {
        var client = await LoggedInClientAsync();
        var id = (await FindAsync("ana@test.com"))!.Id;

        var html = await (await client.GetAsync(url.Replace("{id}", id.ToString()))).Content.ReadAsStringAsync();

        html.Should().NotContain("name=\"State\"");
        html.Should().Contain("name=\"City\"");
        html.Should().NotContain("data-val-required=\"The City field is required.\"");
    }

    [Fact]
    public async Task Create_WithoutACity_SavesTheCustomer_AndTheListShowsADash()
    {
        const string email = "no-city@test.com";
        var client = await LoggedInClientAsync();

        var res = await client.PostFormAsync("/Customers/Create", "/Customers/Create", CustomerForm(email, ""));

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var stored = await FindAsync(email);
        stored.Should().NotBeNull();
        stored!.City.Should().BeNull();

        var details = await client.GetAsync($"/Customers/Details/{stored.Id}");
        details.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_TrimsTheCity()
    {
        const string email = "trim-city@test.com";
        var client = await LoggedInClientAsync();

        await client.PostFormAsync("/Customers/Create", "/Customers/Create", CustomerForm(email, "  Bitola  "));

        (await FindAsync(email))!.City.Should().Be("Bitola");
    }

    [Fact]
    public async Task Create_WithATooLongCity_ShowsTheError_AndSavesNothing()
    {
        const string email = "long-city@test.com";
        var client = await LoggedInClientAsync();

        var res = await client.PostFormAsync("/Customers/Create", "/Customers/Create", CustomerForm(email, new string('x', 101)));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("field-validation-error");
        (await FindAsync(email)).Should().BeNull();
    }

    [Fact]
    public async Task Edit_EmptyingTheCity_ClearsIt()
    {
        const string email = "clear-city@test.com";
        var client = await LoggedInClientAsync();
        await client.PostFormAsync("/Customers/Create", "/Customers/Create", CustomerForm(email, "Ohrid"));
        var id = (await FindAsync(email))!.Id;

        var res = await client.PostFormAsync($"/Customers/Edit/{id}", $"/Customers/Edit/{id}", CustomerForm(email, ""));

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await FindAsync(email))!.City.Should().BeNull();
    }

    [Fact]
    public async Task Search_MatchesTheCity()
    {
        const string email = "search-city@test.com";
        var client = await LoggedInClientAsync();
        await client.PostFormAsync("/Customers/Create", "/Customers/Create", CustomerForm(email, "Strumica"));

        var html = await (await client.GetAsync("/Customers?searchTerm=Strumica")).Content.ReadAsStringAsync();

        html.Should().Contain(email);
    }
}
