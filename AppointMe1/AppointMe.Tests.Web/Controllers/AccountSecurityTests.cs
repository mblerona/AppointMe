using AppointMe.Domain.Identity;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class AccountSecurityTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public AccountSecurityTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void IdentityOptions_ShouldLockOutAfterFiveFailures_ForFifteenMinutes_AndRequireEightCharacters()
    {
        var options = _factory.Services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        options.Lockout.AllowedForNewUsers.Should().BeTrue();
        options.Lockout.MaxFailedAccessAttempts.Should().Be(5);
        options.Lockout.DefaultLockoutTimeSpan.Should().Be(TimeSpan.FromMinutes(15));
        options.Password.RequiredLength.Should().Be(8);
    }

    [Fact]
    public async Task Login_AfterFiveWrongPasswords_LocksAccount_EvenForCorrectPassword()
    {
        const string email = "lockout@test.local";
        await TestData.EnsureTenantWithUserAsync(_factory, Guid.NewGuid(), email);

        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        for (var i = 0; i < 4; i++)
        {
            var wrong = await client.PostLoginAsync(email, "Wrong123!Aa");
            wrong.StatusCode.Should().Be(HttpStatusCode.OK, "failures 1-4 re-render the login page");
        }

        var fifth = await client.PostLoginAsync(email, "Wrong123!Aa");
        fifth.StatusCode.Should().Be(HttpStatusCode.Redirect, "the 5th failure locks the account");
        fifth.Headers.Location!.ToString().Should().Contain("/Identity/Account/Lockout");

        var correct = await client.PostLoginAsync(email, TestData.Password);

        correct.StatusCode.Should().Be(HttpStatusCode.Redirect);
        correct.Headers.Location!.ToString().Should().Contain("/Identity/Account/Lockout");

        using var scope = _factory.Services.CreateScope();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        var user = await userMgr.FindByEmailAsync(email);
        (await userMgr.IsLockedOutAsync(user!)).Should().BeTrue();
    }

    [Fact]
    public async Task Register_WithSevenCharacterPassword_IsRejected_AndCreatesNoUser()
    {
        const string email = "shortpwd@test.local";
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var res = await client.PostFormAsync("/Identity/Account/Register", "/Identity/Account/Register",
            new Dictionary<string, string>
            {
                ["Input.FirstName"] = "Short",
                ["Input.LastName"] = "Password",
                ["Input.Email"] = email,
                ["Input.Password"] = "Abc123x",
                ["Input.ConfirmPassword"] = "Abc123x"
            });

        res.StatusCode.Should().Be(HttpStatusCode.OK, "an invalid form re-renders the page instead of redirecting");

        using var scope = _factory.Services.CreateScope();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        (await userMgr.FindByEmailAsync(email)).Should().BeNull();
    }
}
