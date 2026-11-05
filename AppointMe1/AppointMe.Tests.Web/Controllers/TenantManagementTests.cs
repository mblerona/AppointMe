using System.Net;
using System.Text.RegularExpressions;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

public class TenantManagementTests : IClassFixture<PlatformAdminWebAppFactory>
{
    private static readonly Guid DefaultTenant = CustomWebAppFactory.DefaultTenantId;

    private readonly PlatformAdminWebAppFactory _factory;

    public TenantManagementTests(PlatformAdminWebAppFactory factory)
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

    private async Task<HttpClient> TenantClientAsync()
    {
        var client = NewClient();
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);
        return client;
    }

    /// <summary>A new tenant with one user. The name is unique, so a search for it finds only this tenant.</summary>
    private async Task<(Guid Id, string Name, string OwnerEmail)> AddTenantAsync(string prefix = "Biz", DateTime? createdAt = null)
    {
        var id = Guid.NewGuid();
        var unique = id.ToString("N")[..12];
        var name = $"{prefix} {unique}";
        var email = $"owner-{unique}@test.com";
        await TestData.EnsureTenantWithUserAsync(_factory, id, email);

        await TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.SingleAsync(b => b.Id == id);
            business.Name = name;
            if (createdAt != null) business.CreatedAt = createdAt.Value;
            return await db.SaveChangesAsync();
        });

        return (id, name, email);
    }

    private Task AddUsageAsync(Guid tenantId, int customers, int appointments, int invoices) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var customerIds = Enumerable.Range(0, customers).Select(_ => Guid.NewGuid()).ToList();
            for (var i = 0; i < customers; i++)
            {
                db.Customers.Add(new Customer
                {
                    Id = customerIds[i], TenantId = tenantId, CustomerNumber = i + 1, FirstName = "Hidden", LastName = "Person",
                    Email = $"{Guid.NewGuid():N}@test.com", PhoneNumber = "+38970123456", State = "MK", City = "Skopje"
                });
            }
            for (var i = 0; i < appointments; i++)
            {
                db.Appointments.Add(new Appointment
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerIds[0], OrderNumber = $"ORD-{i}",
                    Description = "Hidden", AppointmentDate = DateTime.UtcNow
                });
            }
            for (var i = 0; i < invoices; i++)
            {
                db.Invoices.Add(new Invoice
                {
                    Id = Guid.NewGuid(), TenantId = tenantId, AppointmentId = Guid.NewGuid(), CustomerId = customerIds[0],
                    InvoiceNumber = $"INV-{i}", InvoiceYear = 2026, InvoiceSequence = i + 1,
                    CustomerNameSnapshot = "Hidden Person", BusinessNameSnapshot = "Biz"
                });
            }
            return await db.SaveChangesAsync();
        });

    private Task<Business> GetBusinessAsync(Guid id) =>
        TestData.WithDbAsync(_factory, db => db.Businesses.AsNoTracking().SingleAsync(b => b.Id == id));

    private static string Search(string term) => $"/Admin/Tenants?search={Uri.EscapeDataString(term)}";

    private static async Task<string> HtmlAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, url);
        return await response.Content.ReadAsStringAsync();
    }

    private static Dictionary<string, string> EditFields(
        string name = "Edited name", string email = "", string phone = "", string address = "") => new()
    {
        ["Name"] = name,
        ["Email"] = email,
        ["Phone"] = phone,
        ["Address"] = address
    };

    private static string EditUrl(Guid id) => $"/Admin/Tenants/Edit/{id}";

    // ---------------- list ----------------

    [Fact]
    public async Task List_ShowsEveryTenant_WithNameOwnerDateAndStatus()
    {
        var first = await AddTenantAsync("First", new DateTime(2026, 3, 10, 12, 0, 0));
        var second = await AddTenantAsync("Second");
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, "/Admin/Tenants");

        html.Should().Contain(first.Name).And.Contain(first.OwnerEmail).And.Contain("10 Mar 2026");
        html.Should().Contain(second.Name).And.Contain(second.OwnerEmail);
        html.Should().Contain("Test Business", "the seeded tenant is listed too");
        html.Should().Contain("text-bg-success");

        var total = await TestData.WithDbAsync(_factory, db => db.Businesses.CountAsync());
        Regex.Matches(html, "/Admin/Tenants/Details/").Count.Should().Be(total);
    }

    [Fact]
    public async Task List_IsNewestFirst()
    {
        var older = await AddTenantAsync("Order", DateTime.UtcNow.AddDays(10));
        var newer = await AddTenantAsync("Order", DateTime.UtcNow.AddDays(20));
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, "/Admin/Tenants");

        html.IndexOf(newer.Name, StringComparison.Ordinal).Should().BeLessThan(html.IndexOf(older.Name, StringComparison.Ordinal));
    }

    [Fact]
    public async Task List_ShowsTheCorrectCountsForEachTenant()
    {
        var tenant = await AddTenantAsync();
        await AddUsageAsync(tenant.Id, customers: 2, appointments: 3, invoices: 1);
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, Search(tenant.Name));

        html.Should().MatchRegex(@"text-end"">2</td>\s*<td class=""text-end"">3</td>\s*<td class=""text-end"">1</td>");
    }

    [Fact]
    public async Task List_FindsATenantByNameOrOwnerEmail_AndNoOtherTenant()
    {
        var wanted = await AddTenantAsync("Wanted");
        var other = await AddTenantAsync("Other");
        var client = await AdminClientAsync();

        foreach (var term in new[] { wanted.Name, wanted.Name.ToUpperInvariant(), wanted.OwnerEmail })
        {
            var html = await HtmlAsync(client, Search(term));
            html.Should().Contain(wanted.Name);
            html.Should().NotContain(other.Name);
        }
    }

    [Fact]
    public async Task List_WithNoMatch_SaysSo_AndKeepsTheSearchTerm()
    {
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, Search("no-such-tenant-at-all"));

        html.Should().Contain("No tenants found");
        html.Should().Contain("value=\"no-such-tenant-at-all\"");
    }

    [Fact]
    public async Task StatusFilter_WorksAndKeepsTheSelection()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var active = await HtmlAsync(client, "/Admin/Tenants?status=Active");
        active.Should().Contain(tenant.Name);
        active.Should().MatchRegex(@"<option selected=""selected"" value=""Active""|<option value=""Active"" selected=""selected""");

        var suspended = await HtmlAsync(client, "/Admin/Tenants?status=Suspended");
        suspended.Should().Contain("No tenants found");

        (await HtmlAsync(client, "/Admin/Tenants?status=NotAStatus")).Should().Contain(tenant.Name);
    }

    [Fact]
    public async Task List_EncodesBusinessNames()
    {
        var tenant = await AddTenantAsync();
        const string attack = "<script>alert(1)</script>";
        await TestData.WithDbAsync(_factory, async db =>
        {
            (await db.Businesses.SingleAsync(b => b.Id == tenant.Id)).Name = attack;
            return await db.SaveChangesAsync();
        });
        var client = await AdminClientAsync();

        (await HtmlAsync(client, "/Admin/Tenants")).Should().NotContain(attack);
        (await HtmlAsync(client, $"/Admin/Tenants/Details/{tenant.Id}")).Should().NotContain(attack);

        await TestData.WithDbAsync(_factory, async db =>
        {
            (await db.Businesses.SingleAsync(b => b.Id == tenant.Id)).Name = tenant.Name;
            return await db.SaveChangesAsync();
        });
    }

    // ---------------- details ----------------

    [Fact]
    public async Task Details_ShowContactInformationAndCorrectCounts()
    {
        var tenant = await AddTenantAsync();
        await AddUsageAsync(tenant.Id, customers: 2, appointments: 3, invoices: 1);
        await TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.SingleAsync(b => b.Id == tenant.Id);
            business.Email = "contact@details.test";
            business.Phone = "+38970123456";
            business.Address = "Partizanska 1";
            return await db.SaveChangesAsync();
        });
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, $"/Admin/Tenants/Details/{tenant.Id}");

        html.Should().Contain(tenant.Name).And.Contain(tenant.OwnerEmail);
        html.Should().Contain("contact@details.test").And.Contain("Partizanska 1").And.Contain("070 123 456");
        html.Should().Contain("id=\"customer-count\">2<");
        html.Should().Contain("id=\"appointment-count\">3<");
        html.Should().Contain("id=\"invoice-count\">1<");
    }

    [Theory]
    [InlineData("/Admin/Tenants")]
    [InlineData("/Admin/Tenants/Details/11111111-1111-1111-1111-111111111111")]
    [InlineData("/Admin/Tenants/Edit/11111111-1111-1111-1111-111111111111")]
    public async Task AdminPages_NeverShowCustomerDetailsOrAppointmentContents(string url)
    {
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, url);

        html.Should().NotContain("ana@test.com");
        html.Should().NotContain("070123456");
        html.Should().NotContain("ORD-0001");
        html.Should().NotContain("Seed appointment");
        html.Should().NotContain("INV-");
    }

    [Theory]
    [InlineData("/Admin/Tenants/Details/99999999-9999-9999-9999-999999999999")]
    [InlineData("/Admin/Tenants/Edit/99999999-9999-9999-9999-999999999999")]
    [InlineData("/Admin/Tenants/Details/not-a-guid")]
    public async Task UnknownTenant_Is404(string url)
    {
        var client = await AdminClientAsync();

        (await client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---------------- home ----------------

    [Fact]
    public async Task Home_ShowsPlatformTotals()
    {
        await AddTenantAsync("Recent");
        await AddTenantAsync("Older", DateTime.UtcNow.AddDays(-60));
        var since = DateTime.UtcNow.AddDays(-30);
        var (total, recent) = await TestData.WithDbAsync(_factory, async db =>
            (await db.Businesses.CountAsync(), await db.Businesses.CountAsync(b => b.CreatedAt >= since)));
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, "/Admin");

        html.Should().Contain($"id=\"total-tenants\">{total}<");
        html.Should().Contain($"id=\"active-tenants\">{total}<");
        html.Should().Contain("id=\"suspended-tenants\">0<");
        html.Should().Contain($"id=\"new-tenants\">{recent}<");
    }

    // ---------------- edit ----------------

    [Fact]
    public async Task Edit_FormShowsTheCurrentValues()
    {
        var tenant = await AddTenantAsync();
        await TestData.WithDbAsync(_factory, async db =>
        {
            var business = await db.Businesses.SingleAsync(b => b.Id == tenant.Id);
            business.Email = "current@test.com";
            business.Phone = "+38970123456";
            business.Address = "Current address";
            return await db.SaveChangesAsync();
        });
        var client = await AdminClientAsync();

        var html = await HtmlAsync(client, EditUrl(tenant.Id));

        html.Should().Contain($"value=\"{tenant.Name}\"");
        html.Should().Contain("value=\"current@test.com\"");
        html.Should().Contain("value=\"070 123 456\"");
        html.Should().Contain("value=\"Current address\"");
    }

    [Fact]
    public async Task Edit_SavesAndReturnsToDetails_WithAMessage()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(EditUrl(tenant.Id), EditUrl(tenant.Id),
            EditFields("Saved name", "saved@test.com", "070 123 456", "Saved address"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Be($"/Admin/Tenants/Details/{tenant.Id}");

        var saved = await GetBusinessAsync(tenant.Id);
        saved.Name.Should().Be("Saved name");
        saved.Email.Should().Be("saved@test.com");
        saved.Phone.Should().Be("+38970123456");
        saved.Address.Should().Be("Saved address");

        (await HtmlAsync(client, response.Headers.Location.ToString())).Should().Contain("Business details saved.");
    }

    [Fact]
    public async Task Edit_DoesNotChangeTheOwnersLoginEmail()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        await client.PostFormAsync(EditUrl(tenant.Id), EditUrl(tenant.Id), EditFields(email: "business@test.com"));

        using var scope = _factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();
        (await users.FindByEmailAsync(tenant.OwnerEmail)).Should().NotBeNull();
    }

    public static TheoryData<string, string, string, string> InvalidEdits => new()
    {
        { "", "", "", "" },
        { "   ", "", "", "" },
        { new string('x', 201), "", "", "" },
        { "Name", "not-an-email", "", "" },
        { "Name", "", "abc", "" },
        { "Name", "", "", new string('x', 501) }
    };

    [Theory]
    [MemberData(nameof(InvalidEdits))]
    public async Task Edit_WithAnInvalidValue_ShowsTheFormAgain_AndSavesNothing(
        string name, string email, string phone, string address)
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var response = await client.PostFormAsync(EditUrl(tenant.Id), EditUrl(tenant.Id), EditFields(name, email, phone, address));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("field-validation-error");

        var saved = await GetBusinessAsync(tenant.Id);
        saved.Name.Should().Be(tenant.Name);
        saved.Email.Should().BeNull();
    }

    [Fact]
    public async Task Edit_UsesTheIdFromTheUrl_NotAnIdInTheForm()
    {
        var target = await AddTenantAsync();
        var other = await AddTenantAsync();
        var client = await AdminClientAsync();

        var fields = EditFields("Changed target");
        fields["Id"] = other.Id.ToString();
        await client.PostFormAsync(EditUrl(target.Id), EditUrl(target.Id), fields);

        (await GetBusinessAsync(target.Id)).Name.Should().Be("Changed target");
        (await GetBusinessAsync(other.Id)).Name.Should().Be(other.Name);
    }

    [Fact]
    public async Task Edit_OfAnUnknownTenant_Is404()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();
        var unknown = EditUrl(Guid.NewGuid());

        var response = await client.PostFormAsync(EditUrl(tenant.Id), unknown, EditFields());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Edit_WithoutAnAntiforgeryToken_IsRejected()
    {
        var tenant = await AddTenantAsync();
        var client = await AdminClientAsync();

        var response = await client.PostAsync(EditUrl(tenant.Id), new FormUrlEncodedContent(EditFields()));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetBusinessAsync(tenant.Id)).Name.Should().Be(tenant.Name);
    }

    // ---------------- who can reach these pages ----------------

    public static TheoryData<string> AdminUrls => new()
    {
        "/Admin/Tenants",
        "/Admin/Tenants/Index",
        "/Admin/Tenants?search=a",
        $"/Admin/Tenants/Details/{DefaultTenant}",
        $"/Admin/Tenants/Edit/{DefaultTenant}"
    };

    [Theory]
    [MemberData(nameof(AdminUrls))]
    public async Task Tenant_IsRedirectedToAccessDenied_EvenForTheirOwnBusiness(string url)
    {
        var client = await TenantClientAsync();

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Identity/Account/AccessDenied");
    }

    [Theory]
    [MemberData(nameof(AdminUrls))]
    public async Task Anonymous_IsRedirectedToLogin(string url)
    {
        var response = await NewClient().GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Identity/Account/Login");
    }

    [Fact]
    public async Task Tenant_PostingAnEdit_ChangesNothing()
    {
        var client = await TenantClientAsync();

        var response = await client.PostFormAsync("/Settings/Business", EditUrl(DefaultTenant), EditFields("Hacked"));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Identity/Account/AccessDenied");
        (await GetBusinessAsync(DefaultTenant)).Name.Should().Be("Test Business");
    }

    [Fact]
    public async Task Anonymous_PostingAnEdit_ChangesNothing()
    {
        var response = await NewClient().PostAsync(EditUrl(DefaultTenant), new FormUrlEncodedContent(EditFields("Hacked")));

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.ToString().Should().Contain("/Identity/Account/Login");
        (await GetBusinessAsync(DefaultTenant)).Name.Should().Be("Test Business");
    }
}
