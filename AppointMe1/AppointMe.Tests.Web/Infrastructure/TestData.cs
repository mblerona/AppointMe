using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AppointMe.Tests.Web.Infrastructure;

/// <summary>Seeding helpers shared by web tests. Every method is idempotent.</summary>
public static class TestData
{
    public const string Password = "Test123!Aa";

    public static async Task<Business> EnsureBusinessAsync(ApplicationDbContext db, Guid tenantId, string name)
    {
        var biz = await db.Businesses.FirstOrDefaultAsync(b => b.Id == tenantId);
        if (biz != null) return biz;

        biz = new Business
        {
            Id = tenantId,
            Name = name,
            Address = name,
            EnableInvoices = true,
            EnableServices = true,
            DefaultSlotMinutes = 30,
            WorkDayStart = new TimeSpan(9, 0, 0),
            WorkDayEnd = new TimeSpan(17, 0, 0),
            OpenMon = true,
            OpenTue = true,
            OpenWed = true,
            OpenThu = true,
            OpenFri = true
        };

        db.Businesses.Add(biz);
        await db.SaveChangesAsync();
        return biz;
    }

    public static async Task<AppointMeAppUser> EnsureUserAsync(
        UserManager<AppointMeAppUser> userMgr, string email, Guid? tenantId, string password = Password)
    {
        var user = await userMgr.FindByEmailAsync(email);
        if (user != null) return user;

        user = new AppointMeAppUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            Email = email,
            FirstName = "Test",
            LastName = "User",
            TenantId = tenantId,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var result = await userMgr.CreateAsync(user, password);
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));

        return user;
    }

    /// <summary>Creates a tenant (Business) with one user and returns the user's email.</summary>
    public static async Task<string> EnsureTenantWithUserAsync<TEntryPoint>(
        WebApplicationFactory<TEntryPoint> factory, Guid tenantId, string email) where TEntryPoint : class
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();

        await EnsureBusinessAsync(db, tenantId, $"Biz {email}");
        await EnsureUserAsync(userMgr, email, tenantId);
        return email;
    }

    /// <summary>Runs <paramref name="action"/> against the factory's database in a new scope.</summary>
    public static async Task<T> WithDbAsync<TEntryPoint, T>(
        WebApplicationFactory<TEntryPoint> factory, Func<ApplicationDbContext, Task<T>> action) where TEntryPoint : class
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await action(db);
    }
}
