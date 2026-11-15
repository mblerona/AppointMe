using AppointMe.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Registrations are grouped by layer in Infrastructure/ServiceCollectionExtensions.cs.
builder.Services
    .AddAppointMeData(builder.Configuration)
    .AddAppointMeIdentity()
    .AddAppointMeEmail(builder.Configuration)
    .AddAppointMePlatformAdmin(builder.Configuration)
    .AddAppointMeServices()
    .AddAppointMeWeb()
    .AddAppointMeRateLimiting(builder.Configuration, builder.Environment);

var app = builder.Build();

// First, so every response gets the headers, static files and error pages included.
app.UseMiddleware<SecurityHeadersMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// A 404, 403 or 429 without a body gets the friendly page (HomeController.Status), keeping its status code.
app.UseStatusCodePagesWithReExecute(TenantStatusMiddleware.HomeStatusPath + "/{0}");

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// After routing, so it sees [EnableRateLimiting] on the account pages.
app.UseRateLimiter();

app.UseAuthentication();
app.UseMiddleware<TenantStatusMiddleware>();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapRazorPages();

await app.Services.SeedPlatformAdminAsync();

app.Run();
public partial class Program { }
