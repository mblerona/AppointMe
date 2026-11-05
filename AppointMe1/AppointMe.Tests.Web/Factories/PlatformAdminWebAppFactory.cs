using Microsoft.AspNetCore.Hosting;

namespace AppointMe.Tests.Web.Factories;

/// <summary>Sets the PlatformAdmin settings, so the real startup seeder creates the admin account.</summary>
public sealed class PlatformAdminWebAppFactory : CustomWebAppFactory
{
    public const string AdminEmail = "admin@appointme.local";
    public const string AdminPassword = "Admin123!Aa";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("PlatformAdmin:Email", AdminEmail);
        builder.UseSetting("PlatformAdmin:Password", AdminPassword);
    }
}
