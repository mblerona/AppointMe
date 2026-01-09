using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class HomeControllerAuthTests : IClassFixture<NoAuthWebAppFactory>
{
    private readonly NoAuthWebAppFactory _factory;

    public HomeControllerAuthTests(NoAuthWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Home_Index_WhenNotAuthenticated_ShouldRedirectToIdentityLogin()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var res = await client.GetAsync("/");

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Contain("/Identity/Account/Login");
    }
}
