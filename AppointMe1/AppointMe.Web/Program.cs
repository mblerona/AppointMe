using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Interface;
using AppointMe.Service.Implementation;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using AppointMe.Service.Email;
using AppointMe.Service.Email.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddScoped<IServiceOfferingRepository, ServiceOfferingRepository>();
builder.Services.AddScoped<IInvoiceRepository, InvoiceRepository>();

builder.Services.AddTransient<ISmtpClientFactory, MailKitSmtpClientFactory>();


builder.Services.AddDefaultIdentity<AppointMeAppUser>(options =>
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

builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IAppointmentRepository, AppointmentRepository>();

builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IAppointmentService, AppointmentService>();
builder.Services.AddScoped<IInvoiceService, InvoiceService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddMemoryCache();

// Tenant of the current request, resolved once from the database (see TenantContext).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, AppointMe.Web.Infrastructure.TenantContext>();
builder.Services.AddScoped<AppointMe.Web.Infrastructure.RequireTenantFilter>();

// Registration (Business + owner) and Identity emails (confirmation, password reset) via MailKit.
builder.Services.AddScoped<IBusinessRepository, BusinessRepository>();
builder.Services.AddScoped<IBusinessService, BusinessService>();
builder.Services.AddScoped<IServiceOfferingService, ServiceOfferingService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<ILogoStorage, AppointMe.Web.Infrastructure.WebRootLogoStorage>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddTransient<Microsoft.AspNetCore.Identity.UI.Services.IEmailSender, AppointMe.Web.Infrastructure.IdentityEmailSender>();
builder.Services.AddScoped<AppointMe.Web.Infrastructure.AccountConfirmationEmail>();

builder.Services.AddHttpClient<IHolidayService, HolidayService>(client =>
    client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
builder.Services.Configure<AppointMe.Service.Email.MailSettings>(
    builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<AppointMe.Service.Email.IEmailService, AppointMe.Service.Email.EmailService>();


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();
public partial class Program { }