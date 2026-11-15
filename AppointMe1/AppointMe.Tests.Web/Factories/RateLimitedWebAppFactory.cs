using AppointMe.Tests.Web.Infrastructure;
using Microsoft.AspNetCore.Hosting;

namespace AppointMe.Tests.Web.Factories;

/// <summary>
/// The account forms' rate limit switched on (it is off in the Testing environment), with a small limit.
/// Its tests share one limiter, and TestServer gives every request the same address.
/// </summary>
public sealed class RateLimitedWebAppFactory : CustomWebAppFactory
{
    public const int PermitLimit = 3;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.UseSetting("RateLimiting:Enabled", "true");
        builder.UseSetting("RateLimiting:PermitLimit", PermitLimit.ToString());
        builder.UseSetting("RateLimiting:WindowSeconds", "600");
    }
}
