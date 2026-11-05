using System.Net;
using System.Text.RegularExpressions;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AppointMe.Web.Areas.Identity.Pages.Account;
using AppointMe.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// Registration creates the Business together with the user; the account must be confirmed by email
/// before it can log in.
/// </summary>
public class RegistrationTests : IClassFixture<CapturingEmailWebAppFactory>
{
    private readonly CapturingEmailWebAppFactory _factory;

    public RegistrationTests(CapturingEmailWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    internal static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client, string email, string businessName = "  Bella Salon  ", string password = TestData.Password) =>
        client.PostFormAsync("/Identity/Account/Register", "/Identity/Account/Register",
            new Dictionary<string, string>
            {
                ["Input.BusinessName"] = businessName,
                ["Input.FirstName"] = "Ana",
                ["Input.LastName"] = "Test",
                ["Input.Email"] = email,
                ["Input.Password"] = password,
                ["Input.ConfirmPassword"] = password
            });

    private async Task<AppointMeAppUser?> FindUserAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        return await userMgr.FindByEmailAsync(email);
    }

    private Task<int> CountBusinessesAsync() =>
        TestData.WithDbAsync(_factory, (ApplicationDbContext db) => db.Businesses.CountAsync());

    private string ConfirmationLinkFor(string email)
    {
        var mail = _factory.Emails.SentTo(email).Should().ContainSingle().Subject;
        mail.Subject.Should().Be(AccountConfirmationEmail.Subject);
        mail.IsHtml.Should().BeTrue();

        var href = Regex.Match(mail.Content, "href=\"([^\"]+)\"");
        href.Success.Should().BeTrue("the email must contain the confirmation link");
        return new Uri(WebUtility.HtmlDecode(href.Groups[1].Value)).PathAndQuery;
    }

    [Fact]
    public async Task Register_CreatesExactlyOneBusiness_LinkedToTheNewUser()
    {
        const string email = "reg-one-business@test.com";
        var before = await CountBusinessesAsync();

        var res = await RegisterAsync(NewClient(), email);

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Contain("/Identity/Account/RegisterConfirmation");

        (await CountBusinessesAsync()).Should().Be(before + 1);

        var user = await FindUserAsync(email);
        user.Should().NotBeNull();
        user!.EmailConfirmed.Should().BeFalse();
        user.TenantId.Should().NotBeNull();
        user.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        var business = await TestData.WithDbAsync(_factory,
            (ApplicationDbContext db) => db.Businesses.SingleOrDefaultAsync(b => b.Id == user.TenantId));
        business.Should().NotBeNull();
        business!.Name.Should().Be("Bella Salon", "the business name is trimmed");
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_CreatesNoBusiness()
    {
        var before = await CountBusinessesAsync();

        var res = await RegisterAsync(NewClient(), CustomWebAppFactory.DefaultUserEmail);

        res.StatusCode.Should().Be(HttpStatusCode.OK, "the form is shown again with the error");
        (await res.Content.ReadAsStringAsync()).Should().Contain("is already taken");
        (await CountBusinessesAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Register_WithoutBusinessName_IsRejected_AndCreatesNothing()
    {
        const string email = "reg-no-business-name@test.com";
        var before = await CountBusinessesAsync();

        var res = await RegisterAsync(NewClient(), email, businessName: "");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await FindUserAsync(email)).Should().BeNull();
        (await CountBusinessesAsync()).Should().Be(before);
    }

    [Fact]
    public async Task ConfirmationPage_TellsTheUserToCheckTheirEmail()
    {
        const string email = "reg-check-email@test.com";
        var client = NewClient();
        var res = await RegisterAsync(client, email);

        var page = await client.GetAsync(res.Headers.Location);

        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await page.Content.ReadAsStringAsync();
        html.Should().Contain("Check your email to confirm your account");
        html.Should().NotContain("ConfirmEmail?userId", "the link must only be sent by email");
    }

    [Fact]
    public async Task UnconfirmedUser_CannotLogIn_AndIsToldToConfirm()
    {
        const string email = "reg-unconfirmed@test.com";
        var client = NewClient();
        await RegisterAsync(client, email);

        var login = await client.PostLoginAsync(email, TestData.Password);

        login.StatusCode.Should().Be(HttpStatusCode.OK, "the login form is shown again");
        var html = await login.Content.ReadAsStringAsync();
        html.Should().Contain(LoginModel.ConfirmEmailMessage);
        html.Should().Contain("ResendEmailConfirmation");

        var dashboard = await client.GetAsync("/Dashboard");
        dashboard.StatusCode.Should().Be(HttpStatusCode.Redirect);
        dashboard.Headers.Location!.ToString().Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task UnconfirmedUser_WithWrongPassword_GetsTheGenericError()
    {
        const string email = "reg-unconfirmed-wrongpwd@test.com";
        var client = NewClient();
        await RegisterAsync(client, email);

        var login = await client.PostLoginAsync(email, "Wrong123!Aa");

        var html = await login.Content.ReadAsStringAsync();
        html.Should().Contain("Invalid login attempt");
        html.Should().NotContain(LoginModel.ConfirmEmailMessage, "the hint would reveal that the account exists");
    }

    [Fact]
    public async Task ConfirmedUser_CanLogIn()
    {
        var login = await NewClient().PostLoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);

        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        login.Headers.Location!.ToString().Should().NotContain("Login");
    }

    [Fact]
    public async Task ConfirmationLink_ConfirmsTheAccount_ThenLoginWorks()
    {
        const string email = "reg-confirm-link@test.com";
        var client = NewClient();
        await RegisterAsync(client, email);

        var confirm = await client.GetAsync(ConfirmationLinkFor(email));

        confirm.StatusCode.Should().Be(HttpStatusCode.OK);
        (await confirm.Content.ReadAsStringAsync()).Should().Contain("Email confirmed");
        (await FindUserAsync(email))!.EmailConfirmed.Should().BeTrue();

        await client.LoginAsync(email, TestData.Password);
        (await client.GetAsync("/Dashboard")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConfirmationLink_WithInvalidCode_DoesNotConfirm()
    {
        const string email = "reg-bad-code@test.com";
        var client = NewClient();
        await RegisterAsync(client, email);
        var user = await FindUserAsync(email);

        var res = await client.GetAsync($"/Identity/Account/ConfirmEmail?userId={user!.Id}&code=bm90LWEtdG9rZW4");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Confirmation failed");
        (await FindUserAsync(email))!.EmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public async Task Resend_ForUnconfirmedUser_SendsANewLink()
    {
        const string email = "reg-resend@test.com";
        var client = NewClient();
        await RegisterAsync(client, email);
        _factory.Emails.SentTo(email).Should().HaveCount(1);

        var res = await client.PostFormAsync("/Identity/Account/ResendEmailConfirmation", "/Identity/Account/ResendEmailConfirmation",
            new Dictionary<string, string> { ["Input.Email"] = email });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("we&#x27;ve sent a new confirmation link");
        _factory.Emails.SentTo(email).Should().HaveCount(2);
    }

    [Theory]
    [InlineData("reg-nobody@test.com")]                 // no such account
    [InlineData(CustomWebAppFactory.DefaultUserEmail)]  // already confirmed
    public async Task Resend_ShowsTheSameMessage_AndSendsNothing_WhenThereIsNothingToConfirm(string email)
    {
        var client = NewClient();

        var res = await client.PostFormAsync("/Identity/Account/ResendEmailConfirmation", "/Identity/Account/ResendEmailConfirmation",
            new Dictionary<string, string> { ["Input.Email"] = email });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("we&#x27;ve sent a new confirmation link");
        _factory.Emails.SentTo(email).Should().BeEmpty();
    }
}

/// <summary>If the confirmation email can't be sent, the account still exists and the user is warned.</summary>
public class RegistrationEmailFailureTests : IClassFixture<FailingEmailWebAppFactory>
{
    private readonly FailingEmailWebAppFactory _factory;

    public RegistrationEmailFailureTests(FailingEmailWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_WhenEmailFails_StillCreatesUserAndBusiness_AndShowsAWarning()
    {
        const string email = "reg-email-fails@test.com";
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var res = await RegistrationTests.RegisterAsync(client, email);

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().Should().Contain("emailFailed=True");

        using (var scope = _factory.Services.CreateScope())
        {
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await userMgr.FindByEmailAsync(email);
            user.Should().NotBeNull();
            (await db.Businesses.AnyAsync(b => b.Id == user!.TenantId)).Should().BeTrue();
        }

        var page = await client.GetAsync(res.Headers.Location);
        var html = await page.Content.ReadAsStringAsync();
        html.Should().Contain("couldn't send the confirmation email");
        html.Should().NotContain(TestDoubles.ThrowingEmailService.SecretDetail);
    }
}
