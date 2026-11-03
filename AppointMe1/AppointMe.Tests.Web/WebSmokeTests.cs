using System.Net;
using Xunit;
using AppointMe.Tests.Web.Infrastructure;

public class WebSmokeTests : IClassFixture<NoAuthWebAppFactory>
{
    // NoAuthWebAppFactory (in-memory database): the plain WebApplicationFactory<Program> would use the
    // real appsettings and connection string.
    private readonly NoAuthWebAppFactory _factory;

    public WebSmokeTests(NoAuthWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HomePage_Returns200()
    {
        var client = _factory.CreateClient();
        var res = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }
}