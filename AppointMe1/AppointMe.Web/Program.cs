using AppointMe.Web.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Registrations are grouped by layer in Infrastructure/ServiceCollectionExtensions.cs.
builder.Services
    .AddAppointMeData(builder.Configuration)
    .AddAppointMeIdentity()
    .AddAppointMeEmail(builder.Configuration)
    .AddAppointMePlatformAdmin(builder.Configuration)
    .AddAppointMeServices()
    .AddAppointMeWeb();

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
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

app.MapRazorPages();

await app.Services.SeedPlatformAdminAsync();

app.Run();
public partial class Program { }
