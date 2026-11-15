using System.Net;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// Login, register, forgot password and resend confirmation share a per-IP limit on their posts.
/// One test, because the tests of a factory share one limiter and all requests come from the same address.
/// </summary>
public class RateLimitingTests : IClassFixture<RateLimitedWebAppFactory>
{
    private readonly RateLimitedWebAppFactory _factory;

    public RateLimitingTests(RateLimitedWebAppFactory factory)
    {
        _factory = factory;
    }

    private static Dictionary<string, string> WrongLogin() => new()
    {
        ["Input.Email"] = "nobody-rate-limit@test.com",
        ["Input.Password"] = "Wrong123!Aa"
    };

    [Fact]
    public async Task AfterTheLimit_FurtherPostsGetTheFriendlyTooManyAttemptsPage_ButTheFormsCanStillBeOpened()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string login = "/Identity/Account/Login";

        // Within the limit: the normal answer (the form again, with the generic error).
        for (var i = 0; i < RateLimitedWebAppFactory.PermitLimit; i++)
            (await client.PostFormAsync(login, login, WrongLogin())).StatusCode.Should().Be(HttpStatusCode.OK, $"attempt {i + 1}");

        var limited = await client.PostFormAsync(login, login, WrongLogin());

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        var html = await limited.Content.ReadAsStringAsync();
        html.Should().Contain("Too many attempts").And.Contain("wait a minute");
        html.Should().NotContainAny("Exception", "RateLimit", "nobody-rate-limit");
        limited.Headers.Contains("Retry-After").Should().BeTrue();

        // The limit is per IP for all four forms, so the others are limited too...
        (await client.PostFormAsync("/Identity/Account/ForgotPassword", "/Identity/Account/ForgotPassword",
            new Dictionary<string, string> { ["Input.Email"] = "nobody-rate-limit@test.com" })).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await client.PostFormAsync("/Identity/Account/ResendEmailConfirmation", "/Identity/Account/ResendEmailConfirmation",
            new Dictionary<string, string> { ["Input.Email"] = "nobody-rate-limit@test.com" })).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await RegistrationTests.RegisterAsync(client, "rate-limit-register@test.com")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        // ...but opening the forms is never limited, and neither is the rest of the app.
        foreach (var url in new[] { login, "/Identity/Account/Register", "/Identity/Account/ForgotPassword", "/Identity/Account/ResendEmailConfirmation" })
            (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK, url);
    }
}

/// <summary>In the Testing environment the limit is off unless a test switches it on, so the other tests are never throttled.</summary>
public class RateLimitingOffInTestingTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public RateLimitingOffInTestingTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ManyLoginPosts_AreNeverRateLimited()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        const string login = "/Identity/Account/Login";

        for (var i = 0; i < 15; i++)
        {
            var response = await client.PostFormAsync(login, login, new Dictionary<string, string>
            {
                ["Input.Email"] = $"nobody-{i}@test.com",
                ["Input.Password"] = "Wrong123!Aa"
            });
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"attempt {i + 1}");
        }
    }
}
