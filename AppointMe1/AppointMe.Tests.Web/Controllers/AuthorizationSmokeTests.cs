using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class AuthorizationSmokeTests : IClassFixture<NoAuthWebAppFactory>
{
    private readonly NoAuthWebAppFactory _factory;

    public AuthorizationSmokeTests(NoAuthWebAppFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/Dashboard")]
    [InlineData("/Customers")]
    [InlineData("/Appointments")]
    [InlineData("/Invoices")]
    [InlineData("/Services")]
    [InlineData("/Settings/Business")]
    public async Task ProtectedPages_WhenNotAuthenticated_ShouldRedirectToLogin(string url)
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var res = await client.GetAsync(url);

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Contain("/Identity/Account/Login");
    }
}
