using System.Net;
using System.Text.RegularExpressions;
using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AppointMe.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class SuspensionTests : IClassFixture<PlatformAdminWebAppFactory>
{
    private const string SuspendedPath = TenantStatusMiddleware.SuspendedPath;

    private readonly PlatformAdminWebAppFactory _factory;

    public SuspensionTests(PlatformAdminWebAppFactory factory)
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

    private async Task<HttpClient> LoggedInAsync(string email)
    {
        var client = NewClient();
        await client.LoginAsync(email, TestData.Password);
        return client;
    }

    private async Task<(Guid Id, string Email)> AddTenantAsync()
    {
        var id = Guid.NewGuid();
        var email = $"tenant-{id:N}@test.com";
        await TestData.EnsureTenantWithUserAsync(_factory, id, email);
        return (id, email);
    }

    private Task SetSuspendedAsync(Guid id, SuspensionReason reason = SuspensionReason.SecurityConcern, string? note = null) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.SingleAsync(b => b.Id == id);
            business.Status = BusinessStatus.Suspended;
            business.SuspendedAt = new DateTime(2026, 3, 10, 12, 0, 0);
            business.SuspensionReason = reason;
            business.SuspensionNote = note;
            return await db.SaveChangesAsync();
        });

    private Task<Business> GetBusinessAsync(Guid id) =>
        TestData.WithDbAsync(_factory, db => db.Businesses.AsNoTracking().SingleAsync(b => b.Id == id));

    private Task<List<AdminActionLog>> GetLogAsync(Guid id) =>
        TestData.WithDbAsync(_factory, db => db.AdminActionLogs.AsNoTracking().Where(l => l.BusinessId == id).ToListAsync());

    private static string Details(Guid id) => $"/Admin/Tenants/Details/{id}";
    private static string Suspend(Guid id) => $"/Admin/Tenants/Suspend/{id}";
    private static string Confirm(Guid id) => $"/Admin/Tenants/ConfirmSuspend/{id}";
    private static string Reactivate(Guid id) => $"/Admin/Tenants/Reactivate/{id}";

    private static Dictionary<string, string> Reason(string reason, string note = "") => new()
    {
        ["Reason"] = reason,
        ["Note"] = note
    };

    private static async Task<string> HtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadAsStringAsync();
    }

    private static void ShouldRedirectTo(HttpResponseMessage response, string expected, string because = "")
    {
        response.StatusCode.Should().Be(HttpStatusCode.Redirect, because);
        response.Headers.Location!.ToString().Should().Contain(expected, because);
    }

    // ---------------- the admin's suspend flow ----------------

    [Fact]
    public async Task SuspendForm_ListsTheFixedReasons_AndWarnsThatTheNoteIsVisible()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, Suspend(tenant.Id));

        html.Should().Contain("Abuse of email notifications");
        html.Should().Contain("Fraudulent or fake business");
        html.Should().Contain("Violation of the terms of use");
        html.Should().Contain("Security concern");
        html.Should().Contain(">Other<");
        html.Should().Contain("The tenant will see this note");
    }

    [Fact]
    public async Task ChoosingAReason_OnlyShowsTheConfirmation_AndSuspendsNothing()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Suspend(tenant.Id), Suspend(tenant.Id), Reason("SecurityConcern", "Check this"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain(Confirm(tenant.Id));
        html.Should().Contain("Security concern").And.Contain("Check this");

        (await GetBusinessAsync(tenant.Id)).Status.Should().Be(BusinessStatus.Active);
        (await GetLogAsync(tenant.Id)).Should().BeEmpty();
        _factory.Emails.SentTo(tenant.Email).Should().BeEmpty();
    }

    [Fact]
    public async Task SubmittingExactlyWhatTheConfirmationPageRendered_Suspends_WithTheChosenReasonAndNote()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var page = await client.PostFormAsync(Suspend(tenant.Id), Suspend(tenant.Id), Reason("ViolationOfTermsOfUse", "Line one\nLine two"));
        var html = await page.Content.ReadAsStringAsync();

        // Only the hidden fields and the token of the form on that page, nothing added by the test.
        var form = Regex.Match(html, $@"<form[^>]*action=""{Regex.Escape(Confirm(tenant.Id))}"".*?</form>", RegexOptions.Singleline).Value;
        form.Should().NotBeEmpty();
        var fields = Regex.Matches(form, @"<input[^>]*>").Select(m => m.Value).ToDictionary(
            input => WebUtility.HtmlDecode(Regex.Match(input, @"name=""([^""]*)""").Groups[1].Value),
            input => WebUtility.HtmlDecode(Regex.Match(input, @"value=""([^""]*)""").Groups[1].Value));
        fields.Should().ContainKeys("Reason", "Note", "__RequestVerificationToken");

        var response = await client.PostAsync(Confirm(tenant.Id), new FormUrlEncodedContent(fields));

        ShouldRedirectTo(response, Details(tenant.Id));
        var business = await GetBusinessAsync(tenant.Id);
        business.Status.Should().Be(BusinessStatus.Suspended);
        business.SuspensionReason.Should().Be(SuspensionReason.ViolationOfTermsOfUse);
        business.SuspensionNote.Should().Contain("Line one").And.Contain("Line two");
    }

    [Fact]
    public async Task Confirming_Suspends_Logs_AndEmailsTheTenant()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Details(tenant.Id), Confirm(tenant.Id), Reason("SecurityConcern", "  Check this  "));

        ShouldRedirectTo(response, Details(tenant.Id));

        var business = await GetBusinessAsync(tenant.Id);
        business.Status.Should().Be(BusinessStatus.Suspended);
        business.SuspensionReason.Should().Be(SuspensionReason.SecurityConcern);
        business.SuspensionNote.Should().Be("Check this");
        business.SuspendedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        var entry = (await GetLogAsync(tenant.Id)).Should().ContainSingle().Which;
        entry.Action.Should().Be(AdminActionType.TenantSuspended);
        entry.Reason.Should().Be("Security concern");
        entry.Note.Should().Be("Check this");
        entry.AdminEmail.Should().Be(PlatformAdminWebAppFactory.AdminEmail);
        entry.BusinessName.Should().Be(business.Name);

        var email = _factory.Emails.SentTo(tenant.Email).Should().ContainSingle().Which;
        email.Subject.Should().Contain("suspended");
        email.Content.Should().Contain("Security concern").And.Contain("Check this").And.Contain(PlatformAdminWebAppFactory.SupportEmail);

        var details = await HtmlAsync(client, Details(tenant.Id));
        details.Should().Contain("text-bg-danger").And.Contain("Check this").And.Contain("Reactivate");
        details.Should().Contain("The tenant has been emailed");
    }

    [Theory]
    [InlineData("AbuseOfEmailNotifications")]
    [InlineData("FraudulentOrFakeBusiness")]
    [InlineData("ViolationOfTermsOfUse")]
    [InlineData("SecurityConcern")]
    public async Task ReasonsOtherThanOther_DontNeedANote(string reason)
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        await client.PostFormAsync(Details(tenant.Id), Confirm(tenant.Id), Reason(reason));

        (await GetBusinessAsync(tenant.Id)).Status.Should().Be(BusinessStatus.Suspended);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Other_WithoutANote_IsRefused_AtBothSteps(string note)
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        foreach (var url in new[] { Suspend(tenant.Id), Confirm(tenant.Id) })
        {
            var response = await client.PostFormAsync(Details(tenant.Id), url, Reason("Other", note));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Contain("Please add a note");
        }

        (await GetBusinessAsync(tenant.Id)).Status.Should().Be(BusinessStatus.Active);
    }

    [Fact]
    public async Task Other_WithANote_IsAccepted()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        await client.PostFormAsync(Details(tenant.Id), Confirm(tenant.Id), Reason("Other", "A specific reason"));

        var business = await GetBusinessAsync(tenant.Id);
        business.SuspensionReason.Should().Be(SuspensionReason.Other);
        business.SuspensionNote.Should().Be("A specific reason");
    }

    [Theory]
    [InlineData("")]
    [InlineData("NotAReason")]
    [InlineData("99")]
    public async Task AnInvalidReason_IsRefused(string reason)
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Details(tenant.Id), Confirm(tenant.Id), Reason(reason));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("field-validation-error");
        (await GetBusinessAsync(tenant.Id)).Status.Should().Be(BusinessStatus.Active);
    }

    [Fact]
    public async Task ANoteThatIsTooLong_IsRefused()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Details(tenant.Id), Confirm(tenant.Id),
            Reason("Other", new string('x', AdminActionLog.MaxNoteLength + 1)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetBusinessAsync(tenant.Id)).Status.Should().Be(BusinessStatus.Active);
    }

    [Fact]
    public async Task SuspendingTwice_IsRefused_AndLogsAndEmailsOnlyOnce()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();
        await client.PostFormAsync(Details(tenant.Id), Confirm(tenant.Id), Reason("SecurityConcern"));

        var response = await client.PostFormAsync(Details(tenant.Id), Confirm(tenant.Id), Reason("Other", "Second"));

        ShouldRedirectTo(response, Details(tenant.Id));
        (await HtmlAsync(client, Details(tenant.Id))).Should().Contain("already suspended");
        (await GetBusinessAsync(tenant.Id)).SuspensionReason.Should().Be(SuspensionReason.SecurityConcern);
        (await GetLogAsync(tenant.Id)).Should().ContainSingle();
        _factory.Emails.SentTo(tenant.Email).Should().ContainSingle();
    }

    [Fact]
    public async Task SuspendPage_OfASuspendedTenant_GoesBackToDetails()
    {
        var tenant = await AddTenantAsync();
        await SetSuspendedAsync(tenant.Id);
        var client = await AdminClientAsync();

        ShouldRedirectTo(await client.GetAsync(Suspend(tenant.Id)), Details(tenant.Id));
    }

    [Fact]
    public async Task ReactivatePage_OfAnActiveTenant_GoesBackToDetails()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        ShouldRedirectTo(await client.GetAsync(Reactivate(tenant.Id)), Details(tenant.Id));
    }

    [Fact]
    public async Task ConfirmingTheSuspension_UsesTheIdFromTheUrl_NotAnIdInTheForm()
    {
        var target = await AddTenantAsync();
        var other = await AddTenantAsync();
        var client = await AdminClientAsync();

        var fields = Reason("SecurityConcern");
        fields["Id"] = other.Id.ToString();
        await client.PostFormAsync(Details(target.Id), Confirm(target.Id), fields);

        (await GetBusinessAsync(target.Id)).Status.Should().Be(BusinessStatus.Suspended);
        (await GetBusinessAsync(other.Id)).Status.Should().Be(BusinessStatus.Active);
    }

    [Fact]
    public async Task UnknownTenant_Is404()
    {
        var tenant = await AddTenantAsync();
        var unknown = Guid.NewGuid();
        var client = await AdminClientAsync();

        (await client.GetAsync(Suspend(unknown))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync(Reactivate(unknown))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostFormAsync(Details(tenant.Id), Suspend(unknown), Reason("SecurityConcern")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostFormAsync(Details(tenant.Id), Confirm(unknown), Reason("SecurityConcern")))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.PostFormAsync(Details(tenant.Id), Reactivate(unknown), new Dictionary<string, string>()))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConfirmingWithoutAnAntiforgeryToken_IsRejected()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var response = await client.PostAsync(Confirm(tenant.Id), new FormUrlEncodedContent(Reason("SecurityConcern")));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetBusinessAsync(tenant.Id)).Status.Should().Be(BusinessStatus.Active);
    }

    // ---------------- emails ----------------

    [Fact]
    public async Task WhenTheEmailFails_TheSuspensionStands_AndTheAdminIsWarned()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        _factory.Emails.Fail = true;
        try
        {
            await client.PostFormAsync(Details(tenant.Id), Confirm(tenant.Id), Reason("SecurityConcern"));
        }
        finally
        {
            _factory.Emails.Fail = false;
        }

        (await GetBusinessAsync(tenant.Id)).Status.Should().Be(BusinessStatus.Suspended);
        (await GetLogAsync(tenant.Id)).Should().ContainSingle();
        (await HtmlAsync(client, Details(tenant.Id))).Should().Contain("could not be sent");
    }

    [Fact]
    public async Task ATenantWithNoEmailAddress_IsStillSuspended_AndTheAdminIsWarned()
    {
        var id = Guid.NewGuid();
        await TestData.WithDbAsync(_factory, db => TestData.EnsureBusinessAsync(db, id, "No users"));
        var client = await AdminClientAsync();

        await client.PostFormAsync(Details(id), Confirm(id), Reason("SecurityConcern"));

        (await GetBusinessAsync(id)).Status.Should().Be(BusinessStatus.Suspended);
        (await HtmlAsync(client, Details(id))).Should().Contain("no email address to notify");
    }

    // ---------------- what a suspended tenant sees ----------------

    [Fact]
    public async Task LoginWithTheCorrectPassword_ShowsTheSuspendedPage_WithReasonNoteAndSupportEmail()
    {
        var tenant = await AddTenantAsync();
        await SetSuspendedAsync(tenant.Id, SuspensionReason.ViolationOfTermsOfUse, "Please read section 4");
        var client = NewClient();

        var login = await client.PostLoginAsync(tenant.Email, TestData.Password);
        ShouldRedirectTo(login, "/");

        var next = await client.GetAsync(login.Headers.Location);
        ShouldRedirectTo(next, SuspendedPath);

        var html = await HtmlAsync(client, SuspendedPath);
        html.Should().Contain("Your account has been suspended");
        html.Should().Contain("Violation of the terms of use");
        html.Should().Contain("Please read section 4");
        html.Should().Contain(PlatformAdminWebAppFactory.SupportEmail);
        html.Should().Contain("10 Mar 2026");
        html.Should().Contain("/Identity/Account/Logout");
        html.Should().NotContain("href=\"/Customers");
    }

    [Fact]
    public async Task LoginWithAWrongPassword_GetsOnlyInvalidLoginAttempt()
    {
        var tenant = await AddTenantAsync();
        await SetSuspendedAsync(tenant.Id, SuspensionReason.ViolationOfTermsOfUse, "Secret note");

        var response = await NewClient().PostLoginAsync(tenant.Email, "Wrong123!Aa");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Invalid login attempt");
        html.Should().NotContainEquivalentOf("suspended");
        html.Should().NotContain("Secret note").And.NotContain("Violation of the terms of use");
    }

    [Fact]
    public async Task SuspendingTakesEffectOnTheNextRequest_AndReactivatingRestoresAccess()
    {
        var tenant = await AddTenantAsync();
        var tenantClient = await LoggedInAsync(tenant.Email);
        var admin = await AdminClientAsync();
        (await tenantClient.GetAsync("/Customers")).StatusCode.Should().Be(HttpStatusCode.OK);

        await admin.PostFormAsync(Details(tenant.Id), Confirm(tenant.Id), Reason("SecurityConcern"));

        ShouldRedirectTo(await tenantClient.GetAsync("/Customers"), SuspendedPath);

        var reactivate = await admin.PostFormAsync(Reactivate(tenant.Id), Reactivate(tenant.Id),
            new Dictionary<string, string> { ["Note"] = "Resolved by phone" });
        ShouldRedirectTo(reactivate, Details(tenant.Id));

        (await tenantClient.GetAsync("/Customers")).StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldRedirectTo(await tenantClient.GetAsync(SuspendedPath), "/");

        var business = await GetBusinessAsync(tenant.Id);
        business.Status.Should().Be(BusinessStatus.Active);
        business.SuspendedAt.Should().BeNull();
        business.SuspensionReason.Should().BeNull();
        business.SuspensionNote.Should().BeNull();

        var email = _factory.Emails.SentTo(tenant.Email).Last();
        email.Subject.Should().Contain("reactivated");
        email.Content.Should().NotContain("Resolved by phone", "the reactivation note is only for the log");

        var log = await GetLogAsync(tenant.Id);
        log.Should().Contain(l => l.Action == AdminActionType.TenantReactivated && l.Note == "Resolved by phone");
    }

    /// <summary>Every page and action in the app, built from what is registered, so new ones are covered automatically.</summary>
    private IReadOnlyList<string> AllAppUrls()
    {
        var actions = _factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items;
        return actions.Select(ToUrl).OfType<string>().Distinct().ToList();

        static string? ToUrl(ActionDescriptor action) => action switch
        {
            PageActionDescriptor page => $"/{page.AreaName}{page.ViewEnginePath}",
            ControllerActionDescriptor c when c.RouteValues.TryGetValue("area", out var area) && !string.IsNullOrEmpty(area)
                => $"/{area}/{c.ControllerName}/{c.ActionName}/{CustomWebAppFactory.DefaultTenantId}",
            ControllerActionDescriptor c => $"/{c.ControllerName}/{c.ActionName}/{CustomWebAppFactory.DefaultTenantId}",
            _ => null
        };
    }

    private static bool AllowedWhileSuspended(string url) =>
        url.StartsWith("/Suspended/") || url.StartsWith("/Home/Error/")
        || url == "/Identity/Account/Login" || url == "/Identity/Account/Logout";

    [Fact]
    public async Task SuspendedTenant_CantReachOrPostToAnyPage_ExceptTheShortAllowedList()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);
        await SetSuspendedAsync(tenant.Id);

        var urls = AllAppUrls();
        urls.Should().Contain(u => u.StartsWith("/Customers/Index"));
        urls.Should().Contain(u => u.StartsWith("/Settings/Business"));
        urls.Should().Contain(u => u.StartsWith("/Admin/Tenants/Index"));
        urls.Should().Contain("/Identity/Account/Manage/Index");
        urls.Count.Should().BeGreaterThan(30);

        foreach (var url in urls.Where(u => !AllowedWhileSuspended(u)))
        {
            ShouldRedirectTo(await client.GetAsync(url), SuspendedPath, $"GET {url}");
            ShouldRedirectTo(await client.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>())),
                SuspendedPath, $"POST {url}");
        }
    }

    [Fact]
    public async Task SuspendedTenant_PostingAValidForm_ChangesNothing()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);
        var name = (await GetBusinessAsync(tenant.Id)).Name;
        await SetSuspendedAsync(tenant.Id);

        var customer = await client.PostFormAsync(SuspendedPath, "/Customers/Create", new Dictionary<string, string>
        {
            ["FirstName"] = "New", ["LastName"] = "Customer", ["Email"] = "new@test.com",
            ["PhoneNumber"] = "070123456", ["State"] = "MK", ["City"] = "Skopje"
        });
        ShouldRedirectTo(customer, SuspendedPath);

        var settings = await client.PostFormAsync(SuspendedPath, "/Settings/Business", new Dictionary<string, string>
        {
            ["Id"] = tenant.Id.ToString(), ["Name"] = "Changed while suspended"
        });
        ShouldRedirectTo(settings, SuspendedPath);

        (await TestData.WithDbAsync(_factory, db => db.Customers.CountAsync(c => c.TenantId == tenant.Id))).Should().Be(0);
        (await GetBusinessAsync(tenant.Id)).Name.Should().Be(name);
    }

    [Theory]
    [InlineData("/Admin")]
    [InlineData("/Admin/Tenants")]
    public async Task SuspendedTenant_CantUseTheAdminArea_ToReactivateThemselves(string url)
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);
        await SetSuspendedAsync(tenant.Id);

        ShouldRedirectTo(await client.GetAsync(url), SuspendedPath);

        var post = await client.PostFormAsync(SuspendedPath, Reactivate(tenant.Id), new Dictionary<string, string>());
        ShouldRedirectTo(post, SuspendedPath);
        (await GetBusinessAsync(tenant.Id)).Status.Should().Be(BusinessStatus.Suspended);
    }

    [Fact]
    public async Task ScriptsAskingForJson_GetForbidden_NotARedirect()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);
        await SetSuspendedAsync(tenant.Id);

        var request = new HttpRequestMessage(HttpMethod.Get, "/Appointments/Calendar");
        request.Headers.Accept.ParseAdd("application/json");

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task SuspendedTenant_CanLogOut_AndThenLogBackIn()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);
        await SetSuspendedAsync(tenant.Id);

        var logout = await client.PostFormAsync(SuspendedPath, "/Identity/Account/Logout", new Dictionary<string, string>());

        ShouldRedirectTo(logout, "/Identity/Account/Logout");
        (await client.GetAsync("/Identity/Account/Logout")).StatusCode.Should().Be(HttpStatusCode.OK);
        ShouldRedirectTo(await client.GetAsync(SuspendedPath), "/Identity/Account/Login");

        (await client.PostLoginAsync(tenant.Email, TestData.Password)).StatusCode.Should().Be(HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task SuspendedPage_EncodesTheNote()
    {
        const string attack = "<script>alert(1)</script>";
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);
        await SetSuspendedAsync(tenant.Id, SuspensionReason.Other, attack);

        (await HtmlAsync(client, SuspendedPath)).Should().NotContain(attack);
        (await HtmlAsync(await AdminClientAsync(), Details(tenant.Id))).Should().NotContain(attack);
    }

    [Fact]
    public async Task SuspendedPage_ForAnyoneElse_IsNotShown()
    {
        var tenant = await AddTenantAsync();

        ShouldRedirectTo(await NewClient().GetAsync(SuspendedPath), "/Identity/Account/Login");
        ShouldRedirectTo(await (await LoggedInAsync(tenant.Email)).GetAsync(SuspendedPath), "/");
    }

    [Fact]
    public async Task SuspendingOneTenant_DoesNotAffectAnother_OrTheAdmin()
    {
        var suspended = await AddTenantAsync();
        var other = await AddTenantAsync();
        var otherClient = await LoggedInAsync(other.Email);
        var admin = await AdminClientAsync();

        await SetSuspendedAsync(suspended.Id);

        (await otherClient.GetAsync("/Customers")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/Admin/Tenants")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync(Details(suspended.Id))).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------- list, totals ----------------

    [Fact]
    public async Task StatusFilter_AndTotals_UseTheRealStatus()
    {
        var suspended = await AddTenantAsync();
        var active = await AddTenantAsync();
        await SetSuspendedAsync(suspended.Id);
        var client = await AdminClientAsync();

        var suspendedList = await HtmlAsync(client, "/Admin/Tenants?status=Suspended");
        suspendedList.Should().Contain(suspended.Email).And.NotContain(active.Email);
        suspendedList.Should().Contain("text-bg-danger");

        var activeList = await HtmlAsync(client, "/Admin/Tenants?status=Active");
        activeList.Should().Contain(active.Email).And.NotContain(suspended.Email);

        var (total, suspendedCount) = await TestData.WithDbAsync(_factory, async db =>
            (await db.Businesses.CountAsync(), await db.Businesses.CountAsync(b => b.Status == BusinessStatus.Suspended)));
        var home = await HtmlAsync(client, "/Admin");
        home.Should().Contain($"id=\"total-tenants\">{total}<");
        home.Should().Contain($"id=\"active-tenants\">{total - suspendedCount}<");
        home.Should().Contain($"id=\"suspended-tenants\">{suspendedCount}<");
    }

    // ---------------- action log ----------------

    [Fact]
    public async Task ActionLogPage_AndDetails_ShowWhoDidWhatAndWhy()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();
        await client.PostFormAsync(Details(tenant.Id), Confirm(tenant.Id), Reason("FraudulentOrFakeBusiness", "Fake address"));
        var name = (await GetBusinessAsync(tenant.Id)).Name;

        foreach (var url in new[] { "/Admin/ActionLog", Details(tenant.Id) })
        {
            var html = await HtmlAsync(client, url);
            html.Should().Contain(name);
            html.Should().Contain("Fraudulent or fake business").And.Contain("Fake address");
            html.Should().Contain(PlatformAdminWebAppFactory.AdminEmail);
        }
    }

    [Fact]
    public async Task EditingATenant_LogsTheFieldNames_ButNotTheirValues()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();
        var edit = $"/Admin/Tenants/Edit/{tenant.Id}";

        await client.PostFormAsync(edit, edit, new Dictionary<string, string>
        {
            ["Name"] = "Logged name", ["Email"] = "logged@test.com", ["Phone"] = "", ["Address"] = "Logged address"
        });

        var entry = (await GetLogAsync(tenant.Id)).Should().ContainSingle().Which;
        entry.Action.Should().Be(AdminActionType.TenantEdited);
        entry.ChangedFields.Should().Contain("Name").And.Contain("Email").And.Contain("Address");
        entry.ChangedFields.Should().NotContain("Phone");
        entry.ChangedFields.Should().NotContain("logged@test.com").And.NotContain("Logged address");
        entry.BusinessName.Should().Be("Logged name");
        entry.AdminEmail.Should().Be(PlatformAdminWebAppFactory.AdminEmail);
    }

    [Fact]
    public async Task SavingAnUnchangedForm_LogsNothing()
    {
        var tenant = await AddTenantAsync();
        var name = (await GetBusinessAsync(tenant.Id)).Name;
        var address = (await GetBusinessAsync(tenant.Id)).Address;
        var client = await AdminClientAsync();
        var edit = $"/Admin/Tenants/Edit/{tenant.Id}";

        await client.PostFormAsync(edit, edit, new Dictionary<string, string>
        {
            ["Name"] = name, ["Email"] = "", ["Phone"] = "", ["Address"] = address ?? ""
        });

        (await GetLogAsync(tenant.Id)).Should().BeEmpty();
    }

    // ---------------- who can do this ----------------

    public static TheoryData<string> AdminGetUrls => new()
    {
        "/Admin/ActionLog",
        $"/Admin/Tenants/Suspend/{CustomWebAppFactory.DefaultTenantId}",
        $"/Admin/Tenants/Reactivate/{CustomWebAppFactory.DefaultTenantId}"
    };

    [Theory]
    [MemberData(nameof(AdminGetUrls))]
    public async Task Tenant_GetsAccessDenied(string url)
    {
        var client = await LoggedInAsync(await TestData.EnsureTenantWithUserAsync(
            _factory, CustomWebAppFactory.DefaultTenantId, CustomWebAppFactory.DefaultUserEmail));

        ShouldRedirectTo(await client.GetAsync(url), "/Identity/Account/AccessDenied");
    }

    [Theory]
    [MemberData(nameof(AdminGetUrls))]
    public async Task Anonymous_GoesToLogin(string url)
    {
        ShouldRedirectTo(await NewClient().GetAsync(url), "/Identity/Account/Login");
    }

    [Fact]
    public async Task Tenant_CantSuspendAnotherTenant_OrThemselves()
    {
        var tenant = await AddTenantAsync();
        var victim = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);

        foreach (var id in new[] { victim.Id, tenant.Id })
        {
            var response = await client.PostFormAsync("/Settings/Business", Confirm(id), Reason("SecurityConcern"));

            ShouldRedirectTo(response, "/Identity/Account/AccessDenied");
            (await GetBusinessAsync(id)).Status.Should().Be(BusinessStatus.Active);
        }
    }

    [Fact]
    public async Task Anonymous_CantSuspendATenant()
    {
        var tenant = await AddTenantAsync();

        var response = await NewClient().PostAsync(Confirm(tenant.Id), new FormUrlEncodedContent(Reason("SecurityConcern")));

        ShouldRedirectTo(response, "/Identity/Account/Login");
        (await GetBusinessAsync(tenant.Id)).Status.Should().Be(BusinessStatus.Active);
    }
}

/// <summary>Without the Support:Email setting the page still works, just without an address.</summary>
public class SuspendedPageWithoutSupportEmailTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public SuspendedPageWithoutSupportEmailTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task PageRenders_AndTellsTheTenantToContactSupport()
    {
        var id = Guid.NewGuid();
        var email = $"tenant-{id:N}@test.com";
        await TestData.EnsureTenantWithUserAsync(_factory, id, email);
        await TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.SingleAsync(b => b.Id == id);
            business.Status = BusinessStatus.Suspended;
            business.SuspensionReason = SuspensionReason.SecurityConcern;
            return await db.SaveChangesAsync();
        });
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.PostLoginAsync(email, TestData.Password);

        var response = await client.GetAsync(TenantStatusMiddleware.SuspendedPath);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Please contact support");
        html.Should().NotContain("mailto:");
    }
}
