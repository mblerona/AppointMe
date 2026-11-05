using System.Net;
using System.Text.RegularExpressions;
using AppointMe.Domain.Identity;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// Written against the framework's built-in page and kept for our replacement, so the behaviour stays the same:
/// no account enumeration, nothing for unconfirmed accounts, and a link that really resets the password.
/// </summary>
public class ForgotPasswordTests : IClassFixture<CapturingEmailWebAppFactory>
{
    private const string Url = "/Identity/Account/ForgotPassword";
    private const string ConfirmationUrl = "/Identity/Account/ForgotPasswordConfirmation";

    private readonly CapturingEmailWebAppFactory _factory;

    public ForgotPasswordTests(CapturingEmailWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<string> AddUserAsync(bool confirmed = true)
    {
        var email = $"forgot-{Guid.NewGuid():N}@test.com";
        await TestData.EnsureTenantWithUserAsync(_factory, Guid.NewGuid(), email);
        if (!confirmed)
        {
            using var scope = _factory.Services.CreateScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            var user = await users.FindByEmailAsync(email);
            user!.EmailConfirmed = false;
            await users.UpdateAsync(user);
        }
        return email;
    }

    private Task<HttpResponseMessage> SubmitAsync(string email) =>
        NewClient().PostFormAsync(Url, Url, new Dictionary<string, string> { ["Input.Email"] = email });

    private static string LinkIn(string body) =>
        WebUtility.HtmlDecode(Regex.Match(body, @"href=['""]([^'""]+)['""]").Groups[1].Value);

    [Fact]
    public async Task AConfirmedAccount_GetsOneEmail_WithAResetLink()
    {
        var email = await AddUserAsync();

        var response = await SubmitAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain(ConfirmationUrl);
        var message = _factory.Emails.SentTo(email).Should().ContainSingle().Which;
        message.Subject.Should().Be("Reset your password");
        message.IsHtml.Should().BeTrue();
        message.Content.Should().MatchRegex(@"^Please reset your password by <a href='[^']+'>clicking here</a>\.$");
        LinkIn(message.Content).Should().Contain("/Identity/Account/ResetPassword?code=");
    }

    [Fact]
    public async Task AnUnconfirmedAccount_GetsNothing_AndTheResponseIsTheSame()
    {
        var email = await AddUserAsync(confirmed: false);

        var response = await SubmitAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain(ConfirmationUrl);
        _factory.Emails.SentTo(email).Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnknownEmail_GetsTheSameResponse()
    {
        var email = $"nobody-{Guid.NewGuid():N}@test.com";

        var response = await SubmitAsync(email);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain(ConfirmationUrl);
        _factory.Emails.SentTo(email).Should().BeEmpty();
    }

    [Fact]
    public async Task AnInvalidEmail_ShowsTheFormAgain()
    {
        var response = await SubmitAsync("not-an-email");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task TheLink_ReallyResetsThePassword()
    {
        var email = await AddUserAsync();
        await SubmitAsync(email);
        var link = new Uri(LinkIn(_factory.Emails.SentTo(email).Single().Content));
        const string newPassword = "Changed123!Aa";

        var client = NewClient();
        var page = await client.GetAsync(link.PathAndQuery);
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await page.Content.ReadAsStringAsync();
        var code = WebUtility.HtmlDecode(Regex.Match(html, @"name=""Input\.Code""[^>]*value=""([^""]*)""|value=""([^""]*)""[^>]*name=""Input\.Code""").Value);
        code = Regex.Match(code, @"value=""([^""]*)""").Groups[1].Value;

        var reset = await client.PostAsync(link.AbsolutePath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = newPassword,
            ["Input.ConfirmPassword"] = newPassword,
            ["Input.Code"] = code,
            ["__RequestVerificationToken"] = TestLoginHelper.ExtractAntiForgeryToken(html)
        }));

        reset.StatusCode.Should().Be(HttpStatusCode.Redirect);
        reset.Headers.Location!.ToString().Should().Contain("ResetPasswordConfirmation");
        await NewClient().LoginAsync(email, newPassword);
    }

    [Fact]
    public async Task ThePage_IsOpenToAnonymousUsers()
    {
        (await NewClient().GetAsync(Url)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
