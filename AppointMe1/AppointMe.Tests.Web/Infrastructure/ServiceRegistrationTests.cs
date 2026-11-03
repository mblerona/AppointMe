using AppointMe.Service.Email;
using AppointMe.Service.Interface;
using AppointMe.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace AppointMe.Tests.Web.Infrastructure;

/// <summary>The registration extension methods (audit A11).</summary>
public class ServiceRegistrationTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public ServiceRegistrationTests(CustomWebAppFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public void AddAppointMeEmail_RegistersIEmailServiceOnce()
    {
        var services = new ServiceCollection();

        services.AddAppointMeEmail(new ConfigurationBuilder().Build());

        services.Where(d => d.ServiceType == typeof(IEmailService)).Should().ContainSingle()
            .Which.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AppServices_RegisterEachServiceInterfaceOnce()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = TestConnection.Placeholder })
            .Build();

        services.AddAppointMeData(config).AddAppointMeEmail(config).AddAppointMeServices();

        var duplicates = services
            .Where(d => d.ServiceType.Namespace?.StartsWith("AppointMe") == true)
            .GroupBy(d => d.ServiceType)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.Name);
        duplicates.Should().BeEmpty();
    }

    [Fact]
    public void Mvc_HasTheGlobalAntiforgeryFilter()
    {
        var mvc = _factory.Services.GetRequiredService<IOptions<MvcOptions>>().Value;

        mvc.Filters.OfType<AutoValidateAntiforgeryTokenAttribute>().Should().ContainSingle();
    }

    [Fact]
    public void App_ResolvesTheNewServices()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;

        sp.GetRequiredService<IBusinessService>().Should().NotBeNull();
        sp.GetRequiredService<IDashboardService>().Should().NotBeNull();
        sp.GetRequiredService<IServiceOfferingService>().Should().NotBeNull();
        sp.GetRequiredService<IAppointmentService>().Should().NotBeNull();
        sp.GetRequiredService<IInvoiceService>().Should().NotBeNull();
        sp.GetRequiredService<CurrentBusiness>().Should().NotBeNull();
    }
}
