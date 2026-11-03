using System.Net;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// RequireTenantFilter on BaseTenantController: a logged-in user without a usable tenant is sent to
/// Settings/Business before any tenant action runs.
/// </summary>
public class TenantRedirectTests : IClassFixture<CustomWebAppFactory>
{
    private const string NoTenantEmail = "no-tenant@test.com";
    private const string MissingBusinessEmail = "missing-business@test.com";

    private readonly CustomWebAppFactory _factory;

    public TenantRedirectTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsUserAsync(string email, Guid? tenantId)
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            await TestData.EnsureUserAsync(userMgr, email, tenantId);
        }

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(email, TestData.Password);
        return client;
    }

    [Theory]
    [InlineData("/Dashboard")]
    [InlineData("/Customers")]
    [InlineData("/Appointments")]
    [InlineData("/Appointments/Calendar")]
    [InlineData("/Services")]
    [InlineData("/Invoices")]
    public async Task UserWithoutTenant_IsRedirectedToBusinessSettings(string url)
    {
        var client = await LoginAsUserAsync(NoTenantEmail, tenantId: null);

        var res = await client.GetAsync(url);

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Contain("/Settings/Business");
    }

    [Fact]
    public async Task UserWhoseBusinessIsMissing_IsRedirectedToBusinessSettings()
    {
        var client = await LoginAsUserAsync(MissingBusinessEmail, tenantId: Guid.NewGuid());

        var res = await client.GetAsync("/Customers");

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Contain("/Settings/Business");
    }

    [Fact]
    public async Task BusinessSettings_ForUserWithoutBusiness_ShowsAnError_AndCreatesNoBusiness()
    {
        var client = await LoginAsUserAsync(NoTenantEmail, tenantId: null);
        var before = await TestData.WithDbAsync(_factory, (ApplicationDbContext db) => db.Businesses.CountAsync());

        var res = await client.GetAsync("/Settings/Business");

        res.StatusCode.Should().Be(HttpStatusCode.OK, "no redirect, so the tenant filter can't loop");
        (await res.Content.ReadAsStringAsync()).Should().Contain("isn't linked to a business");
        (await TestData.WithDbAsync(_factory, (ApplicationDbContext db) => db.Businesses.CountAsync())).Should().Be(before);

        using var scope = _factory.Services.CreateScope();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        (await userMgr.FindByEmailAsync(NoTenantEmail))!.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task UserWithTenant_IsNotRedirected()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);

        var res = await client.GetAsync("/Customers");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
