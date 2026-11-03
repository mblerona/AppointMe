using System.Net;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// The layout and the appointment/customer views get the Business through CurrentBusiness
/// (no DbContext in views). Each tenant sees its own name, colors and feature switches.
/// </summary>
public class LayoutBusinessTests : IClassFixture<HolidaysWebAppFactory>
{
    private static readonly Guid TenantWithServices = Guid.Parse("1a7e0000-0000-0000-0000-000000000001");
    private static readonly Guid TenantWithoutServices = Guid.Parse("1a7e0000-0000-0000-0000-000000000002");

    private readonly HolidaysWebAppFactory _factory;

    public LayoutBusinessTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsync(Guid tenantId, string email, bool enableServices)
    {
        await TestData.EnsureTenantWithUserAsync(_factory, tenantId, email);
        await TestData.WithDbAsync(_factory, async db =>
        {
            var b = await db.Businesses.SingleAsync(x => x.Id == tenantId);
            b.EnableServices = enableServices;
            b.PrimaryColor = "#123456";
            await db.SaveChangesAsync();
            return 0;
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(email, TestData.Password);
        return client;
    }

    [Fact]
    public async Task Layout_ShowsTheTenantsNameAndColor_AndTheServicesLink()
    {
        var client = await LoginAsync(TenantWithServices, "layout-services@test.com", enableServices: true);

        var html = await (await client.GetAsync("/Customers")).Content.ReadAsStringAsync();

        html.Should().Contain("Biz layout-services@test.com");
        html.Should().Contain("--biz-navbar: #123456");
        html.Should().Contain("href=\"/Services\"");
    }

    [Fact]
    public async Task Layout_HidesTheServicesLink_WhenServicesAreOff()
    {
        var client = await LoginAsync(TenantWithoutServices, "layout-noservices@test.com", enableServices: false);

        var html = await (await client.GetAsync("/Customers")).Content.ReadAsStringAsync();

        html.Should().Contain("Biz layout-noservices@test.com");
        html.Should().NotContain("href=\"/Services\"");
    }

    [Theory]
    [InlineData("/Appointments")]
    [InlineData("/Appointments/Create")]
    public async Task AppointmentViews_RenderForBothFeatureSettings(string url)
    {
        var withServices = await LoginAsync(TenantWithServices, "layout-services@test.com", enableServices: true);
        var withoutServices = await LoginAsync(TenantWithoutServices, "layout-noservices@test.com", enableServices: false);

        (await withServices.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await withoutServices.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Layout_RendersForAnonymousUsers_WithTheDefaultBranding()
    {
        // Privacy uses _Layout (the login pages use _AuthLayout).
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var res = await client.GetAsync("/Home/Privacy");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("--biz-navbar: #212529");
    }
}
