using AppointMe.Service.Email;
using AppointMe.Service.Interface;
using AppointMe.Tests.Web.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppointMe.Tests.Web.Factories;

/// <summary>
/// Emails are recorded in <see cref="Emails"/> instead of being sent (registration and confirmation
/// tests). Holidays are faked so tests never call the real holiday API.
/// </summary>
public sealed class CapturingEmailWebAppFactory : CustomWebAppFactory
{
    public CapturingEmailService Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Emails);

            services.RemoveAll<IHolidayService>();
            services.AddSingleton<IHolidayService, FakeHolidayService>();
        });
    }
}
