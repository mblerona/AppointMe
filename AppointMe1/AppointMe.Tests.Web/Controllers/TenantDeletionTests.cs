using System.Net;
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

/// <summary>The pages and who may use them. That no rows are left behind is tested on SQL Server.</summary>
public class TenantDeletionTests : IClassFixture<PlatformAdminWebAppFactory>
{
    private const string Settings = "/Settings/Business";
    private const string RequestUrl = "/Settings/RequestDeletion";
    private const string ConfirmUrl = "/Settings/ConfirmDeletionRequest";
    private const string CancelUrl = "/Settings/CancelDeletionRequest";

    private readonly PlatformAdminWebAppFactory _factory;

    public TenantDeletionTests(PlatformAdminWebAppFactory factory)
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

    private async Task<(Guid Id, string Email, string Name)> AddTenantAsync(bool requested = false)
    {
        var id = Guid.NewGuid();
        var email = $"tenant-{id:N}@test.com";
        var name = $"Biz {id:N}";
        await TestData.EnsureTenantWithUserAsync(_factory, id, email);
        await TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.SingleAsync(b => b.Id == id);
            business.Name = name;
            if (requested)
            {
                business.DeletionRequestedAt = new DateTime(2026, 3, 10, 12, 0, 0);
                business.DeletionRequestReason = "We are closing";
            }
            return await db.SaveChangesAsync();
        });
        return (id, email, name);
    }

    private Task<Business?> GetBusinessAsync(Guid id) =>
        TestData.WithDbAsync(_factory, db => db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id));

    private Task<List<AdminActionLog>> GetLogAsync(Guid id) =>
        TestData.WithDbAsync(_factory, db => db.AdminActionLogs.AsNoTracking().Where(l => l.BusinessId == id).ToListAsync());

    private static string Details(Guid id) => $"/Admin/Tenants/Details/{id}";
    private static string Approve(Guid id) => $"/Admin/Tenants/Approve/{id}";
    private static string Dismiss(Guid id) => $"/Admin/Tenants/Dismiss/{id}";
    private static string Delete(Guid id) => $"/Admin/Tenants/Delete/{id}";

    private static Dictionary<string, string> Fields(params (string Key, string Value)[] fields) =>
        fields.ToDictionary(f => f.Key, f => f.Value);

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

    // ---------------- the tenant's request ----------------

    [Fact]
    public async Task Settings_OfferTheRequest_WhenThereIsNone()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);

        var html = await HtmlAsync(client, Settings);

        html.Should().Contain("Request account deletion").And.Contain(RequestUrl);
        html.Should().NotContain("pending review");
    }

    [Fact]
    public async Task ChoosingToRequest_OnlyShowsTheConfirmation_AndRequestsNothing()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);
        var emailsBefore = _factory.Emails.SentTo(PlatformAdminWebAppFactory.AdminEmail).Count;

        var response = await client.PostFormAsync(RequestUrl, RequestUrl, Fields(("Reason", "We are closing")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Are you sure?").And.Contain(ConfirmUrl);
        (await GetBusinessAsync(tenant.Id))!.DeletionRequestedAt.Should().BeNull();
        _factory.Emails.SentTo(PlatformAdminWebAppFactory.AdminEmail).Should().HaveCount(emailsBefore);
    }

    [Fact]
    public async Task Confirming_SavesTheRequest_ShowsItAsPending_AndEmailsTheAdmin()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);

        var response = await client.PostFormAsync(RequestUrl, ConfirmUrl, Fields(("Reason", "We are closing")));

        ShouldRedirectTo(response, Settings);
        var business = await GetBusinessAsync(tenant.Id);
        business!.DeletionRequestedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        business.DeletionRequestReason.Should().Be("We are closing");

        var html = await HtmlAsync(client, Settings);
        var today = AppointMe.Domain.TimeZones.BusinessTimeZones.Now(TimeProvider.System, business.TimeZoneId);
        html.Should().Contain($"Deletion requested on {today:d MMM yyyy}, pending review");
        html.Should().Contain(CancelUrl);
        html.Should().NotContain("Request account deletion");
        html.Should().Contain("sent for review");

        _factory.Emails.SentTo(PlatformAdminWebAppFactory.AdminEmail)
            .Should().Contain(m => m.Content.Contains(tenant.Name) && m.Content.Contains("We are closing"));
    }

    [Fact]
    public async Task TheReasonIsOptional()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);

        await client.PostFormAsync(RequestUrl, ConfirmUrl, Fields(("Reason", "")));

        var business = await GetBusinessAsync(tenant.Id);
        business!.DeletionRequestedAt.Should().NotBeNull();
        business.DeletionRequestReason.Should().BeNull();
    }

    [Fact]
    public async Task AReasonThatIsTooLong_IsRefused()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);
        var tooLong = new string('x', Business.MaxDeletionRequestReasonLength + 1);

        var response = await client.PostFormAsync(RequestUrl, ConfirmUrl, Fields(("Reason", tooLong)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("field-validation-error");
        (await GetBusinessAsync(tenant.Id))!.DeletionRequestedAt.Should().BeNull();
    }

    [Fact]
    public async Task RequestingAgain_IsRefused_AndKeepsTheFirst()
    {
        var tenant = await AddTenantAsync(requested: true);
        var client = await LoggedInAsync(tenant.Email);

        ShouldRedirectTo(await client.GetAsync(RequestUrl), Settings);

        var response = await client.PostFormAsync(Settings, ConfirmUrl, Fields(("Reason", "Second")));

        ShouldRedirectTo(response, Settings);
        (await GetBusinessAsync(tenant.Id))!.DeletionRequestReason.Should().Be("We are closing");
        (await HtmlAsync(client, Settings)).Should().Contain("already requested");
    }

    [Fact]
    public async Task Cancelling_ClearsTheRequest_AndItLeavesTheAdminsList()
    {
        var tenant = await AddTenantAsync(requested: true);
        var client = await LoggedInAsync(tenant.Email);
        var admin = await AdminClientAsync();
        (await HtmlAsync(admin, "/Admin")).Should().Contain(tenant.Name);

        var response = await client.PostFormAsync(Settings, CancelUrl, Fields());

        ShouldRedirectTo(response, Settings);
        var business = await GetBusinessAsync(tenant.Id);
        business!.DeletionRequestedAt.Should().BeNull();
        business.DeletionRequestReason.Should().BeNull();

        (await HtmlAsync(client, Settings)).Should().Contain("Request account deletion").And.Contain("cancelled");
        (await HtmlAsync(admin, "/Admin")).Should().NotContain(tenant.Name);
    }

    [Fact]
    public async Task CancellingWithoutARequest_IsRefusedPolitely()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);

        ShouldRedirectTo(await client.PostFormAsync(Settings, CancelUrl, Fields()), Settings);

        (await HtmlAsync(client, Settings)).Should().Contain("no pending deletion request");
    }

    [Fact]
    public async Task ARequestOnlyEverAffectsTheTenantsOwnBusiness_WhateverTheFormSays()
    {
        var tenant = await AddTenantAsync();
        var victim = await AddTenantAsync(requested: true);
        var client = await LoggedInAsync(tenant.Email);

        await client.PostFormAsync(RequestUrl, ConfirmUrl, Fields(("Reason", "Mine"), ("Id", victim.Id.ToString()), ("TenantId", victim.Id.ToString())));

        (await GetBusinessAsync(tenant.Id))!.DeletionRequestReason.Should().Be("Mine");
        (await GetBusinessAsync(victim.Id))!.DeletionRequestReason.Should().Be("We are closing");

        await client.PostFormAsync(Settings, CancelUrl, Fields(("Id", victim.Id.ToString())));

        (await GetBusinessAsync(tenant.Id))!.DeletionRequestedAt.Should().BeNull();
        (await GetBusinessAsync(victim.Id))!.DeletionRequestedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task SavingSettingsWithAnError_StillShowsThePendingRequest()
    {
        var tenant = await AddTenantAsync(requested: true);
        var client = await LoggedInAsync(tenant.Email);

        var response = await client.PostFormAsync(Settings, Settings, Fields(("Id", tenant.Id.ToString()), ("Name", "")));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("pending review");
    }

    [Fact]
    public async Task ASuspendedTenant_CantRequestOrCancel_ButTheRequestStays()
    {
        var tenant = await AddTenantAsync(requested: true);
        var client = await LoggedInAsync(tenant.Email);
        await TestData.WithDbAsync(_factory, async db =>
        {
            (await db.Businesses.SingleAsync(b => b.Id == tenant.Id)).Status = BusinessStatus.Suspended;
            return await db.SaveChangesAsync();
        });

        ShouldRedirectTo(await client.PostFormAsync("/Suspended", CancelUrl, Fields()), "/Suspended");
        ShouldRedirectTo(await client.PostFormAsync("/Suspended", ConfirmUrl, Fields(("Reason", "x"))), "/Suspended");

        (await GetBusinessAsync(tenant.Id))!.DeletionRequestReason.Should().Be("We are closing");
    }

    [Theory]
    [InlineData(RequestUrl)]
    public async Task TheAdmin_HasNoAccountToRequestDeletionFor(string url)
    {
        var client = await AdminClientAsync();

        ShouldRedirectTo(await client.GetAsync(url), "/Admin");
        ShouldRedirectTo(await client.PostFormAsync("/Admin", ConfirmUrl, Fields()), "/Admin");
    }

    [Fact]
    public async Task Anonymous_GoesToLogin()
    {
        ShouldRedirectTo(await NewClient().GetAsync(RequestUrl), "/Identity/Account/Login");
    }

    // ---------------- what the admin sees ----------------

    [Fact]
    public async Task AdminHome_ShowsTheBadgeAndTheRequests()
    {
        var tenant = await AddTenantAsync(requested: true);
        var pending = await TestData.WithDbAsync(_factory, db => db.Businesses.CountAsync(b => b.DeletionRequestedAt != null));
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, "/Admin");

        html.Should().Contain("Pending deletion requests");
        html.Should().Contain($"id=\"pending-deletions\">{pending}<");
        html.Should().Contain(tenant.Name).And.Contain(tenant.Email).And.Contain("We are closing");
        html.Should().Contain(Approve(tenant.Id)).And.Contain(Dismiss(tenant.Id));
    }

    [Fact]
    public async Task TheListAndDetails_MarkAPendingRequest()
    {
        var tenant = await AddTenantAsync(requested: true);
        var client = await AdminClientAsync();

        (await HtmlAsync(client, $"/Admin/Tenants?search={tenant.Name.Replace(" ", "%20")}")).Should().Contain("Deletion requested");
        var details = await HtmlAsync(client, Details(tenant.Id));
        details.Should().Contain("id=\"deletion-request\"").And.Contain("We are closing");
        details.Should().Contain(Approve(tenant.Id)).And.Contain(Dismiss(tenant.Id));
    }

    [Fact]
    public async Task Details_WithoutARequest_OfferOnlyTheDirectDelete()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var details = await HtmlAsync(client, Details(tenant.Id));

        details.Should().Contain(Delete(tenant.Id));
        details.Should().NotContain(Approve(tenant.Id)).And.NotContain(Dismiss(tenant.Id));
        ShouldRedirectTo(await client.GetAsync(Approve(tenant.Id)), Details(tenant.Id));
        ShouldRedirectTo(await client.GetAsync(Dismiss(tenant.Id)), Details(tenant.Id));
    }

    // ---------------- dismissing ----------------

    [Fact]
    public async Task Dismissing_ClearsTheRequest_KeepsTheTenant_AndEmailsThem()
    {
        var tenant = await AddTenantAsync(requested: true);
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Dismiss(tenant.Id), Dismiss(tenant.Id), Fields(("Note", "Talked to them")));

        ShouldRedirectTo(response, Details(tenant.Id));
        var business = await GetBusinessAsync(tenant.Id);
        business.Should().NotBeNull();
        business!.DeletionRequestedAt.Should().BeNull();

        var log = (await GetLogAsync(tenant.Id)).Should().ContainSingle().Which;
        log.Action.Should().Be(AdminActionType.DeletionRequestDismissed);
        log.AdminEmail.Should().Be(PlatformAdminWebAppFactory.AdminEmail);

        _factory.Emails.SentTo(tenant.Email).Should().ContainSingle().Which.Subject.Should().Contain("not approved");

        (await (await LoggedInAsync(tenant.Email)).GetAsync("/Customers")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ---------------- approving and deleting ----------------

    [Fact]
    public async Task ThePage_ShowsWhatGoes_AndAsksForTheName()
    {
        var tenant = await AddTenantAsync(requested: true);
        var client = await AdminClientAsync();

        var approve = await HtmlAsync(client, Approve(tenant.Id));
        approve.Should().Contain("be undone").And.Contain(tenant.Name).And.Contain("We are closing");
        approve.Should().Contain("ConfirmName");

        (await HtmlAsync(client, Delete(tenant.Id))).Should().Contain("ConfirmName");
    }

    [Theory]
    [InlineData("")]
    [InlineData("wrong name")]
    public async Task TheWrongName_DeletesNothing(string typed)
    {
        var tenant = await AddTenantAsync(requested: true);
        var client = await AdminClientAsync();

        foreach (var url in new[] { Approve(tenant.Id), Delete(tenant.Id) })
        {
            var response = await client.PostFormAsync(url, url, Fields(("ConfirmName", typed)));

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await response.Content.ReadAsStringAsync()).Should().Contain("field-validation-error");
        }

        (await GetBusinessAsync(tenant.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Approving_DeletesTheTenant_AndEverythingFollows()
    {
        var tenant = await AddTenantAsync(requested: true);
        var tenantClient = await LoggedInAsync(tenant.Email);
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Approve(tenant.Id), Approve(tenant.Id), Fields(("ConfirmName", tenant.Name)));

        ShouldRedirectTo(response, "/Admin/Tenants");
        (await GetBusinessAsync(tenant.Id)).Should().BeNull();

        using (var scope = _factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
            (await users.FindByEmailAsync(tenant.Email)).Should().BeNull();
        }

        _factory.Logos.Deleted.Should().Contain(tenant.Id);
        _factory.Emails.SentTo(tenant.Email).Should().ContainSingle().Which.Subject.Should().Contain("deleted");

        var list = await HtmlAsync(client, "/Admin/Tenants");
        list.Should().Contain("permanently deleted").And.NotContain(Details(tenant.Id));

        // Their open session ends, and they can't log in again.
        ShouldRedirectTo(await tenantClient.GetAsync("/Customers"), "/Identity/Account/Login");
        var login = await NewClient().PostLoginAsync(tenant.Email, TestData.Password);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        (await login.Content.ReadAsStringAsync()).Should().Contain("Invalid login attempt");

        // The log says who did it, and doesn't link to a tenant that is gone.
        var log = (await GetLogAsync(tenant.Id)).Should().ContainSingle().Which;
        log.Action.Should().Be(AdminActionType.DeletionRequestApproved);
        log.BusinessName.Should().Be(tenant.Name);
        log.AdminEmail.Should().Be(PlatformAdminWebAppFactory.AdminEmail);

        var logPage = await HtmlAsync(client, "/Admin/ActionLog");
        logPage.Should().Contain(tenant.Name).And.NotContain(Details(tenant.Id));
        logPage.Should().Contain(PlatformAdminWebAppFactory.AdminEmail);

        (await client.GetAsync(Details(tenant.Id))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeletingWithoutARequest_Works_WithTheSameConfirmation()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Delete(tenant.Id), Delete(tenant.Id),
            Fields(("ConfirmName", tenant.Name), ("Reason", "Fake business")));

        ShouldRedirectTo(response, "/Admin/Tenants");
        (await GetBusinessAsync(tenant.Id)).Should().BeNull();

        var log = (await GetLogAsync(tenant.Id)).Should().ContainSingle().Which;
        log.Action.Should().Be(AdminActionType.TenantDeleted);
        log.Reason.Should().Be("Fake business");
        log.AdminEmail.Should().Be(PlatformAdminWebAppFactory.AdminEmail);
    }

    [Fact]
    public async Task ApprovingAfterTheTenantCancelled_DeletesNothing()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(Details(tenant.Id), Approve(tenant.Id), Fields(("ConfirmName", tenant.Name)));

        ShouldRedirectTo(response, Details(tenant.Id));
        (await GetBusinessAsync(tenant.Id)).Should().NotBeNull();
        (await HtmlAsync(client, Details(tenant.Id))).Should().Contain("nothing was deleted");
    }

    [Fact]
    public async Task ASuspendedTenantsRequest_CanStillBeApproved()
    {
        var tenant = await AddTenantAsync(requested: true);
        await TestData.WithDbAsync(_factory, async db =>
        {
            (await db.Businesses.SingleAsync(b => b.Id == tenant.Id)).Status = BusinessStatus.Suspended;
            return await db.SaveChangesAsync();
        });
        var client = await AdminClientAsync();

        await client.PostFormAsync(Approve(tenant.Id), Approve(tenant.Id), Fields(("ConfirmName", tenant.Name)));

        (await GetBusinessAsync(tenant.Id)).Should().BeNull();
    }

    [Fact]
    public async Task DeletingUsesTheIdFromTheUrl_NotAnIdInTheForm()
    {
        var target = await AddTenantAsync();
        var other = await AddTenantAsync();
        var client = await AdminClientAsync();

        await client.PostFormAsync(Delete(target.Id), Delete(target.Id), Fields(("ConfirmName", target.Name), ("Id", other.Id.ToString())));

        (await GetBusinessAsync(target.Id)).Should().BeNull();
        (await GetBusinessAsync(other.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task DeletingOneTenant_LeavesAnotherUsable()
    {
        var doomed = await AddTenantAsync();
        var other = await AddTenantAsync();
        var otherClient = await LoggedInAsync(other.Email);
        var client = await AdminClientAsync();

        await client.PostFormAsync(Delete(doomed.Id), Delete(doomed.Id), Fields(("ConfirmName", doomed.Name)));

        (await otherClient.GetAsync("/Customers")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetBusinessAsync(other.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task WhenTheEmailAndLogoFail_TheTenantIsStillDeleted_AndTheAdminIsWarned()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        _factory.Emails.Fail = true;
        _factory.Logos.Fail = true;
        try
        {
            await client.PostFormAsync(Delete(tenant.Id), Delete(tenant.Id), Fields(("ConfirmName", tenant.Name)));
        }
        finally
        {
            _factory.Emails.Fail = false;
            _factory.Logos.Fail = false;
        }

        (await GetBusinessAsync(tenant.Id)).Should().BeNull();
        var html = await HtmlAsync(client, "/Admin/Tenants");
        html.Should().Contain("could not be sent").And.Contain("logo file could not be removed");
    }

    [Fact]
    public async Task WithoutAnAntiforgeryToken_NothingHappens()
    {
        var tenant = await AddTenantAsync(requested: true);
        var client = await AdminClientAsync();

        foreach (var url in new[] { Approve(tenant.Id), Delete(tenant.Id), Dismiss(tenant.Id) })
        {
            var response = await client.PostAsync(url, new FormUrlEncodedContent(Fields(("ConfirmName", tenant.Name))));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        (await GetBusinessAsync(tenant.Id))!.DeletionRequestedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task AnUnknownTenant_Is404()
    {
        var tenant = await AddTenantAsync();
        var unknown = Guid.NewGuid();
        var client = await AdminClientAsync();

        foreach (var url in new[] { Approve(unknown), Delete(unknown), Dismiss(unknown) })
        {
            (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
            (await client.PostFormAsync(Details(tenant.Id), url, Fields(("ConfirmName", "x")))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    // ---------------- who can do this ----------------

    [Fact]
    public async Task ATenant_CantApproveTheirOwnRequest_OrDeleteOrDismissAnyTenant()
    {
        var tenant = await AddTenantAsync(requested: true);
        var victim = await AddTenantAsync(requested: true);
        var client = await LoggedInAsync(tenant.Email);

        foreach (var (id, name) in new[] { (tenant.Id, tenant.Name), (victim.Id, victim.Name) })
        {
            foreach (var url in new[] { Approve(id), Delete(id), Dismiss(id) })
            {
                ShouldRedirectTo(await client.GetAsync(url), "/Identity/Account/AccessDenied");
                ShouldRedirectTo(await client.PostFormAsync(Settings, url, Fields(("ConfirmName", name))), "/Identity/Account/AccessDenied");
            }

            var business = await GetBusinessAsync(id);
            business.Should().NotBeNull();
            business!.DeletionRequestedAt.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Anonymous_CantDeleteATenant()
    {
        var tenant = await AddTenantAsync(requested: true);

        foreach (var url in new[] { Approve(tenant.Id), Delete(tenant.Id), Dismiss(tenant.Id) })
        {
            var response = await NewClient().PostAsync(url, new FormUrlEncodedContent(Fields(("ConfirmName", tenant.Name))));
            ShouldRedirectTo(response, "/Identity/Account/Login");
        }

        (await GetBusinessAsync(tenant.Id)).Should().NotBeNull();
    }

    // ---------------- the built-in page that would skip all this ----------------

    private async Task ShouldBeBlockedAsync(HttpClient client, string email)
    {
        const string url = "/Identity/Account/Manage/DeletePersonalData";

        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var post = await client.PostFormAsync("/Identity/Account/Manage/PersonalData", url, Fields(("Input.Password", TestData.Password)));
        post.StatusCode.Should().Be(HttpStatusCode.NotFound);

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        (await users.FindByEmailAsync(email)).Should().NotBeNull();
    }

    [Fact]
    public async Task DeletePersonalData_IsBlocked_ForATenant()
    {
        var tenant = await AddTenantAsync();

        await ShouldBeBlockedAsync(await LoggedInAsync(tenant.Email), tenant.Email);
    }

    [Fact]
    public async Task DeletePersonalData_IsBlocked_ForTheAdmin()
    {
        await ShouldBeBlockedAsync(await AdminClientAsync(), PlatformAdminWebAppFactory.AdminEmail);
    }

    [Fact]
    public async Task PersonalDataPage_HasNoDeleteLink_AndDownloadStillWorks()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);

        var html = await HtmlAsync(client, "/Identity/Account/Manage/PersonalData");

        html.Should().NotContain("DeletePersonalData");
        html.Should().Contain("/Identity/Account/Manage/DownloadPersonalData");
        html.Should().Contain(Settings);

        var download = await client.PostFormAsync("/Identity/Account/Manage/PersonalData", "/Identity/Account/Manage/DownloadPersonalData", Fields());
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsStringAsync()).Should().Contain(tenant.Email);
    }

    [Fact]
    public async Task PersonalDataPage_TellsTheAdmin_TheirAccountCantBeDeletedThere()
    {
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, "/Identity/Account/Manage/PersonalData");

        html.Should().Contain("Admin accounts can't be deleted here.");
        html.Should().NotContain("To delete your account");
        html.Should().Contain("/Identity/Account/Manage/DownloadPersonalData");
    }

    [Fact]
    public async Task PersonalDataPage_TellsATenant_ToUseBusinessSettings()
    {
        var tenant = await AddTenantAsync();
        var client = await LoggedInAsync(tenant.Email);

        var html = await HtmlAsync(client, "/Identity/Account/Manage/PersonalData");

        html.Should().Contain("To delete your account");
        html.Should().NotContain("Admin accounts");
    }

    [Fact]
    public async Task PersonalDataPage_ForAnonymous_GoesToLogin()
    {
        ShouldRedirectTo(await NewClient().GetAsync("/Identity/Account/Manage/PersonalData"), "/Identity/Account/Login");
    }
}
