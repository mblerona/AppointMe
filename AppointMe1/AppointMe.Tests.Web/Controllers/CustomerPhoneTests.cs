using System.Net;
using AppointMe.Domain.Validation;
using AppointMe.Tests.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>Phone numbers through the customer forms: validated, stored in E.164, shown readably, searchable.</summary>
public class CustomerPhoneTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public CustomerPhoneTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoggedInClientAsync()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);
        return client;
    }

    private static Dictionary<string, string> CustomerForm(string email, string phone, string second = "") => new()
    {
        ["FirstName"] = "Phone",
        ["LastName"] = "Test",
        ["Email"] = email,
        ["PhoneNumber"] = phone,
        ["SecondPhoneNumber"] = second,
        ["City"] = "Skopje",
        ["State"] = "North Macedonia"
    };

    private static string Html(string text) => System.Text.Encodings.Web.HtmlEncoder.Default.Encode(text);

    private Task<AppointMe.Domain.DomainModels.Customer?> FindAsync(string email) =>
        TestData.WithDbAsync(_factory, db => db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Email == email));

    [Fact]
    public async Task Create_StoresE164_AndTheListShowsTheReadableForm()
    {
        const string email = "phone-ok@test.com";
        var client = await LoggedInClientAsync();

        var res = await client.PostFormAsync("/Customers/Create", "/Customers/Create",
            CustomerForm(email, "070/555-123", "+49 30 1234567"));

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var stored = await FindAsync(email);
        stored!.PhoneNumber.Should().Be("+38970555123");
        stored.SecondPhoneNumber.Should().Be("+49301234567");

        var list = await (await client.GetAsync("/Customers")).Content.ReadAsStringAsync();
        list.Should().Contain("070 555 123");

        var details = await (await client.GetAsync($"/Customers/Details/{stored.Id}")).Content.ReadAsStringAsync();
        // Razor HTML-encodes "+" as "&#x2B;" (browsers decode it), so compare with the encoded form.
        details.Should().Contain($"href=\"tel:{Html("+38970555123")}\"");
        details.Should().Contain(Html("+49 30 1234567"));
    }

    [Fact]
    public async Task Create_WithInvalidPhone_ShowsTheError_AndSavesNothing()
    {
        const string email = "phone-bad@test.com";
        var client = await LoggedInClientAsync();

        var res = await client.PostFormAsync("/Customers/Create", "/Customers/Create", CustomerForm(email, "12345"));

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain("Enter a valid phone number");
        (await FindAsync(email)).Should().BeNull();
    }

    [Fact]
    public async Task EditForm_ShowsTheReadableNumber_AndSavingKeepsE164()
    {
        const string email = "phone-edit@test.com";
        var client = await LoggedInClientAsync();
        await client.PostFormAsync("/Customers/Create", "/Customers/Create", CustomerForm(email, "070 444 333"));
        var id = (await FindAsync(email))!.Id;

        var edit = await (await client.GetAsync($"/Customers/Edit/{id}")).Content.ReadAsStringAsync();
        edit.Should().Contain("value=\"070 444 333\"");

        var save = await client.PostFormAsync($"/Customers/Edit/{id}", $"/Customers/Edit/{id}",
            CustomerForm(email, "070 444 333"));

        save.StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await FindAsync(email))!.PhoneNumber.Should().Be("+38970444333");
    }

    [Fact]
    public async Task Search_ByPhoneInAnyFormat_FindsTheCustomer()
    {
        const string email = "phone-search@test.com";
        var client = await LoggedInClientAsync();
        await client.PostFormAsync("/Customers/Create", "/Customers/Create", CustomerForm(email, "+389 71 222 111"));

        var html = await (await client.GetAsync("/Customers?searchTerm=071%20222%20111")).Content.ReadAsStringAsync();

        html.Should().Contain(email);
    }

    [Fact]
    public async Task BusinessSettings_InvalidPhone_ShowsTheError()
    {
        var client = await LoggedInClientAsync();

        var res = await client.PostFormAsync("/Settings/Business", "/Settings/Business", new Dictionary<string, string>
        {
            ["Id"] = CustomWebAppFactory.DefaultTenantId.ToString(),
            ["Name"] = "Test Business",
            ["Phone"] = "123",
            ["DefaultSlotMinutes"] = "30",
            ["WorkDayStart"] = "09:00",
            ["WorkDayEnd"] = "17:00"
        });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        (await res.Content.ReadAsStringAsync()).Should().Contain(Html(PhoneNumberNormalizer.InvalidMessage));
    }
}
