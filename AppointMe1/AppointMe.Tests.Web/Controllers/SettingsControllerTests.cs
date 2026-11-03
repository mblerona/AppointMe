using System.Net;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>Settings/Business through IBusinessService (Batch 2 step 3). Uses its own tenants, not the default one.</summary>
public class SettingsControllerTests : IClassFixture<CustomWebAppFactory>
{
    private static readonly Guid Tenant = Guid.Parse("5e771165-0000-0000-0000-000000000001");
    private static readonly Guid OtherTenant = Guid.Parse("5e771165-0000-0000-0000-000000000002");
    private const string Email = "settings-owner@test.com";

    private readonly CustomWebAppFactory _factory;

    public SettingsControllerTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsync()
    {
        await TestData.EnsureTenantWithUserAsync(_factory, Tenant, Email);
        await TestData.EnsureTenantWithUserAsync(_factory, OtherTenant, "settings-other@test.com");

        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(Email, TestData.Password);
        return client;
    }

    private static Dictionary<string, string> Form(Guid id, string name, string start = "09:00", string end = "17:00") => new()
    {
        ["Id"] = id.ToString(),
        ["Name"] = name,
        ["DefaultSlotMinutes"] = "45",
        ["WorkDayStart"] = start,
        ["WorkDayEnd"] = end,
        ["PrimaryColor"] = "ABC",
        ["EnableServices"] = "true",
        ["OpenMon"] = "true",
        // A browser always posts every checkbox (asp-for adds a hidden "false"); the VM defaults OpenTue to true.
        ["OpenTue"] = "false"
    };

    [Fact]
    public async Task Get_ShowsTheTenantsSettings()
    {
        var client = await LoginAsync();

        var res = await client.GetAsync("/Settings/Business");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        // The other tests rename the business, so check the tenant id in the hidden field instead of the name.
        (await res.Content.ReadAsStringAsync()).Should().Contain(Tenant.ToString());
    }

    [Fact]
    public async Task Post_SavesThroughTheService_AndRedirects()
    {
        var client = await LoginAsync();

        var res = await client.PostFormAsync("/Settings/Business", "/Settings/Business", Form(Tenant, "  Renamed salon "));

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var b = await TestData.WithDbAsync(_factory, db => db.Businesses.AsNoTracking().SingleAsync(x => x.Id == Tenant));
        b.Name.Should().Be("Renamed salon");
        b.PrimaryColor.Should().Be("#abc");
        b.DefaultSlotMinutes.Should().Be(45);
        b.OpenTue.Should().BeFalse();
    }

    [Fact]
    public async Task Post_WithWorkDayEndBeforeStart_ShowsTheError_AndSavesNothing()
    {
        var client = await LoginAsync();
        var before = await TestData.WithDbAsync(_factory, db => db.Businesses.AsNoTracking().SingleAsync(x => x.Id == Tenant));

        var res = await client.PostFormAsync("/Settings/Business", "/Settings/Business",
            Form(Tenant, "Should not be saved", start: "17:00", end: "09:00"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Work day end must be after start.");
        var after = await TestData.WithDbAsync(_factory, db => db.Businesses.AsNoTracking().SingleAsync(x => x.Id == Tenant));
        after.Name.Should().Be(before.Name);
    }

    [Fact]
    public async Task Post_WithAnotherTenantsId_IsForbidden_AndChangesNeitherBusiness()
    {
        var client = await LoginAsync();

        var res = await client.PostFormAsync("/Settings/Business", "/Settings/Business", Form(OtherTenant, "Hijacked"));

        res.StatusCode.Should().Be(HttpStatusCode.Redirect, "cookie auth answers Forbid with a redirect to AccessDenied");
        res.Headers.Location!.ToString().Should().Contain("AccessDenied");
        (await TestData.WithDbAsync(_factory, db => db.Businesses.AnyAsync(b => b.Name == "Hijacked"))).Should().BeFalse();
    }
}
