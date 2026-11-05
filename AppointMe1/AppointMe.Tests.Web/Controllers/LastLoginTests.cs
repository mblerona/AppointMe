using System.Net;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class LastLoginTests : IClassFixture<PlatformAdminWebAppFactory>
{
    private readonly PlatformAdminWebAppFactory _factory;

    public LastLoginTests(PlatformAdminWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = NewClient();
        await client.LoginAsync(PlatformAdminWebAppFactory.AdminEmail, PlatformAdminWebAppFactory.AdminPassword);
        return client;
    }

    private async Task<(Guid Id, string Email, string Name)> AddTenantAsync()
    {
        var id = Guid.NewGuid();
        var email = $"login-{id:N}@test.com";
        var name = $"Biz {id:N}";
        await TestData.EnsureTenantWithUserAsync(_factory, id, email);
        await TestData.WithDbAsync(_factory, async db =>
        {
            (await db.Businesses.SingleAsync(b => b.Id == id)).Name = name;
            return await db.SaveChangesAsync();
        });
        return (id, email, name);
    }

    private Task SuspendAsync(Guid id, BusinessStatus status = BusinessStatus.Suspended) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.SingleAsync(b => b.Id == id);
            business.Status = status;
            business.SuspensionReason = status == BusinessStatus.Suspended ? SuspensionReason.SecurityConcern : null;
            return await db.SaveChangesAsync();
        });

    private async Task ChangeUserAsync(string email, Action<AppointMeAppUser> change)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        change(user);
        (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();
    }

    private async Task<DateTime?> LastLoginAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        return (await users.FindByEmailAsync(email))!.LastLoginAt;
    }

    private static void ShouldBeNow(DateTime? value) =>
        value.Should().NotBeNull().And.BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

    private static async Task<string> HtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadAsStringAsync();
    }

    // ---------------- what counts ----------------

    [Fact]
    public async Task ASuccessfulLogin_RecordsTheTime()
    {
        var tenant = await AddTenantAsync();
        (await LastLoginAsync(tenant.Email)).Should().BeNull();

        await NewClient().LoginAsync(tenant.Email, TestData.Password);

        ShouldBeNow(await LastLoginAsync(tenant.Email));
    }

    [Fact]
    public async Task EachLogin_MovesItForward()
    {
        var tenant = await AddTenantAsync();
        var old = DateTime.UtcNow.AddDays(-5);
        await ChangeUserAsync(tenant.Email, u => u.LastLoginAt = old);

        await NewClient().LoginAsync(tenant.Email, TestData.Password);

        ShouldBeNow(await LastLoginAsync(tenant.Email));
    }

    [Fact]
    public async Task TheAdminsLogin_IsRecordedToo()
    {
        await AdminClientAsync();

        ShouldBeNow(await LastLoginAsync(PlatformAdminWebAppFactory.AdminEmail));
    }

    // ---------------- what doesn't count ----------------

    [Fact]
    public async Task AWrongPassword_DoesntCount()
    {
        var tenant = await AddTenantAsync();

        var response = await NewClient().PostLoginAsync(tenant.Email, "Wrong123!Aa");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LastLoginAsync(tenant.Email)).Should().BeNull();
    }

    [Fact]
    public async Task AnUnconfirmedAccount_DoesntCount_EvenWithTheRightPassword()
    {
        var tenant = await AddTenantAsync();
        await ChangeUserAsync(tenant.Email, u => u.EmailConfirmed = false);

        var response = await NewClient().PostLoginAsync(tenant.Email, TestData.Password);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LastLoginAsync(tenant.Email)).Should().BeNull();
    }

    [Fact]
    public async Task ALockedOutAccount_DoesntCount_EvenWithTheRightPassword()
    {
        var tenant = await AddTenantAsync();
        await ChangeUserAsync(tenant.Email, u =>
        {
            u.LockoutEnabled = true;
            u.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
        });

        var response = await NewClient().PostLoginAsync(tenant.Email, TestData.Password);

        response.Headers.Location!.ToString().Should().Contain("Lockout");
        (await LastLoginAsync(tenant.Email)).Should().BeNull();
    }

    [Fact]
    public async Task ASuspendedTenant_DoesntCount_ButCountsAgainAfterReactivation()
    {
        var tenant = await AddTenantAsync();
        await SuspendAsync(tenant.Id);

        var client = NewClient();
        var login = await client.PostLoginAsync(tenant.Email, TestData.Password);
        login.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await client.GetAsync("/Customers")).Headers.Location!.ToString().Should().Contain("/Suspended");
        (await LastLoginAsync(tenant.Email)).Should().BeNull();

        await SuspendAsync(tenant.Id, BusinessStatus.Active);
        await NewClient().LoginAsync(tenant.Email, TestData.Password);

        ShouldBeNow(await LastLoginAsync(tenant.Email));
    }

    [Fact]
    public async Task UsingTheAppWithAnExistingSession_DoesntCount()
    {
        var tenant = await AddTenantAsync();
        var client = NewClient();
        await client.LoginAsync(tenant.Email, TestData.Password);
        var old = DateTime.UtcNow.AddDays(-5);
        await ChangeUserAsync(tenant.Email, u => u.LastLoginAt = old);

        (await client.GetAsync("/Customers")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await LastLoginAsync(tenant.Email)).Should().BeCloseTo(old, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ChangingThePassword_SignsInAgainInternally_ButDoesntCount()
    {
        var tenant = await AddTenantAsync();
        var client = NewClient();
        await client.LoginAsync(tenant.Email, TestData.Password);
        var old = DateTime.UtcNow.AddDays(-5);
        await ChangeUserAsync(tenant.Email, u => u.LastLoginAt = old);

        const string url = "/Identity/Account/Manage/ChangePassword";
        var response = await client.PostFormAsync(url, url, new Dictionary<string, string>
        {
            ["Input.OldPassword"] = TestData.Password,
            ["Input.NewPassword"] = "Changed123!Aa",
            ["Input.ConfirmPassword"] = "Changed123!Aa"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await LastLoginAsync(tenant.Email)).Should().BeCloseTo(old, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Registering_DoesntCount()
    {
        const string url = "/Identity/Account/Register";
        var email = $"new-{Guid.NewGuid():N}@test.com";

        await NewClient().PostFormAsync(url, url, new Dictionary<string, string>
        {
            ["Input.BusinessName"] = "New business",
            ["Input.FirstName"] = "N",
            ["Input.LastName"] = "B",
            ["Input.Email"] = email,
            ["Input.Password"] = TestData.Password,
            ["Input.ConfirmPassword"] = TestData.Password
        });

        (await LastLoginAsync(email)).Should().BeNull();
    }

    // ---------------- two-factor logins, which use the built-in pages ----------------

    private async Task<string> EnableTwoFactorAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        await users.SetTwoFactorEnabledAsync(user, true);
        await users.ResetAuthenticatorKeyAsync(user);
        return (await users.GenerateNewTwoFactorRecoveryCodesAsync(user, 1))!.Single();
    }

    /// <summary>What an authenticator app shows (RFC 6238). Identity can't generate this; it only checks it.</summary>
    private async Task<string> AuthenticatorCodeAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        var key = await users.GetAuthenticatorKeyAsync((await users.FindByEmailAsync(email))!);

        var bits = string.Concat(key!.TrimEnd('=').Select(c =>
            Convert.ToString("ABCDEFGHIJKLMNOPQRSTUVWXYZ234567".IndexOf(c), 2).PadLeft(5, '0')));
        var secret = Enumerable.Range(0, bits.Length / 8).Select(i => Convert.ToByte(bits.Substring(i * 8, 8), 2)).ToArray();

        var step = BitConverter.GetBytes(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
        if (BitConverter.IsLittleEndian) Array.Reverse(step);

        var hash = HMACSHA1.HashData(secret, step);
        var offset = hash[^1] & 0x0F;
        var value = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (value % 1_000_000).ToString("D6");
    }

    /// <summary>Does the password step and checks that it alone hasn't counted.</summary>
    private async Task<(HttpClient Client, string Page)> PassPasswordStepAsync(string email)
    {
        var client = NewClient();
        var login = await client.PostLoginAsync(email, TestData.Password);
        login.Headers.Location!.ToString().Should().Contain("LoginWith2fa");
        (await LastLoginAsync(email)).Should().BeNull("the password alone doesn't complete the login");
        return (client, login.Headers.Location!.ToString());
    }

    private static Dictionary<string, string> TwoFactorFields(string code) => new()
    {
        ["Input.TwoFactorCode"] = code,
        ["Input.RememberMachine"] = "false"
    };

    [Fact]
    public async Task ATwoFactorLogin_CountsOnlyWhenTheCodeIsRight()
    {
        var tenant = await AddTenantAsync();
        await EnableTwoFactorAsync(tenant.Email);
        var (client, page) = await PassPasswordStepAsync(tenant.Email);

        var wrong = await client.PostFormAsync(page, page, TwoFactorFields("000000"));
        wrong.StatusCode.Should().Be(HttpStatusCode.OK);
        (await LastLoginAsync(tenant.Email)).Should().BeNull();

        var right = await client.PostFormAsync(page, page, TwoFactorFields(await AuthenticatorCodeAsync(tenant.Email)));
        right.StatusCode.Should().Be(HttpStatusCode.Redirect);

        ShouldBeNow(await LastLoginAsync(tenant.Email));
    }

    [Fact]
    public async Task ARecoveryCodeLogin_Counts()
    {
        var tenant = await AddTenantAsync();
        var recoveryCode = await EnableTwoFactorAsync(tenant.Email);
        var (client, _) = await PassPasswordStepAsync(tenant.Email);

        const string url = "/Identity/Account/LoginWithRecoveryCode";
        var response = await client.PostFormAsync(url, url, new Dictionary<string, string> { ["Input.RecoveryCode"] = recoveryCode });

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        ShouldBeNow(await LastLoginAsync(tenant.Email));
    }

    [Fact]
    public async Task ASuspendedTenantsTwoFactorLogin_DoesntCount()
    {
        var tenant = await AddTenantAsync();
        await EnableTwoFactorAsync(tenant.Email);
        await SuspendAsync(tenant.Id);
        var (client, page) = await PassPasswordStepAsync(tenant.Email);

        var login = await client.PostFormAsync(page, page, TwoFactorFields(await AuthenticatorCodeAsync(tenant.Email)));

        login.StatusCode.Should().Be(HttpStatusCode.Redirect, "the code must be accepted, or this test proves nothing");
        (await client.GetAsync("/Customers")).Headers.Location!.ToString().Should().Contain("/Suspended");
        (await LastLoginAsync(tenant.Email)).Should().BeNull();
    }

    // ---------------- what the admin sees ----------------

    private static string RowFor(string html, string name) =>
        Regex.Match(html, $@"<tr>(?:(?!</tr>).)*{Regex.Escape(name)}.*?</tr>", RegexOptions.Singleline).Value;

    private static string LastLoginIn(string fragment) =>
        Regex.Match(fragment, @"last-login[^>]*>([^<]*)<").Groups[1].Value;

    [Fact]
    public async Task TheList_ShowsTheLatestLoginPerTenant_InSkopjeTime()
    {
        var first = await AddTenantAsync();
        var second = await AddTenantAsync();
        var never = await AddTenantAsync();

        var staff = $"staff-{Guid.NewGuid():N}@test.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            await TestData.EnsureUserAsync(users, staff, first.Id);
        }
        // 23:30 UTC in January is 00:30 the next day in Skopje. The owner's older login must not win.
        await ChangeUserAsync(first.Email, u => u.LastLoginAt = new DateTime(2026, 1, 10, 8, 0, 0));
        await ChangeUserAsync(staff, u => u.LastLoginAt = new DateTime(2026, 1, 15, 23, 30, 0));
        await ChangeUserAsync(second.Email, u => u.LastLoginAt = new DateTime(2026, 3, 1, 12, 0, 0));

        var client = await AdminClientAsync();
        var html = await HtmlAsync(client, "/Admin/Tenants");

        html.Should().Contain("<th>Last login</th>");
        LastLoginIn(RowFor(html, first.Name)).Should().Be("16 Jan 2026, 00:30");
        LastLoginIn(RowFor(html, second.Name)).Should().Be("1 Mar 2026, 13:00");
        LastLoginIn(RowFor(html, never.Name)).Should().Be("Never");
    }

    [Fact]
    public async Task ABusinessWithNoAccounts_ShowsNever()
    {
        var id = Guid.NewGuid();
        var name = $"No users {id:N}";
        await TestData.WithDbAsync(_factory, db => TestData.EnsureBusinessAsync(db, id, name));
        var client = await AdminClientAsync();

        LastLoginIn(RowFor(await HtmlAsync(client, "/Admin/Tenants"), name)).Should().Be("Never");
    }

    [Fact]
    public async Task TheDetailsPage_ShowsItPerAccount()
    {
        var tenant = await AddTenantAsync();
        var staff = $"staff-{Guid.NewGuid():N}@test.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            await TestData.EnsureUserAsync(users, staff, tenant.Id);
        }
        await ChangeUserAsync(tenant.Email, u => u.LastLoginAt = new DateTime(2026, 1, 15, 23, 30, 0));
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, $"/Admin/Tenants/Details/{tenant.Id}");

        html.Should().Contain("Europe/Skopje");
        LastLoginIn(RowFor(html, tenant.Email)).Should().Be("16 Jan 2026, 00:30");
        LastLoginIn(RowFor(html, staff)).Should().Be("Never");
    }

    [Fact]
    public async Task ARealLogin_ShowsUpOnTheAdminsPages()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();
        LastLoginIn(RowFor(await HtmlAsync(client, "/Admin/Tenants"), tenant.Name)).Should().Be("Never");

        await NewClient().LoginAsync(tenant.Email, TestData.Password);

        LastLoginIn(RowFor(await HtmlAsync(client, "/Admin/Tenants"), tenant.Name)).Should().NotBe("Never");
        LastLoginIn(RowFor(await HtmlAsync(client, $"/Admin/Tenants/Details/{tenant.Id}"), tenant.Email)).Should().NotBe("Never");
    }
}

/// <summary>Moves the clock past the points where the cookie and the security stamp are renewed.</summary>
public class LastLoginCookieRenewalTests : IClassFixture<MovableClockWebAppFactory>
{
    private readonly MovableClockWebAppFactory _factory;

    public LastLoginCookieRenewalTests(MovableClockWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<DateTime?> LastLoginAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        return (await users.FindByEmailAsync(email))!.LastLoginAt;
    }

    [Fact]
    public async Task ACookieRenewal_DoesntCount_ButTheNextRealLoginDoes()
    {
        var email = $"renew-{Guid.NewGuid():N}@test.com";
        await TestData.EnsureTenantWithUserAsync(_factory, Guid.NewGuid(), email);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await client.LoginAsync(email, TestData.Password);
        var loggedInAt = await LastLoginAsync(email);
        loggedInAt.Should().Be(_factory.Clock.GetUtcNow().UtcDateTime, "it uses the app's clock");

        // Over half the 60-minute session, and over the 30-minute security stamp check: the response renews the cookie.
        _factory.Clock.Advance(TimeSpan.FromMinutes(40));
        var response = await client.GetAsync("/Customers");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith(".AspNetCore.Identity.Application="),
            "otherwise this test wouldn't prove anything");
        (await LastLoginAsync(email)).Should().Be(loggedInAt);

        var again = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await again.LoginAsync(email, TestData.Password);

        (await LastLoginAsync(email)).Should().Be(_factory.Clock.GetUtcNow().UtcDateTime);
    }
}
