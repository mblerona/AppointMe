using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>A new Business has no colors saved until its owner chooses some.</summary>
public class DefaultColorsTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public DefaultColorsTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> NewTenantClientAsync(string? navbar = null, string? background = null)
    {
        var tenantId = Guid.NewGuid();
        var email = $"colors-{tenantId:N}@test.com";
        await TestData.EnsureTenantWithUserAsync(_factory, tenantId, email);

        await TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.SingleAsync(b => b.Id == tenantId);
            business.PrimaryColor = navbar;
            business.SecondaryColor = background;
            return await db.SaveChangesAsync();
        });

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(email, TestData.Password);
        return client;
    }

    [Fact]
    public async Task NewBusiness_GetsTheDefaultColorsInTheLayout()
    {
        var client = await NewTenantClientAsync();

        var html = await (await client.GetAsync("/Settings/Business")).Content.ReadAsStringAsync();

        html.Should().Contain($"--biz-navbar: {BusinessBrandingDefaults.NavbarColor}");
        html.Should().Contain($"--biz-bg: {BusinessBrandingDefaults.BackgroundColor}");
        html.Should().Contain("--biz-nav-text: #ffffff", "the navbar is dark");
        html.Should().Contain("--biz-text: #000000", "the background is light");
    }

    [Fact]
    public async Task BusinessNameInTheNavbar_UsesTheNavbarTextColor_NotThePageTextColor()
    {
        var client = await NewTenantClientAsync();

        var html = await (await client.GetAsync("/Settings/Business")).Content.ReadAsStringAsync();

        html.Should().MatchRegex(@"\.topbar span\s*\{\s*color: var\(--biz-nav-text\) !important;");
    }

    [Fact]
    public async Task SettingsForm_OffersTheDefaults_SoSavingCantStoreBlack()
    {
        var client = await NewTenantClientAsync();

        var html = await (await client.GetAsync("/Settings/Business")).Content.ReadAsStringAsync();

        html.Should().MatchRegex($@"type=""color""[^>]*value=""{BusinessBrandingDefaults.NavbarColor}""");
        html.Should().MatchRegex($@"type=""color""[^>]*value=""{BusinessBrandingDefaults.BackgroundColor}""");
    }

    [Fact]
    public async Task ColorsTheOwnerChose_AreKept()
    {
        var client = await NewTenantClientAsync(navbar: "#112233", background: "#ddeeff");

        var html = await (await client.GetAsync("/Settings/Business")).Content.ReadAsStringAsync();

        html.Should().Contain("--biz-navbar: #112233");
        html.Should().Contain("--biz-bg: #ddeeff");
        html.Should().MatchRegex(@"type=""color""[^>]*value=""#112233""");
        html.Should().NotContain(BusinessBrandingDefaults.NavbarColor);
    }
}
