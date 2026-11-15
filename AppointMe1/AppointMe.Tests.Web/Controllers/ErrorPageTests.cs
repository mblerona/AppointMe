using System.Net;
using AppointMe.Domain.DTO;
using AppointMe.Service.Interface;
using AppointMe.Tests.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace AppointMe.Tests.Web.Controllers;

/// <summary>
/// An unexpected exception (outside Development) shows the friendly error page: status 500, nothing technical,
/// no Request ID and no text about the Development environment. The exception only goes to the log.
/// </summary>
public class ErrorPageTests : IClassFixture<ErrorPageTests.ThrowingDashboardWebAppFactory>
{
    public const string SecretDetail = "Connection to db-server-42 failed for user sa";

    /// <summary>The dashboard throws an exception whose message must never reach the page.</summary>
    public sealed class ThrowingDashboardWebAppFactory : CustomWebAppFactory
    {
        private sealed class ThrowingDashboardService : IDashboardService
        {
            public Task<DashboardDTO> GetDashboardAsync(Guid tenantId, string? range, string? status, string? search) =>
                throw new InvalidOperationException(SecretDetail);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDashboardService>();
                services.AddScoped<IDashboardService, ThrowingDashboardService>();
            });
        }
    }

    private readonly ThrowingDashboardWebAppFactory _factory;

    public ErrorPageTests(ThrowingDashboardWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AnUnexpectedError_ShowsTheFriendlyPage_With500_AndNothingTechnical()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        // Not LoginAsync: it follows the redirect to the dashboard, which throws here on purpose.
        (await client.PostLoginAsync(CustomWebAppFactory.DefaultUserEmail, CustomWebAppFactory.DefaultUserPassword))
            .StatusCode.Should().Be(HttpStatusCode.Redirect, "the login itself succeeds");

        var response = await client.GetAsync("/Dashboard");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        var html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("Something went wrong").And.Contain("Go to the start page");
        html.Should().NotContainAny(SecretDetail, "db-server-42", "InvalidOperationException", "Request ID",
            "Development", "ASPNETCORE_ENVIRONMENT", "Stack");
    }

    [Fact]
    public async Task TheErrorPage_OpenedDirectly_ShowsTheSameFriendlyText()
    {
        var html = await _factory.CreateClient().GetStringAsync("/Home/Error");

        html.Should().Contain("Something went wrong");
        html.Should().NotContainAny("Request ID", "Development");
    }
}
