using System.Net;
using System.Text.RegularExpressions;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Service.Email;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AppointMe.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class TenantAccountToolsTests : IClassFixture<PlatformAdminWebAppFactory>
{
    private const string AdminEmail = PlatformAdminWebAppFactory.AdminEmail;

    private readonly PlatformAdminWebAppFactory _factory;

    public TenantAccountToolsTests(PlatformAdminWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient NewClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private async Task<HttpClient> AdminClientAsync()
    {
        var client = NewClient();
        await client.LoginAsync(AdminEmail, PlatformAdminWebAppFactory.AdminPassword);
        return client;
    }

    private async Task<HttpClient> LoggedInAsync(string email, string password = TestData.Password)
    {
        var client = NewClient();
        await client.LoginAsync(email, password);
        return client;
    }

    private async Task<(Guid Id, string Email, string UserId)> AddTenantAsync(bool confirmed = true, bool locked = false, bool suspended = false)
    {
        var id = Guid.NewGuid();
        var email = $"account-{id:N}@test.com";
        await TestData.EnsureTenantWithUserAsync(_factory, id, email);

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        user.EmailConfirmed = confirmed;
        if (locked)
        {
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(15);
            user.AccessFailedCount = 3;
        }
        await users.UpdateAsync(user);

        if (suspended)
        {
            await TestData.WithDbAsync(_factory, async db =>
            {
                var business = await db.Businesses.SingleAsync(b => b.Id == id);
                business.Status = BusinessStatus.Suspended;
                business.SuspensionReason = SuspensionReason.SecurityConcern;
                return await db.SaveChangesAsync();
            });
        }

        return (id, email, user.Id);
    }

    private async Task<AppointMeAppUser> GetUserAsync(string id)
    {
        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        return (await users.FindByIdAsync(id))!;
    }

    private Task<List<AdminActionLog>> GetLogAsync(Guid id) =>
        TestData.WithDbAsync(_factory, db => db.AdminActionLogs.AsNoTracking().Where(l => l.BusinessId == id).ToListAsync());

    private static string Details(Guid id) => $"/Admin/Tenants/Details/{id}";
    private static string Action(string action, Guid tenant) => $"/Admin/TenantAccounts/{action}/{tenant}";
    private static string Confirm(string action, Guid tenant, string accountId) =>
        $"{Action(action, tenant)}?accountId={Uri.EscapeDataString(accountId)}";

    private static Dictionary<string, string> Account(string accountId) => new() { ["accountId"] = accountId };

    private static async Task<string> HtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadAsStringAsync();
    }

    private static void ShouldRedirectTo(HttpResponseMessage response, string expected)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain(expected);
    }

    private static string LinkIn(string body) =>
        WebUtility.HtmlDecode(Regex.Match(body, @"href=['""]([^'""]+)['""]").Groups[1].Value);

    private static string CodeIn(string link) => Regex.Match(link, @"code=([^&]+)").Groups[1].Value;

    public static TheoryData<string> Actions => new() { "ResendConfirmation", "Unlock", "SendPasswordReset" };

    // ---------------- the details page ----------------

    [Fact]
    public async Task Details_ShowEachAccountsHealth_AndOnlyTheButtonsThatApply()
    {
        var healthy = await AddTenantAsync();
        var unconfirmed = await AddTenantAsync(confirmed: false);
        var locked = await AddTenantAsync(locked: true);
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, Details(healthy.Id));
        html.Should().Contain(healthy.Email).And.Contain(">Confirmed<").And.Contain("Not locked");
        html.Should().Contain(Confirm("SendPasswordReset", healthy.Id, healthy.UserId).Replace("&", "&amp;"));
        html.Should().NotContain("/Admin/TenantAccounts/ResendConfirmation").And.NotContain("/Admin/TenantAccounts/Unlock");

        html = await HtmlAsync(client, Details(unconfirmed.Id));
        html.Should().Contain(">Not confirmed<");
        html.Should().Contain("/Admin/TenantAccounts/ResendConfirmation");
        html.Should().NotContain("/Admin/TenantAccounts/SendPasswordReset").And.NotContain("/Admin/TenantAccounts/Unlock");

        html = await HtmlAsync(client, Details(locked.Id));
        html.Should().Contain(">Locked out<").And.Contain("/Admin/TenantAccounts/Unlock");
    }

    [Fact]
    public async Task Details_ShowTheLockoutEnd_InSkopjeTime()
    {
        var tenant = await AddTenantAsync();
        var end = new DateTimeOffset(DateTime.UtcNow.Year + 1, 1, 15, 23, 30, 0, TimeSpan.Zero);
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            var user = (await users.FindByIdAsync(tenant.UserId))!;
            user.LockoutEnabled = true;
            user.LockoutEnd = end;
            await users.UpdateAsync(user);
        }
        var client = await AdminClientAsync();

        (await HtmlAsync(client, Details(tenant.Id))).Should().Contain($"until 16 Jan {end.Year}, 00:30");
    }

    [Fact]
    public async Task AnExpiredLockout_IsNotShownAsLocked()
    {
        var tenant = await AddTenantAsync();
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            var user = (await users.FindByIdAsync(tenant.UserId))!;
            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.UtcNow.AddMinutes(-1);
            await users.UpdateAsync(user);
        }
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, Details(tenant.Id));

        html.Should().Contain("Not locked").And.NotContain("/Admin/TenantAccounts/Unlock");
    }

    [Fact]
    public async Task ABusinessWithNoAccounts_SaysSo()
    {
        var id = Guid.NewGuid();
        await TestData.WithDbAsync(_factory, db => TestData.EnsureBusinessAsync(db, id, "No users"));
        var client = await AdminClientAsync();

        (await HtmlAsync(client, Details(id))).Should().Contain("no login accounts");
    }

    [Fact]
    public async Task AnAdminAccountWithATenantIdSetByHand_IsNotListed_AndCantBeTargeted()
    {
        var tenant = await AddTenantAsync();
        string adminAccountId;
        var adminAccountEmail = $"hand-admin-{Guid.NewGuid():N}@test.com";
        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            var user = await TestData.EnsureUserAsync(users, adminAccountEmail, tenant.Id);
            await users.AddToRoleAsync(user, PlatformRoles.Admin);
            adminAccountId = user.Id;
        }
        var client = await AdminClientAsync();

        (await HtmlAsync(client, Details(tenant.Id))).Should().NotContain(adminAccountEmail);
        (await client.GetAsync(Confirm("SendPasswordReset", tenant.Id, adminAccountId))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostFormAsync(Details(tenant.Id), Action("SendPasswordReset", tenant.Id), Account(adminAccountId)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.Emails.SentTo(adminAccountEmail).Should().BeEmpty();
    }

    // ---------------- the confirmation step ----------------

    [Fact]
    public async Task EachConfirmationPage_NamesTheAccount_AndChangesNothing()
    {
        var unconfirmed = await AddTenantAsync(confirmed: false);
        var locked = await AddTenantAsync(locked: true);
        var client = await AdminClientAsync();

        var resend = await HtmlAsync(client, Confirm("ResendConfirmation", unconfirmed.Id, unconfirmed.UserId));
        resend.Should().Contain(unconfirmed.Email).And.Contain("Resend the confirmation email?");

        var unlock = await HtmlAsync(client, Confirm("Unlock", locked.Id, locked.UserId));
        unlock.Should().Contain(locked.Email).And.Contain("Unlock this account?");

        var reset = await HtmlAsync(client, Confirm("SendPasswordReset", locked.Id, locked.UserId));
        reset.Should().Contain("Send a password reset email?").And.Contain("see or set the password");

        _factory.Emails.SentTo(unconfirmed.Email).Should().BeEmpty();
        _factory.Emails.SentTo(locked.Email).Should().BeEmpty();
        (await GetUserAsync(locked.UserId)).LockoutEnd.Should().NotBeNull();
        (await GetLogAsync(unconfirmed.Id)).Should().BeEmpty();
        (await GetLogAsync(locked.Id)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task SubmittingWhatTheConfirmationPageRendered_Works(string action)
    {
        var tenant = await AddTenantAsync(confirmed: action != "ResendConfirmation", locked: action == "Unlock");
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, Confirm(action, tenant.Id, tenant.UserId));
        var hidden = WebUtility.HtmlDecode(Regex.Match(html, @"name=""accountId"" value=""([^""]*)""").Groups[1].Value);
        hidden.Should().Be(tenant.UserId);

        var response = await client.PostAsync(Action(action, tenant.Id), new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["accountId"] = hidden,
            ["__RequestVerificationToken"] = TestLoginHelper.ExtractAntiForgeryToken(html)
        }));

        ShouldRedirectTo(response, Details(tenant.Id));
        (await GetLogAsync(tenant.Id)).Should().ContainSingle();
    }

    // ---------------- resend confirmation ----------------

    [Fact]
    public async Task Resend_SendsTheRegistrationEmail_WhoseLinkReallyConfirms()
    {
        var tenant = await AddTenantAsync(confirmed: false);
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Details(tenant.Id), Action("ResendConfirmation", tenant.Id), Account(tenant.UserId));

        ShouldRedirectTo(response, Details(tenant.Id));
        var message = _factory.Emails.SentTo(tenant.Email).Should().ContainSingle().Which;
        message.Subject.Should().Be(AccountConfirmationEmail.Subject);
        message.IsHtml.Should().BeTrue();

        var link = new Uri(LinkIn(message.Content));
        link.AbsolutePath.Should().Be("/Identity/Account/ConfirmEmail");
        (await NewClient().GetAsync(link.PathAndQuery)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetUserAsync(tenant.UserId)).EmailConfirmed.Should().BeTrue();

        (await HtmlAsync(client, Details(tenant.Id))).Should().Contain("has been sent");
    }

    [Fact]
    public async Task Resend_ForAConfirmedAccount_IsRefused_SendsNothing_AndLogsNothing()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        ShouldRedirectTo(await client.GetAsync(Confirm("ResendConfirmation", tenant.Id, tenant.UserId)), Details(tenant.Id));
        (await HtmlAsync(client, Details(tenant.Id))).Should().Contain("already confirmed");

        var response = await client.PostFormAsync(Details(tenant.Id), Action("ResendConfirmation", tenant.Id), Account(tenant.UserId));

        ShouldRedirectTo(response, Details(tenant.Id));
        (await HtmlAsync(client, Details(tenant.Id))).Should().Contain("already confirmed");
        _factory.Emails.SentTo(tenant.Email).Should().BeEmpty();
        (await GetLogAsync(tenant.Id)).Should().BeEmpty();
    }

    // ---------------- unlock ----------------

    [Fact]
    public async Task Unlock_ClearsTheLockoutAndCount_SoTheyCanLogIn()
    {
        var tenant = await AddTenantAsync(locked: true);
        ShouldRedirectTo(await NewClient().PostLoginAsync(tenant.Email, TestData.Password), "Lockout");
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Details(tenant.Id), Action("Unlock", tenant.Id), Account(tenant.UserId));

        ShouldRedirectTo(response, Details(tenant.Id));
        var user = await GetUserAsync(tenant.UserId);
        user.LockoutEnd.Should().BeNull();
        user.AccessFailedCount.Should().Be(0);

        var html = await HtmlAsync(client, Details(tenant.Id));
        html.Should().Contain("has been unlocked").And.Contain("Not locked");
        await NewClient().LoginAsync(tenant.Email, TestData.Password);
    }

    [Fact]
    public async Task Unlock_OfAnAccountThatIsntLocked_IsRefused_AndLogsNothing()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        ShouldRedirectTo(await client.GetAsync(Confirm("Unlock", tenant.Id, tenant.UserId)), Details(tenant.Id));
        var response = await client.PostFormAsync(Details(tenant.Id), Action("Unlock", tenant.Id), Account(tenant.UserId));

        ShouldRedirectTo(response, Details(tenant.Id));
        (await HtmlAsync(client, Details(tenant.Id))).Should().Contain("locked out");
        (await GetLogAsync(tenant.Id)).Should().BeEmpty();
    }

    // ---------------- password reset ----------------

    [Fact]
    public async Task Reset_SendsTheForgotPasswordEmail_WhoseLinkReallyResets_AndChangesNoPasswordItself()
    {
        var tenant = await AddTenantAsync();
        var hashBefore = (await GetUserAsync(tenant.UserId)).PasswordHash;
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Details(tenant.Id), Action("SendPasswordReset", tenant.Id), Account(tenant.UserId));

        ShouldRedirectTo(response, Details(tenant.Id));
        (await GetUserAsync(tenant.UserId)).PasswordHash.Should().Be(hashBefore);

        var fromAdmin = _factory.Emails.SentTo(tenant.Email).Should().ContainSingle().Which;
        fromAdmin.Subject.Should().Be(PasswordResetEmail.Subject);

        // The same email as the Forgot password page, apart from the one-time code.
        await NewClient().PostFormAsync("/Identity/Account/ForgotPassword", "/Identity/Account/ForgotPassword",
            new Dictionary<string, string> { ["Input.Email"] = tenant.Email });
        var fromPage = _factory.Emails.SentTo(tenant.Email).Last();
        fromPage.Should().NotBeSameAs(fromAdmin);
        fromPage.Subject.Should().Be(fromAdmin.Subject);
        fromPage.IsHtml.Should().Be(fromAdmin.IsHtml);
        WithoutCode(fromPage).Should().Be(WithoutCode(fromAdmin));

        // The link works.
        const string newPassword = "Changed123!Aa";
        var link = new Uri(LinkIn(fromAdmin.Content));
        var anonymous = NewClient();
        var page = await HtmlAsync(anonymous, link.PathAndQuery);
        var reset = await anonymous.PostAsync(link.AbsolutePath, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Email"] = tenant.Email,
            ["Input.Password"] = newPassword,
            ["Input.ConfirmPassword"] = newPassword,
            ["Input.Code"] = WebUtility.HtmlDecode(Regex.Match(
                Regex.Match(page, @"<input[^>]*name=""Input\.Code""[^>]*>").Value, @"value=""([^""]*)""").Groups[1].Value),
            ["__RequestVerificationToken"] = TestLoginHelper.ExtractAntiForgeryToken(page)
        }));
        ShouldRedirectTo(reset, "ResetPasswordConfirmation");
        await NewClient().LoginAsync(tenant.Email, newPassword);

        static string WithoutCode(EmailMessage message) => Regex.Replace(message.Content, @"code=[^']+", "code=X");
    }

    [Fact]
    public async Task Reset_ForAnUnconfirmedAccount_IsRefused_LikeForgotPassword()
    {
        var tenant = await AddTenantAsync(confirmed: false);
        var client = await AdminClientAsync();

        ShouldRedirectTo(await client.GetAsync(Confirm("SendPasswordReset", tenant.Id, tenant.UserId)), Details(tenant.Id));
        var response = await client.PostFormAsync(Details(tenant.Id), Action("SendPasswordReset", tenant.Id), Account(tenant.UserId));

        ShouldRedirectTo(response, Details(tenant.Id));
        (await HtmlAsync(client, Details(tenant.Id))).Should().Contain("confirmed yet");
        _factory.Emails.SentTo(tenant.Email).Should().BeEmpty();
        (await GetLogAsync(tenant.Id)).Should().BeEmpty();
    }

    // ---------------- suspended tenants ----------------

    [Fact]
    public async Task AllThreeActions_WorkForASuspendedTenant()
    {
        var resend = await AddTenantAsync(confirmed: false, suspended: true);
        var unlock = await AddTenantAsync(locked: true, suspended: true);
        var reset = await AddTenantAsync(suspended: true);
        var client = await AdminClientAsync();

        await client.PostFormAsync(Details(resend.Id), Action("ResendConfirmation", resend.Id), Account(resend.UserId));
        await client.PostFormAsync(Details(unlock.Id), Action("Unlock", unlock.Id), Account(unlock.UserId));
        await client.PostFormAsync(Details(reset.Id), Action("SendPasswordReset", reset.Id), Account(reset.UserId));

        _factory.Emails.SentTo(resend.Email).Should().ContainSingle();
        (await GetUserAsync(unlock.UserId)).LockoutEnd.Should().BeNull();
        _factory.Emails.SentTo(reset.Email).Should().ContainSingle();
    }

    [Fact]
    public async Task ASuspendedTenantStillLoggedIn_CanUseTheResetLink_ButNothingElse()
    {
        var tenant = await AddTenantAsync();
        var tenantClient = await LoggedInAsync(tenant.Email);
        await TestData.WithDbAsync(_factory, async db =>
        {
            (await db.Businesses.SingleAsync(b => b.Id == tenant.Id)).Status = BusinessStatus.Suspended;
            return await db.SaveChangesAsync();
        });
        var admin = await AdminClientAsync();
        await admin.PostFormAsync(Details(tenant.Id), Action("SendPasswordReset", tenant.Id), Account(tenant.UserId));
        var link = new Uri(LinkIn(_factory.Emails.SentTo(tenant.Email).Single().Content));

        (await tenantClient.GetAsync(link.PathAndQuery)).StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldRedirectTo(await tenantClient.GetAsync("/Customers"), TenantStatusMiddleware.SuspendedPath);
    }

    // ---------------- failed emails ----------------

    [Theory]
    [InlineData("ResendConfirmation")]
    [InlineData("SendPasswordReset")]
    public async Task WhenTheEmailFails_TheActionStillCounts_AndTheAdminIsWarned(string action)
    {
        var tenant = await AddTenantAsync(confirmed: action != "ResendConfirmation");
        var client = await AdminClientAsync();

        _factory.Emails.Fail = true;
        try
        {
            var response = await client.PostFormAsync(Details(tenant.Id), Action(action, tenant.Id), Account(tenant.UserId));
            ShouldRedirectTo(response, Details(tenant.Id));
        }
        finally
        {
            _factory.Emails.Fail = false;
        }

        (await GetLogAsync(tenant.Id)).Should().ContainSingle();
        (await HtmlAsync(client, Details(tenant.Id))).Should().Contain("could not be sent");
    }

    // ---------------- the action log ----------------

    [Theory]
    [InlineData("ResendConfirmation", AdminActionType.AccountConfirmationResent, "Resent the confirmation email")]
    [InlineData("Unlock", AdminActionType.AccountUnlocked, "Unlocked an account")]
    [InlineData("SendPasswordReset", AdminActionType.AccountPasswordResetSent, "Sent a password reset email")]
    public async Task EachAction_IsLogged_WithWhoWhatAndWhichAccount(string action, AdminActionType type, string text)
    {
        var tenant = await AddTenantAsync(confirmed: action != "ResendConfirmation", locked: action == "Unlock");
        var client = await AdminClientAsync();

        await client.PostFormAsync(Details(tenant.Id), Action(action, tenant.Id), Account(tenant.UserId));

        var entry = (await GetLogAsync(tenant.Id)).Should().ContainSingle().Which;
        entry.Action.Should().Be(type);
        entry.AdminEmail.Should().Be(AdminEmail);
        entry.Note.Should().Be($"Account: {tenant.Email}");
        entry.At.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        foreach (var url in new[] { "/Admin/ActionLog", Details(tenant.Id) })
        {
            var html = await HtmlAsync(client, url);
            html.Should().Contain(text).And.Contain($"Account: {tenant.Email}");
        }
    }

    [Theory]
    [InlineData("ResendConfirmation")]
    [InlineData("SendPasswordReset")]
    public async Task NoLinkOrTokenEverAppears_InTheLogOrOnThePage(string action)
    {
        var tenant = await AddTenantAsync(confirmed: action != "ResendConfirmation");
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Details(tenant.Id), Action(action, tenant.Id), Account(tenant.UserId));
        var code = CodeIn(LinkIn(_factory.Emails.SentTo(tenant.Email).Single().Content));
        code.Should().NotBeEmpty();

        var entry = (await GetLogAsync(tenant.Id)).Single();
        foreach (var value in new[] { entry.Note, entry.Reason, entry.ChangedFields, entry.BusinessName, entry.AdminEmail })
            (value ?? "").Should().NotContain(code).And.NotContain("http").And.NotContain("code=");

        foreach (var html in new[]
        {
            await response.Content.ReadAsStringAsync(),
            await HtmlAsync(client, Details(tenant.Id)),
            await HtmlAsync(client, "/Admin/ActionLog")
        })
        {
            html.Should().NotContain(code).And.NotContain("code=");
            html.Should().NotContain("/Identity/Account/ResetPassword").And.NotContain("/Identity/Account/ConfirmEmail");
        }
    }

    [Fact]
    public async Task TheDetailsPage_NeverShowsAPasswordHashOrSecurityStamp()
    {
        var tenant = await AddTenantAsync();
        var user = await GetUserAsync(tenant.UserId);
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, Details(tenant.Id));

        html.Should().NotContain(user.PasswordHash!).And.NotContain(user.SecurityStamp!);
    }

    [Fact]
    public async Task DeletingTheTenant_RemovesTheAccountEmailFromTheLog_ButKeepsWhoDidIt()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();
        await client.PostFormAsync(Details(tenant.Id), Action("SendPasswordReset", tenant.Id), Account(tenant.UserId));
        var name = await TestData.WithDbAsync(_factory, db => db.Businesses.Where(b => b.Id == tenant.Id).Select(b => b.Name).SingleAsync());

        await client.PostFormAsync($"/Admin/Tenants/Delete/{tenant.Id}", $"/Admin/Tenants/Delete/{tenant.Id}",
            new Dictionary<string, string> { ["ConfirmName"] = name });

        var entry = (await GetLogAsync(tenant.Id)).Single(l => l.Action == AdminActionType.AccountPasswordResetSent);
        entry.Note.Should().BeNull();
        entry.AdminEmail.Should().Be(AdminEmail);
    }

    // ---------------- only this tenant's accounts ----------------

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task AnAccountFromAnotherTenant_IsRejected_AndNothingHappens(string action)
    {
        var selected = await AddTenantAsync();
        var victim = await AddTenantAsync(confirmed: action != "ResendConfirmation", locked: action == "Unlock");
        var client = await AdminClientAsync();

        (await client.GetAsync(Confirm(action, selected.Id, victim.UserId))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var response = await client.PostFormAsync(Details(selected.Id), Action(action, selected.Id), Account(victim.UserId));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.Emails.SentTo(victim.Email).Should().BeEmpty();
        if (action == "Unlock")
            (await GetUserAsync(victim.UserId)).LockoutEnd.Should().NotBeNull();
        (await GetLogAsync(selected.Id)).Should().BeEmpty();
        (await GetLogAsync(victim.Id)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task AMissingOrUnknownAccount_OrTenant_Is404(string action)
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        (await client.GetAsync(Action(action, tenant.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(Confirm(action, tenant.Id, Guid.NewGuid().ToString()))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(Confirm(action, Guid.NewGuid(), tenant.UserId))).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await client.PostFormAsync(Details(tenant.Id), Action(action, tenant.Id), new Dictionary<string, string>()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostFormAsync(Details(tenant.Id), Action(action, Guid.NewGuid()), Account(tenant.UserId)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task TheTenantIdComesFromTheUrl_NotTheForm()
    {
        var selected = await AddTenantAsync();
        var other = await AddTenantAsync();
        var client = await AdminClientAsync();

        var fields = Account(other.UserId);
        fields["id"] = other.Id.ToString();

        var response = await client.PostFormAsync(Details(selected.Id), Action("SendPasswordReset", selected.Id), fields);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _factory.Emails.SentTo(other.Email).Should().BeEmpty();
    }

    // ---------------- who can do this ----------------

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task ATenant_CantReachThese_EvenForTheirOwnAccount(string action)
    {
        var tenant = await AddTenantAsync(locked: false);
        var other = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);

        foreach (var (tenantId, accountId) in new[] { (tenant.Id, tenant.UserId), (other.Id, other.UserId) })
        {
            ShouldRedirectTo(await client.GetAsync(Confirm(action, tenantId, accountId)), "/Identity/Account/AccessDenied");
            ShouldRedirectTo(await client.PostFormAsync("/Settings/Business", Action(action, tenantId), Account(accountId)), "/Identity/Account/AccessDenied");
        }

        _factory.Emails.SentTo(tenant.Email).Should().BeEmpty();
        _factory.Emails.SentTo(other.Email).Should().BeEmpty();
        (await GetLogAsync(tenant.Id)).Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task Anonymous_GoesToLogin(string action)
    {
        var tenant = await AddTenantAsync();

        ShouldRedirectTo(await NewClient().GetAsync(Confirm(action, tenant.Id, tenant.UserId)), "/Identity/Account/Login");
        ShouldRedirectTo(await NewClient().PostAsync(Action(action, tenant.Id), new FormUrlEncodedContent(Account(tenant.UserId))), "/Identity/Account/Login");
    }

    [Theory]
    [MemberData(nameof(Actions))]
    public async Task WithoutAnAntiforgeryToken_NothingHappens(string action)
    {
        var tenant = await AddTenantAsync(confirmed: action != "ResendConfirmation", locked: action == "Unlock");
        var client = await AdminClientAsync();

        var response = await client.PostAsync(Action(action, tenant.Id), new FormUrlEncodedContent(Account(tenant.UserId)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetLogAsync(tenant.Id)).Should().BeEmpty();
    }
}
