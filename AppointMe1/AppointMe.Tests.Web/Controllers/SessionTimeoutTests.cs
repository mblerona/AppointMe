using System.Net;
using AppointMe.Domain.Identity;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class SessionTimeoutTests : IClassFixture<MovableClockWebAppFactory>
{
    private readonly MovableClockWebAppFactory _factory;

    public SessionTimeoutTests(MovableClockWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, HttpResponseMessage Login)> LoginAsync(bool rememberMe)
    {
        var email = $"session-{Guid.NewGuid():N}@test.com";
        await TestData.EnsureTenantWithUserAsync(_factory, Guid.NewGuid(), email);

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostFormAsync("/Identity/Account/Login", "/Identity/Account/Login", new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = TestData.Password,
            ["Input.RememberMe"] = rememberMe ? "true" : "false"
        });
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);

        return (client, login);
    }

    private static async Task ShouldBeLoggedInAsync(HttpClient client) =>
        (await client.GetAsync("/Customers")).StatusCode.Should().Be(HttpStatusCode.OK);

    private static async Task ShouldBeLoggedOutAsync(HttpClient client)
    {
        var response = await client.GetAsync("/Customers");

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Identity/Account/Login");
    }

    private static string AuthCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(".AspNetCore.Identity.Application="));

    [Fact]
    public async Task NormalLogin_EndsAfterAnHourWithoutActivity()
    {
        var (client, _) = await LoginAsync(rememberMe: false);
        await ShouldBeLoggedInAsync(client);

        _factory.Clock.Advance(TimeSpan.FromMinutes(61));

        await ShouldBeLoggedOutAsync(client);
    }

    [Fact]
    public async Task NormalLogin_StaysAliveWhileItIsBeingUsed()
    {
        var (client, _) = await LoginAsync(rememberMe: false);

        // Two hours in total, but never more than 40 minutes between requests.
        for (var i = 0; i < 3; i++)
        {
            _factory.Clock.Advance(TimeSpan.FromMinutes(40));
            await ShouldBeLoggedInAsync(client);
        }
    }

    [Fact]
    public async Task NormalLogin_CookieIsDroppedWhenTheBrowserCloses()
    {
        var (_, login) = await LoginAsync(rememberMe: false);

        AuthCookie(login).Should().NotContainEquivalentOf("expires=");
    }

    [Fact]
    public async Task RememberMe_SurvivesAnHourOfInactivity_ButEndsAfterTwoWeeks()
    {
        var (client, _) = await LoginAsync(rememberMe: true);

        _factory.Clock.Advance(TimeSpan.FromMinutes(61));
        await ShouldBeLoggedInAsync(client);

        _factory.Clock.Advance(TimeSpan.FromDays(15));
        await ShouldBeLoggedOutAsync(client);
    }

    [Fact]
    public async Task RememberMe_CookieIsKeptForTwoWeeks()
    {
        var before = _factory.Clock.GetUtcNow();
        var (_, login) = await LoginAsync(rememberMe: true);

        var expires = AuthCookie(login).Split(';').Select(p => p.Trim())
            .Single(p => p.StartsWith("expires=", StringComparison.OrdinalIgnoreCase))["expires=".Length..];

        DateTimeOffset.Parse(expires).Should().BeCloseTo(before.AddDays(14), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task SecurityStampCheck_StillRuns_SoAChangedStampEndsTheSession()
    {
        var email = $"stamp-{Guid.NewGuid():N}@test.com";
        await TestData.EnsureTenantWithUserAsync(_factory, Guid.NewGuid(), email);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(email, TestData.Password);
        await ShouldBeLoggedInAsync(client);

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            await users.UpdateSecurityStampAsync((await users.FindByEmailAsync(email))!);
        }

        _factory.Clock.Advance(TimeSpan.FromMinutes(31));

        await ShouldBeLoggedOutAsync(client);
    }
}
