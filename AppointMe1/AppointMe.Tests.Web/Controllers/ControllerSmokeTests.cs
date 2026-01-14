using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class ControllerSmokeTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public ControllerSmokeTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateLoggedInClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);
        return client;
    }

    [Theory]
    [InlineData("/Dashboard")]
    [InlineData("/Customers")]
    [InlineData("/Appointments")]
    [InlineData("/Appointments/Calendar")]

    [InlineData("/Invoices/Details/22222222-2222-2222-2222-222222222222")]
    [InlineData("/Services")]
    [InlineData("/Settings/Business")]
    public async Task MainPages_ShouldReturn200(string url)
    {
        var client = await CreateLoggedInClientAsync();

        var res = await client.GetAsync(url);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Home_Index_WhenAuthenticated_ShouldReturn200OrRedirect()
    {
        var client = await CreateLoggedInClientAsync();

        var res = await client.GetAsync("/");

        res.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.Redirect);

        if (res.StatusCode == HttpStatusCode.Redirect)
            res.Headers.Location!.ToString().Should().Contain("/Dashboard");
    }
}
