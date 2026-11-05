using System.Net;
using System.Text.RegularExpressions;
using AppointMe.Domain.DomainModels;
using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>The optional service duration, through the real service forms and list.</summary>
public class ServiceDurationTests : IClassFixture<HolidaysWebAppFactory>
{
    private static readonly Guid Tenant = Guid.Parse("0de70000-0000-0000-0000-00000000000a");
    private static readonly Guid OtherTenant = Guid.Parse("0de70000-0000-0000-0000-00000000000b");
    private const string Email = "svc-duration@test.com";
    private const string OtherEmail = "svc-duration-other@test.com";

    private readonly HolidaysWebAppFactory _factory;

    public ServiceDurationTests(HolidaysWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoginAsync(Guid tenantId = default, string email = Email)
    {
        await TestData.EnsureTenantWithUserAsync(_factory, tenantId == default ? Tenant : tenantId, email);
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(email, TestData.Password);
        return client;
    }

    private Task<Guid> AddServiceAsync(string name, int? durationMinutes, Guid tenantId = default) =>
        TestData.WithDbAsync(_factory, async db =>
        {
            var service = new ServiceOffering
            {
                Id = Guid.NewGuid(), BusinessId = tenantId == default ? Tenant : tenantId,
                Name = name, Price = 100, DurationMinutes = durationMinutes
            };
            db.ServiceOfferings.Add(service);
            await db.SaveChangesAsync();
            return service.Id;
        });

    private Task<ServiceOffering?> FindAsync(string name) =>
        TestData.WithDbAsync(_factory, db => db.ServiceOfferings.AsNoTracking().FirstOrDefaultAsync(s => s.Name == name));

    private Task<int?> DurationOfAsync(Guid id) =>
        TestData.WithDbAsync(_factory, db => db.ServiceOfferings.Where(s => s.Id == id).Select(s => s.DurationMinutes).FirstAsync());

    private static Task<HttpResponseMessage> PostCreateAsync(HttpClient client, string name, string duration) =>
        client.PostFormAsync("/Services/Create", "/Services/Create", new Dictionary<string, string>
        {
            ["Name"] = name, ["Price"] = "100", ["DurationMinutes"] = duration, ["IsActive"] = "true"
        });

    private static Task<HttpResponseMessage> PostEditAsync(HttpClient client, Guid id, string name, string duration) =>
        client.PostFormAsync($"/Services/Edit/{id}", $"/Services/Edit/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["Name"] = name, ["Price"] = "100", ["DurationMinutes"] = duration, ["IsActive"] = "true"
        });

    [Theory]
    [InlineData("5", 5)]
    [InlineData("480", 480)]
    [InlineData("", null)]
    public async Task Create_InRangeOrEmpty_IsSaved(string input, int? expected)
    {
        var client = await LoginAsync();
        var name = $"Saved {Guid.NewGuid():N}";

        var response = await PostCreateAsync(client, name, input);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await FindAsync(name))!.DurationMinutes.Should().Be(expected);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("481")]
    public async Task Create_OutOfRange_ShowsTheErrorAndSavesNothing(string input)
    {
        var client = await LoginAsync();
        var name = $"Refused {Guid.NewGuid():N}";

        var response = await PostCreateAsync(client, name, input);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Duration must be between 5 and 480 minutes.");
        (await FindAsync(name)).Should().BeNull();
    }

    [Fact]
    public async Task EditForm_ShowsTheStoredDuration()
    {
        var client = await LoginAsync();
        var id = await AddServiceAsync($"Shown {Guid.NewGuid():N}", 45);

        var html = await (await client.GetAsync($"/Services/Edit/{id}")).Content.ReadAsStringAsync();

        var input = Regex.Match(html, "<input[^>]*name=\"DurationMinutes\"[^>]*>").Value;
        input.Should().Contain("value=\"45\"");
    }

    [Fact]
    public async Task Edit_ChangesAndClearsTheDuration()
    {
        var client = await LoginAsync();
        var name = $"Edited {Guid.NewGuid():N}";
        var id = await AddServiceAsync(name, 45);

        (await PostEditAsync(client, id, name, "90")).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await DurationOfAsync(id)).Should().Be(90);

        (await PostEditAsync(client, id, name, "")).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await DurationOfAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task Edit_OutOfRange_ShowsTheErrorAndKeepsTheOldDuration()
    {
        var client = await LoginAsync();
        var name = $"Kept {Guid.NewGuid():N}";
        var id = await AddServiceAsync(name, 45);

        var response = await PostEditAsync(client, id, name, "481");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Duration must be between 5 and 480 minutes.");
        (await DurationOfAsync(id)).Should().Be(45);
    }

    [Fact]
    public async Task Edit_AnotherTenantsService_Returns404AndKeepsItsDuration()
    {
        await TestData.EnsureTenantWithUserAsync(_factory, OtherTenant, OtherEmail);
        var name = $"Other {Guid.NewGuid():N}";
        var id = await AddServiceAsync(name, 45, OtherTenant);
        var client = await LoginAsync();

        // The other tenant's Edit page is a 404 too, so the token comes from this tenant's own form.
        var response = await client.PostFormAsync("/Services/Create", $"/Services/Edit/{id}", new Dictionary<string, string>
        {
            ["Id"] = id.ToString(), ["Name"] = name, ["Price"] = "100", ["DurationMinutes"] = "90", ["IsActive"] = "true"
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DurationOfAsync(id)).Should().Be(45);
    }

    [Fact]
    public async Task Index_ShowsMinutes_OrOneSlotWithoutADuration()
    {
        var tenant = Guid.Parse("0de70000-0000-0000-0000-00000000000c");
        var client = await LoginAsync(tenant, "svc-duration-index@test.com");
        await AddServiceAsync("A with duration", 45, tenant);
        await AddServiceAsync("B without duration", null, tenant);

        var html = await (await client.GetAsync("/Services")).Content.ReadAsStringAsync();

        var cells = Regex.Matches(html, "<td class=\"service-duration\">\\s*(.*?)\\s*</td>").Select(m => m.Groups[1].Value);
        cells.Should().Equal("45 min", "1 slot");
    }
}
