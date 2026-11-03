using AppointMe.Service.Email;
using AppointMe.Service.Interface;
using AppointMe.Tests.Web.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppointMe.Tests.Web.Factories;

/// <summary>
/// Every email send throws (simulates an SMTP outage). Holidays are faked so tests
/// never call the real holiday API.
/// </summary>
public sealed class FailingEmailWebAppFactory : CustomWebAppFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService, ThrowingEmailService>();

            services.RemoveAll<IHolidayService>();
            services.AddSingleton<IHolidayService, FakeHolidayService>();
        });
    }
}
