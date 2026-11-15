using System.Net;
using AppointMe.Tests.Web.Factories;
using AppointMe.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AppointMe.Tests.Web.Infrastructure;

/// <summary>
/// Every response carries the security headers (pages, static files, redirects, error and status pages),
/// and a 404 or 403 shows the friendly page with its status code kept and nothing technical in it.
/// </summary>
public class SecurityHeadersAndStatusPagesTests : IClassFixture<HolidaysWebAppFactory>
{
    private readonly HolidaysWebAppFactory _factory;

    public SecurityHeadersAndStatusPagesTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> LoggedInAsync()
    {
        var client = NewClient();
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);
        return client;
    }

    private static void ShouldHaveTheSecurityHeaders(HttpResponseMessage response, string what)
    {
        foreach (var (name, value) in SecurityHeadersMiddleware.Headers)
        {
            response.Headers.TryGetValues(name, out var values).Should().BeTrue($"{what} must send {name}");
            values!.Should().Equal([value], $"{what} must send exactly one {name}: {value}");
        }
    }

    [Theory]
    [InlineData("/Identity/Account/Login")]       // a page with a form (antiforgery would add SAMEORIGIN)
    [InlineData("/css/site.css")]                 // a static file
    [InlineData("/Customers")]                    // a redirect to the login page
    [InlineData("/Home/Status/404")]              // the friendly status page (a re-executed 404)
    public async Task AnonymousResponses_HaveTheSecurityHeaders(string url) =>
        ShouldHaveTheSecurityHeaders(await NewClient().GetAsync(url), url);

    [Fact]
    public async Task ALoggedInPageWithForms_HasTheSecurityHeaders()
    {
        var client = await LoggedInAsync();

        var response = await client.GetAsync("/Customers/Create");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldHaveTheSecurityHeaders(response, "/Customers/Create");
    }

    [Fact]
    public async Task AnUnknownPage_SendsAnAnonymousVisitorToLogin()
    {
        // The fallback policy also covers addresses that match nothing, so a visitor can't probe for pages.
        var response = await NewClient().GetAsync("/Some/Unknown/Page");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task AnUnknownPage_ShowsALoggedInUserTheFriendly404_WithoutTechnicalDetails()
    {
        var response = await (await LoggedInAsync()).GetAsync("/Some/Unknown/Page");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Page not found").And.Contain("Go to the start page");
        html.Should().NotContainAny("Exception", "Stack", "Request ID", "/Some/Unknown/Page");
    }

    [Fact]
    public async Task AnotherTenantsRecord_StillGivesExactly404_NowWithTheFriendlyPage()
    {
        var client = await LoggedInAsync();

        var response = await client.GetAsync($"/Customers/Details/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Page not found");
    }

    [Fact]
    public async Task APostThatEndsIn404_AlsoGetsTheFriendlyPage()
    {
        // Re-execution keeps the method, so the status page must accept a POST without its own token.
        var client = await LoggedInAsync();

        var response = await client.PostFormAsync("/Customers", $"/Customers/Delete/{Guid.NewGuid()}", new Dictionary<string, string>());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Page not found");
    }

    [Fact]
    public async Task Forbidden_LeadsToTheFriendlyNoAccessPage_WithARealForbiddenStatus()
    {
        // Cookie authentication answers Forbid() with a redirect to AccessDenied, which this app replaces.
        var client = await LoggedInAsync();
        var response = await client.GetAsync("/Admin");   // a tenant in the admin area
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var denied = await client.GetAsync(response.Headers.Location);

        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var html = await denied.Content.ReadAsStringAsync();
        html.Should().Contain("No access").And.Contain("Go to the start page");
        html.Should().NotContainAny("Exception", "Request ID", "ReturnUrl");
        ShouldHaveTheSecurityHeaders(denied, "AccessDenied");
    }

    [Fact]
    public async Task TheStatusPage_OpenedDirectly_IsJustA404()
    {
        var response = await NewClient().GetAsync("/Home/Status/500");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Page not found");
    }
}
