using AppointMe.Domain.Identity;
using AppointMe.Service.Admin;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Creates the PlatformAdmin role and the admin account from configuration at startup. It only ever
    /// adds what is missing: it never changes a password or any other field of an existing account.
    /// </summary>
    public sealed class PlatformAdminSeeder
    {
        private readonly UserManager<AppointMeAppUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly PlatformAdminOptions _options;
        private readonly ILogger<PlatformAdminSeeder> _logger;

        public PlatformAdminSeeder(
            UserManager<AppointMeAppUser> userManager,
            RoleManager<IdentityRole> roleManager,
            IOptions<PlatformAdminOptions> options,
            ILogger<PlatformAdminSeeder> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _options = options.Value;
            _logger = logger;
        }

        public async Task SeedAsync()
        {
            var email = _options.Email?.Trim();
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(_options.Password))
            {
                _logger.LogWarning(
                    "Platform admin not seeded: {Section}:Email and {Section}:Password must both be set.",
                    PlatformAdminOptions.SectionName, PlatformAdminOptions.SectionName);
                return;
            }

            if (!await _roleManager.RoleExistsAsync(PlatformRoles.Admin))
            {
                var roleResult = await _roleManager.CreateAsync(new IdentityRole(PlatformRoles.Admin));
                if (!Succeeded(roleResult, "create the role"))
                    return;
            }

            var user = await _userManager.FindByEmailAsync(email);
            if (user == null)
            {
                user = NewAdmin(email);
                if (!Succeeded(await _userManager.CreateAsync(user, _options.Password), "create the account"))
                    return;
            }
            else if (user.TenantId != null || !user.EmailConfirmed)
            {
                // Someone else's account could hold this email, for example a tenant who registered with
                // it. The role is only ever added to an account a previous seeder run could have created.
                _logger.LogWarning(
                    "Platform admin not seeded: an existing account that belongs to a tenant or has an unconfirmed email uses the configured email.");
                return;
            }

            if (!await _userManager.IsInRoleAsync(user, PlatformRoles.Admin))
                Succeeded(await _userManager.AddToRoleAsync(user, PlatformRoles.Admin), "add the role");
        }

        private static AppointMeAppUser NewAdmin(string email)
        {
            var now = DateTime.UtcNow;
            return new AppointMeAppUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                TenantId = null,
                FirstName = "Platform",
                LastName = "Admin",
                CreatedAt = now,
                UpdatedAt = now
            };
        }

        private bool Succeeded(IdentityResult result, string step)
        {
            if (result.Succeeded)
                return true;

            _logger.LogError(
                "Platform admin not seeded: could not {Step}: {Errors}",
                step, string.Join(" ", result.Errors.Select(e => e.Description)));
            return false;
        }
    }

    public static class PlatformAdminSeederExtensions
    {
        /// <summary>Runs the seeder in its own scope. A failure is logged and never stops the app from starting.</summary>
        public static async Task SeedPlatformAdminAsync(this IServiceProvider services)
        {
            var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger<PlatformAdminSeeder>();
            try
            {
                using var scope = services.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PlatformAdminSeeder>().SeedAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Platform admin seeding failed.");
            }
        }
    }
}
