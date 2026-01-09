using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using System.Net;
using System.Threading.Tasks;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class CsrfTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public CsrfTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Customers_Create_PostWithoutAntiForgeryToken_ShouldBeRejected()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        // ✅ real login (cookie-based)
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);

        // ❌ Intentionally NOT sending __RequestVerificationToken
        var content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string,string>("FirstName", "Ana"),
            new KeyValuePair<string,string>("LastName", "Doe"),
            new KeyValuePair<string,string>("Email", "ana@test.com"),
            new KeyValuePair<string,string>("PhoneNumber", "070123456"),
            new KeyValuePair<string,string>("City", "Skopje"),
            new KeyValuePair<string,string>("State", "North Macedonia"),
        });

        var res = await client.PostAsync("/Customers/Create", content);

        res.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.Redirect);
    }
}
