using AppointMe.Service.Email;
using AppointMe.Service.Interface;
using AppointMe.Tests.Web.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AppointMe.Tests.Web.Factories;

/// <summary>
/// Sets the PlatformAdmin and Support settings, so the real startup seeder creates the admin account.
/// Emails are recorded in <see cref="Emails"/>, and holidays are faked.
/// </summary>
public sealed class PlatformAdminWebAppFactory : CustomWebAppFactory
{
    public const string AdminEmail = "admin@appointme.local";
    public const string AdminPassword = "Admin123!Aa";
    public const string SupportEmail = "support@appointme.local";

    public CapturingEmailService Emails { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("PlatformAdmin:Email", AdminEmail);
        builder.UseSetting("PlatformAdmin:Password", AdminPassword);
        builder.UseSetting("Support:Email", SupportEmail);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailService>();
            services.AddSingleton<IEmailService>(Emails);

            services.RemoveAll<IHolidayService>();
            services.AddSingleton<IHolidayService, FakeHolidayService>();
        });
    }
}
