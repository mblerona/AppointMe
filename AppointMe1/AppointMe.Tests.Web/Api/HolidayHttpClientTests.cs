using AppointMe.Service.Interface;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Api;

public class HolidayHttpClientTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public HolidayHttpClientTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void HolidayService_TypedHttpClient_HasTenSecondTimeout()
    {
        // A typed client registered with AddHttpClient<TClient, TImpl> is named after TClient.
        var client = _factory.Services
            .GetRequiredService<IHttpClientFactory>()
            .CreateClient(nameof(IHolidayService));

        client.Timeout.Should().Be(TimeSpan.FromSeconds(10));
    }
}
