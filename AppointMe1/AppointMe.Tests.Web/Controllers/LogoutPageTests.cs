using System.Net;
using System.Text.RegularExpressions;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class LogoutPageTests : IClassFixture<PlatformAdminWebAppFactory>
{
    private const string LogoutUrl = "/Identity/Account/Logout";

    private readonly PlatformAdminWebAppFactory _factory;

    public LogoutPageTests(PlatformAdminWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> TenantClientAsync()
    {
        var client = NewClient();
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);
        return client;
    }

    private static Dictionary<string, string> NoFields() => new();

    [Fact]
    public async Task LoggedOutPage_UsesTheAccountLayout_AndLinksToLogin()
    {
        var response = await NewClient().GetAsync(LogoutUrl);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("auth-card", "it must use the same layout as the login page");
        html.Should().Contain("You have logged out");
        html.Should().Contain("href=\"/Identity/Account/Login\"");
        html.Should().NotContain("class=\"navbar");
    }

    [Fact]
    public async Task LoggingOut_EndsTheSession_AndShowsTheLoggedOutPage()
    {
        var client = await TenantClientAsync();

        var post = await client.PostFormAsync(LogoutUrl, LogoutUrl, NoFields());

        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Be(LogoutUrl);

        var page = await client.GetAsync(post.Headers.Location);
        var html = await page.Content.ReadAsStringAsync();
        html.Should().Contain("You have logged out");
        html.Should().Contain("href=\"/Identity/Account/Login\"");

        var dashboard = await client.GetAsync("/Dashboard");
        dashboard.StatusCode.Should().Be(HttpStatusCode.Redirect);
        dashboard.Headers.Location!.ToString().Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task LoginLink_OpensTheLoginPage()
    {
        var response = await NewClient().GetAsync("/Identity/Account/Login");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task OpeningThePageWhileLoggedIn_DoesNotLogOut_AndOffersTheButton()
    {
        var client = await TenantClientAsync();

        var html = await (await client.GetAsync(LogoutUrl)).Content.ReadAsStringAsync();

        html.Should().NotContain("You have logged out");
        html.Should().Contain("<form");
        (await client.GetAsync("/Dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminCanLogOut_FromTheAdminLayout()
    {
        var client = NewClient();
        await client.LoginAsync(PlatformAdminWebAppFactory.AdminEmail, PlatformAdminWebAppFactory.AdminPassword);

        var post = await client.PostFormAsync(LogoutUrl, LogoutUrl, NoFields());

        post.Headers.Location!.ToString().Should().Be(LogoutUrl);
        var admin = await client.GetAsync("/Admin");
        admin.StatusCode.Should().Be(HttpStatusCode.Redirect);
        admin.Headers.Location!.ToString().Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task ConfirmationPage_OffersCancel_BackToTheStartPage()
    {
        var client = await TenantClientAsync();

        var html = await (await client.GetAsync(LogoutUrl)).Content.ReadAsStringAsync();

        html.Should().Contain("Do you want to log out");
        html.Should().MatchRegex(@"<a id=""logout-cancel"" href=""/""");
    }

    [Fact]
    public async Task TenantLayout_LinksToTheConfirmationPage_InsteadOfLoggingOutDirectly()
    {
        var client = await TenantClientAsync();

        var html = await (await client.GetAsync("/Dashboard")).Content.ReadAsStringAsync();

        LogoutButtonOpensTheConfirmation(html);
    }

    /// <summary>
    /// The button is a link (the confirmation page without JavaScript) that opens the dialog; the only logout form
    /// is inside the dialog, next to a Cancel button.
    /// </summary>
    internal static void LogoutButtonOpensTheConfirmation(string html)
    {
        Regex.Matches(html, @"<a [^>]*>").Select(m => m.Value)
            .Should().Contain(a => a.Contains($"href=\"{LogoutUrl}\"") && a.Contains("data-bs-target=\"#logout-confirm\""));

        var dialog = Regex.Match(html, @"<div class=""modal fade"" id=""logout-confirm"".*?</form>", RegexOptions.Singleline).Value;
        dialog.Should().Contain("Do you want to log out").And.Contain("data-bs-dismiss=\"modal\"").And.Contain("Cancel");
        Regex.Matches(html, $@"<form[^>]*action=""{LogoutUrl}""").Count.Should().Be(1);
        dialog.Should().MatchRegex($@"<form[^>]*action=""{LogoutUrl}""");
    }

    [Fact]
    public async Task AdminLayout_LinksToTheConfirmationPage_InsteadOfLoggingOutDirectly()
    {
        var client = NewClient();
        await client.LoginAsync(PlatformAdminWebAppFactory.AdminEmail, PlatformAdminWebAppFactory.AdminPassword);

        var html = await (await client.GetAsync("/Admin")).Content.ReadAsStringAsync();

        LogoutButtonOpensTheConfirmation(html);
    }

    [Fact]
    public async Task AdminCancel_LeadsBackToTheAdminArea_StillLoggedIn()
    {
        var client = NewClient();
        await client.LoginAsync(PlatformAdminWebAppFactory.AdminEmail, PlatformAdminWebAppFactory.AdminPassword);
        await client.GetAsync(LogoutUrl);

        var start = await client.GetAsync("/");

        start.StatusCode.Should().Be(HttpStatusCode.Redirect);
        start.Headers.Location!.ToString().Should().Contain("/Admin");
        (await client.GetAsync("/Admin")).StatusCode.Should().Be(HttpStatusCode.OK, "opening the page must not log out");
    }

    [Fact]
    public async Task LoggingOut_WithoutAnAntiforgeryToken_IsRejected()
    {
        var client = await TenantClientAsync();

        var post = await client.PostAsync(LogoutUrl, new FormUrlEncodedContent(NoFields()));

        post.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync("/Dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("https://evil.example/")]
    [InlineData("//evil.example/")]
    public async Task NonLocalReturnUrl_IsIgnored(string returnUrl)
    {
        var client = await TenantClientAsync();

        var post = await client.PostFormAsync(
            LogoutUrl, $"{LogoutUrl}?returnUrl={Uri.EscapeDataString(returnUrl)}", NoFields());

        post.StatusCode.Should().Be(HttpStatusCode.Redirect);
        post.Headers.Location!.ToString().Should().Be(LogoutUrl);
    }

    [Fact]
    public async Task LocalReturnUrl_IsFollowed()
    {
        var client = await TenantClientAsync();

        var post = await client.PostFormAsync(
            LogoutUrl, $"{LogoutUrl}?returnUrl={Uri.EscapeDataString("/Identity/Account/Login")}", NoFields());

        post.Headers.Location!.ToString().Should().Be("/Identity/Account/Login");
    }
}
