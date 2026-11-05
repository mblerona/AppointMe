using System.Net;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class AdminAccessTests : IClassFixture<PlatformAdminWebAppFactory>
{
    private const string AdminEmail = PlatformAdminWebAppFactory.AdminEmail;
    private const string AdminPassword = PlatformAdminWebAppFactory.AdminPassword;

    private readonly PlatformAdminWebAppFactory _factory;

    public AdminAccessTests(PlatformAdminWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = NewClient();
        await client.LoginAsync(AdminEmail, AdminPassword);
        return client;
    }

    private async Task<HttpClient> TenantClientAsync()
    {
        var client = NewClient();
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);
        return client;
    }

    // ---------------- who can reach /Admin ----------------

    [Theory]
    [InlineData("/Admin")]
    [InlineData("/Admin/Home")]
    [InlineData("/Admin/Home/Index")]
    public async Task Admin_CanReachAdmin(string url)
    {
        var client = await AdminClientAsync();

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Platform administration");
    }

    [Fact]
    public async Task AdminPages_UseTheAdminLayout_WithoutTenantNavigation()
    {
        var client = await AdminClientAsync();

        var html = await (await client.GetAsync("/Admin")).Content.ReadAsStringAsync();

        html.Should().Contain("AppointMe Admin");
        html.Should().Contain("/Identity/Account/Logout");
        html.Should().NotContain("href=\"/Customers");
        html.Should().NotContain("href=\"/Appointments");
        html.Should().NotContain("href=\"/Settings");
    }

    [Fact]
    public async Task Tenant_IsRedirectedToAccessDenied_AndThatPageRenders()
    {
        var client = await TenantClientAsync();

        var response = await client.GetAsync("/Admin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Identity/Account/AccessDenied");

        var denied = await client.GetAsync(response.Headers.Location);
        denied.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Anonymous_IsRedirectedToLogin()
    {
        var response = await NewClient().GetAsync("/Admin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task RemovingTheRole_AppliesOnTheNextRequest_EvenThoughTheCookieStillHasIt()
    {
        const string email = "second-admin@test.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            var user = await TestData.EnsureUserAsync(users, email, tenantId: null);
            await users.AddToRoleAsync(user, PlatformRoles.Admin);
        }

        var client = NewClient();
        await client.LoginAsync(email, TestData.Password);
        (await client.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK);

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            var user = await users.FindByEmailAsync(email);
            await users.RemoveFromRoleAsync(user!, PlatformRoles.Admin);
        }

        var response = await client.GetAsync("/Admin");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Identity/Account/AccessDenied");
    }

    // ---------------- where login lands ----------------

    [Fact]
    public async Task AdminLogin_LandsOnAdmin()
    {
        var response = await NewClient().PostLoginAsync(AdminEmail, AdminPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Admin");
    }

    [Theory]
    [InlineData("/Customers", "/Admin")]
    [InlineData("/Administrator", "/Admin")]
    [InlineData("/Admin/Home", "/Admin/Home")]
    public async Task AdminLogin_KeepsAReturnUrlOnlyInsideAdmin(string returnUrl, string expected)
    {
        var client = NewClient();

        var response = await client.PostFormAsync(
            "/Identity/Account/Login",
            $"/Identity/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}",
            new Dictionary<string, string>
            {
                ["Input.Email"] = AdminEmail,
                ["Input.Password"] = AdminPassword
            });

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be(expected);
    }

    [Fact]
    public async Task TenantLogin_StillLandsOnTheHomePath()
    {
        var response = await NewClient().PostLoginAsync(
            CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/");
    }

    [Fact]
    public async Task AdminLogin_WithWrongPassword_GetsOnlyInvalidLoginAttempt()
    {
        const string email = "wrong-password-admin@test.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            var user = await TestData.EnsureUserAsync(users, email, tenantId: null);
            await users.AddToRoleAsync(user, PlatformRoles.Admin);
        }

        var response = await NewClient().PostLoginAsync(email, "Wrong123!Aa");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Invalid login attempt");
    }

    // ---------------- admins in tenant code ----------------

    [Theory]
    [InlineData("/")]
    [InlineData("/Home")]
    [InlineData("/Dashboard")]
    [InlineData("/Customers")]
    [InlineData("/Appointments")]
    [InlineData("/Appointments/Calendar")]
    [InlineData("/Services")]
    [InlineData("/Settings/Business")]
    public async Task Admin_VisitingATenantPage_IsSentToAdmin(string url)
    {
        var client = await AdminClientAsync();

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Admin");
    }

    [Fact]
    public async Task Admin_CannotOpenAnotherTenantsInvoiceById()
    {
        var client = await AdminClientAsync();

        var response = await client.GetAsync($"/Invoices/Details/{CustomWebAppFactory.SeededInvoiceId}");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Admin");
    }

    [Fact]
    public async Task Admin_PostingBusinessSettings_ChangesNothing_AndIsSentToAdmin()
    {
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync("/Admin", "/Settings/Business", new Dictionary<string, string>
        {
            ["Id"] = CustomWebAppFactory.DefaultTenantId.ToString(),
            ["Name"] = "Changed by admin"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be("/Admin");

        var name = await TestData.WithDbAsync(_factory, db =>
            db.Businesses.Where(b => b.Id == CustomWebAppFactory.DefaultTenantId).Select(b => b.Name).SingleAsync());
        name.Should().Be("Test Business");
    }

    [Fact]
    public async Task AdminLogin_AndBrowsing_NeverCreateABusiness()
    {
        var before = await TestData.WithDbAsync(_factory, db => db.Businesses.CountAsync());

        var client = await AdminClientAsync();
        foreach (var url in new[] { "/", "/Home", "/Dashboard", "/Settings/Business", "/Admin" })
            await client.GetAsync(url);

        var after = await TestData.WithDbAsync(_factory, db => db.Businesses.CountAsync());
        after.Should().Be(before);

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        (await users.FindByEmailAsync(AdminEmail))!.TenantId.Should().BeNull();
    }
}
