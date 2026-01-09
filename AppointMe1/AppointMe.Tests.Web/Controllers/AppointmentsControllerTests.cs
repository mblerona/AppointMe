using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class AppointmentsControllerTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AppointmentsControllerTests(CustomWebAppFactory factory)
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

    [Fact]
    public async Task Get_Appointments_Index_ShouldReturn200_ForAuthenticatedUser()
    {
        var client = await CreateLoggedInClientAsync();

        var res = await client.GetAsync("/Appointments");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_Appointments_Calendar_ShouldReturn200_ForAuthenticatedUser()
    {
        var client = await CreateLoggedInClientAsync();

        var res = await client.GetAsync("/Appointments/Calendar");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
