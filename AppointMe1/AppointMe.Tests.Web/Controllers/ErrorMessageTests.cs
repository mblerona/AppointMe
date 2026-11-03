using AppointMe.Tests.Web.Factories;
using AppointMe.Tests.Web.Infrastructure;
using AppointMe.Tests.Web.TestDoubles;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System.Net;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// When email delivery fails, users get a friendly message and never see exception details.
/// </summary>
public class ErrorMessageTests : IClassFixture<FailingEmailWebAppFactory>
{
    private readonly FailingEmailWebAppFactory _factory;

    public ErrorMessageTests(FailingEmailWebAppFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> LoggedInClientAsync()
    {
        var client = _factory.CreateClient(new() { AllowAutoRedirect = false });
        await client.LoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword);
        return client;
    }

    /// <summary>A weekday 2+ weeks ahead, 11:00, never Jan 1 / May 1 (the fake holidays).</summary>
    private static DateTime FutureWorkingSlot()
    {
        var d = DateTime.Today.AddDays(14);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday
               || (d.Month == 1 && d.Day == 1) || (d.Month == 5 && d.Day == 1))
            d = d.AddDays(1);
        return d.AddHours(11);
    }

    [Fact]
    public async Task CreateAppointment_WhenConfirmationEmailFails_SavesAppointment_AndShowsWarning()
    {
        var client = await LoggedInClientAsync();
        var customerId = await TestData.WithDbAsync(_factory, db =>
            db.Customers.Where(c => c.TenantId == CustomWebAppFactory.DefaultTenantId).Select(c => c.Id).FirstAsync());

        var res = await client.PostFormAsync("/Appointments/Create", "/Appointments/Create",
            new Dictionary<string, string>
            {
                ["CustomerId"] = customerId.ToString(),
                ["OrderNumber"] = "ORD-EMAIL-FAIL",
                ["AppointmentDate"] = FutureWorkingSlot().ToString("yyyy-MM-ddTHH:mm"),
                ["Description"] = "Email outage test",
                ["NotifyByEmail"] = "true"
            });

        res.StatusCode.Should().Be(HttpStatusCode.Redirect, "the appointment is saved even though the email failed");
        res.Headers.Location!.ToString().Should().Contain("/Appointments");

        var page = await (await client.GetAsync(res.Headers.Location)).Content.ReadAsStringAsync();
        page.Should().Contain("Appointment created, but the confirmation email could not be sent.");
        page.Should().NotContain(ThrowingEmailService.SecretDetail);

        (await TestData.WithDbAsync(_factory, db => db.Appointments.AnyAsync(a => a.OrderNumber == "ORD-EMAIL-FAIL")))
            .Should().BeTrue();
    }

    [Fact]
    public async Task SendInvoicePdf_WhenEmailFails_ShowsGenericMessage_WithoutExceptionDetails()
    {
        var client = await LoggedInClientAsync();
        var id = CustomWebAppFactory.SeededInvoiceId;

        var res = await client.PostFormAsync($"/Invoices/Details/{id}", $"/Invoices/SendPdfByEmail/{id}",
            new Dictionary<string, string>());

        res.StatusCode.Should().Be(HttpStatusCode.Redirect);

        var page = await (await client.GetAsync(res.Headers.Location)).Content.ReadAsStringAsync();
        page.Should().Contain("The invoice could not be sent by email. Please try again later.");
        page.Should().NotContain(ThrowingEmailService.SecretDetail);
        page.Should().NotContain("InvalidOperationException");
        page.Should().NotContain(" at AppointMe.", "stack traces must never be shown");
    }
}
