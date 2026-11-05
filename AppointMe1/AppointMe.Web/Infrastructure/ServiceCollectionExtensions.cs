using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Repository.Interface;
using AppointMe.Service.Admin;
using AppointMe.Service.Email;
using AppointMe.Service.Email.Infrastructure;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>Service registrations, grouped by layer, so Program.cs stays short (audit A11).</summary>
    public static class ServiceCollectionExtensions
    {
        /// <summary>A normal login ends after this long without activity (renewed once half of it has passed).</summary>
        public static readonly TimeSpan SessionIdleTimeout = TimeSpan.FromMinutes(60);

        /// <summary>The same, for a login with "Remember me" ticked.</summary>
        public static readonly TimeSpan RememberMeDuration = TimeSpan.FromDays(14);

        /// <summary>DbContext (SQL Server LocalDB) and repositories.</summary>
        public static IServiceCollection AddAppointMeData(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
            services.AddDatabaseDeveloperPageExceptionFilter();

            services.AddScoped<IBusinessRepository, BusinessRepository>();
            services.AddScoped<ICustomerRepository, CustomerRepository>();
            services.AddScoped<IAppointmentRepository, AppointmentRepository>();
            services.AddScoped<IServiceOfferingRepository, ServiceOfferingRepository>();
            services.AddScoped<IInvoiceRepository, InvoiceRepository>();

            return services;
        }

        /// <summary>ASP.NET Core Identity with the app's password, lockout and confirmation rules.</summary>
        public static IServiceCollection AddAppointMeIdentity(this IServiceCollection services)
        {
            services.AddDefaultIdentity<AppointMeAppUser>(options =>
                {
                    options.SignIn.RequireConfirmedAccount = true;

                    options.Password.RequireDigit = true;
                    options.Password.RequiredLength = 8;
                    options.Password.RequireNonAlphanumeric = false;
                    options.Password.RequireUppercase = true;
                    options.Password.RequireLowercase = true;

                    options.Lockout.AllowedForNewUsers = true;
                    options.Lockout.MaxFailedAccessAttempts = 5;
                    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                })
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>();

            // Must come after AddDefaultIdentity, which sets up the security stamp check on the same Events.
            services.ConfigureApplicationCookie(options =>
            {
                options.ExpireTimeSpan = SessionIdleTimeout;
                options.SlidingExpiration = true;
                options.Events.OnSigningIn = context =>
                {
                    if (context.Properties.IsPersistent)
                        context.Properties.ExpiresUtc = context.Properties.IssuedUtc?.Add(RememberMeDuration);
                    return Task.CompletedTask;
                };
            });

            // Identity emails (confirmation, password reset) go through our MailKit service.
            services.AddTransient<IEmailSender, IdentityEmailSender>();
            services.AddScoped<AccountConfirmationEmail>();

            return services;
        }

        /// <summary>MailKit email (settings from "EmailSettings"; credentials in user secrets).</summary>
        public static IServiceCollection AddAppointMeEmail(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<MailSettings>(configuration.GetSection("EmailSettings"));
            services.AddTransient<ISmtpClientFactory, MailKitSmtpClientFactory>();
            services.AddScoped<IEmailService, EmailService>();

            return services;
        }

        /// <summary>Platform admin account (settings from "PlatformAdmin"; user secrets locally) and the /Admin area's filter.</summary>
        public static IServiceCollection AddAppointMePlatformAdmin(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<PlatformAdminOptions>(configuration.GetSection(PlatformAdminOptions.SectionName));
            services.AddScoped<PlatformAdminSeeder>();
            services.AddScoped<RequirePlatformAdminFilter>();

            return services;
        }

        /// <summary>Business logic (Service layer) and the external holiday API.</summary>
        public static IServiceCollection AddAppointMeServices(this IServiceCollection services)
        {
            services.AddSingleton(TimeProvider.System);
            services.AddMemoryCache();

            services.AddScoped<IBusinessService, BusinessService>();
            services.AddScoped<ICustomerService, CustomerService>();
            services.AddScoped<IAppointmentService, AppointmentService>();
            services.AddScoped<IServiceOfferingService, ServiceOfferingService>();
            services.AddScoped<IInvoiceService, InvoiceService>();
            services.AddScoped<IDashboardService, DashboardService>();

            services.AddHttpClient<IHolidayService, HolidayService>(client =>
                client.Timeout = TimeSpan.FromSeconds(10));

            return services;
        }

        /// <summary>MVC, Razor Pages and the web-only pieces (tenant resolution, logo files).</summary>
        public static IServiceCollection AddAppointMeWeb(this IServiceCollection services)
        {
            // Every unsafe request (POST, PUT, DELETE, ...) to a controller must carry an antiforgery token,
            // even if an action forgets [ValidateAntiForgeryToken]. Razor Pages validate by default.
            services.AddControllersWithViews(options =>
                options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
            services.AddRazorPages();

            // Tenant of the current request, resolved once from the database (see TenantContext).
            services.AddHttpContextAccessor();
            services.AddScoped<ITenantContext, TenantContext>();
            services.AddScoped<RequireTenantFilter>();

            services.AddScoped<ILogoStorage, WebRootLogoStorage>();
            services.AddScoped<CurrentBusiness>();

            return services;
        }
    }
}
