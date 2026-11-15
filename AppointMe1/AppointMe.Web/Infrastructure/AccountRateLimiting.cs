using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>Settings under "RateLimiting" (appsettings or user secrets).</summary>
    public sealed class AccountRateLimitingOptions
    {
        public const string SectionName = "RateLimiting";

        /// <summary>Off by default only in the Testing environment, so the web tests aren't throttled.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Attempts (form posts) per IP address and window, shared by all the account forms.</summary>
        public int PermitLimit { get; set; } = 10;

        public int WindowSeconds { get; set; } = 60;
    }

    /// <summary>
    /// Limits the account forms that guess passwords or send emails (login, register, forgot password,
    /// resend confirmation) per IP address. Only the posts count: viewing a form is never limited.
    /// A refused post gets 429, which the status code page turns into a friendly "wait a moment" page.
    /// The IP is the connection's address; behind a reverse proxy, forwarded headers must be set up first,
    /// or every visitor would share one limit.
    /// </summary>
    public static class AccountRateLimiting
    {
        public const string PolicyName = "account-forms";

        public static IServiceCollection AddAppointMeRateLimiting(
            this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
        {
            var section = configuration.GetSection(AccountRateLimitingOptions.SectionName);
            services.AddOptions<AccountRateLimitingOptions>()
                .Bind(section)
                .PostConfigure(o =>
                {
                    // Testing: off unless a test switches it on explicitly.
                    if (environment.IsEnvironment("Testing") && section[nameof(AccountRateLimitingOptions.Enabled)] == null)
                        o.Enabled = false;
                })
                .Validate(o => o.PermitLimit > 0 && o.WindowSeconds > 0, "RateLimiting needs a positive PermitLimit and WindowSeconds.");

            services.AddRateLimiter(limiter =>
            {
                limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                limiter.AddPolicy(PolicyName, context =>
                {
                    var options = context.RequestServices.GetRequiredService<IOptions<AccountRateLimitingOptions>>().Value;

                    if (!options.Enabled || !HttpMethods.IsPost(context.Request.Method))
                        return RateLimitPartition.GetNoLimiter("not-limited");

                    var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = options.PermitLimit,
                        Window = TimeSpan.FromSeconds(options.WindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
                });

                limiter.OnRejected = (rejected, _) =>
                {
                    if (rejected.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                        rejected.HttpContext.Response.Headers.RetryAfter =
                            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

                    rejected.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger(typeof(AccountRateLimiting))
                        .LogWarning("Rate limit reached for {Path}.", rejected.HttpContext.Request.Path);

                    // No body: the status code page renders the friendly message.
                    return ValueTask.CompletedTask;
                };
            });

            return services;
        }
    }
}
