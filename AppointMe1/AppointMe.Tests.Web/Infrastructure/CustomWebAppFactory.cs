using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.InMemory.Storage;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AppointMe.Tests.Web;

public class CustomWebAppFactory : WebApplicationFactory<Program>
{
    public const string DefaultUserEmail = "test@appointme.local";
    public const string DefaultUserPassword = "Test123!Aa";

    public static readonly Guid DefaultTenantId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    //  seeded invoice id so tests can call /Invoices/Details/{id}
    public static readonly Guid SeededInvoiceId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly InMemoryDatabaseRoot _dbRoot = new();
    private readonly string _dbName = $"webtests-db-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbDescriptor != null) services.Remove(dbDescriptor);

            services.AddSingleton(_dbRoot);

            services.AddDbContext<ApplicationDbContext>((sp, opt) =>
            {
                var root = sp.GetRequiredService<InMemoryDatabaseRoot>();
                opt.UseInMemoryDatabase(_dbName, root);
            });

            //  Identity login must work in tests
            services.Configure<IdentityOptions>(o =>
            {
                o.SignIn.RequireConfirmedAccount = false;
                o.SignIn.RequireConfirmedEmail = false;
                o.SignIn.RequireConfirmedPhoneNumber = false;
            });

            var spBuilt = services.BuildServiceProvider();
            using var scope = spBuilt.CreateScope();

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userMgr = scope.ServiceProvider.GetRequiredService<UserManager<AppointMeAppUser>>();

            db.Database.EnsureCreated();

            SeedAllAsync(db, userMgr).GetAwaiter().GetResult();
        });
    }

    private static async Task SeedAllAsync(ApplicationDbContext db, UserManager<AppointMeAppUser> userMgr)
    {
        // 1) BUSINESS (TENANT)
        var biz = await db.Businesses.FirstOrDefaultAsync(b => b.Id == DefaultTenantId);
        if (biz == null)
        {
            biz = new Business
            {
                Id = DefaultTenantId,
                Name = "Test Business",
                Address = "Skopje",
                EnableServices = true,
                EnableInvoices = true,
                DefaultSlotMinutes = 30,
                WorkDayStart = new TimeSpan(9, 0, 0),
                WorkDayEnd = new TimeSpan(17, 0, 0),
                OpenMon = true,
                OpenTue = true,
                OpenWed = true,
                OpenThu = true,
                OpenFri = true,
                OpenSat = false,
                OpenSun = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            db.Businesses.Add(biz);
            await db.SaveChangesAsync();
        }

        // 2) TEST USER (IDENTITY)
        var user = await userMgr.FindByEmailAsync(DefaultUserEmail);
        if (user == null)
        {
            user = new AppointMeAppUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = DefaultUserEmail,
                Email = DefaultUserEmail,
                FirstName = "Test",
                LastName = "User",
                TenantId = DefaultTenantId,
                EmailConfirmed = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            var result = await userMgr.CreateAsync(user, DefaultUserPassword);
            if (!result.Succeeded)
                throw new InvalidOperationException("Failed to seed test user: " +
                    string.Join(", ", result.Errors.Select(e => e.Description)));
        }
        else
        {
            var changed = false;
            if (user.TenantId != DefaultTenantId) { user.TenantId = DefaultTenantId; changed = true; }
            if (!user.EmailConfirmed) { user.EmailConfirmed = true; changed = true; }
            if (string.IsNullOrWhiteSpace(user.FirstName)) { user.FirstName = "Test"; changed = true; }
            if (string.IsNullOrWhiteSpace(user.LastName)) { user.LastName = "User"; changed = true; }

            if (changed)
            {
                user.UpdatedAt = DateTime.UtcNow;
                await userMgr.UpdateAsync(user);
            }
        }

        // 3) CUSTOMER
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.TenantId == DefaultTenantId);
        if (customer == null)
        {
            customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = DefaultTenantId,
                FirstName = "Ana",
                LastName = "Doe",
                Email = "ana@test.com",
                PhoneNumber = "070123456",
                City = "Skopje",
                State = "North Macedonia",
                CustomerNumber = 1
            };
            db.Customers.Add(customer);
            await db.SaveChangesAsync();
        }

        // 4) APPOINTMENT
        var appt = await db.Appointments.FirstOrDefaultAsync(a => a.TenantId == DefaultTenantId);
        if (appt == null)
        {
            appt = new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = DefaultTenantId,
                CustomerId = customer.Id,
                OrderNumber = "ORD-0001",
                Description = "Seed appointment for web tests",
                AppointmentDate = DateTime.UtcNow.AddDays(1).Date.AddHours(10),
                Status = AppointmentStatus.Scheduled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();
        }

        // 5) INVOICE (seed known ID)
        var invoice = await db.Invoices.FirstOrDefaultAsync(i => i.Id == SeededInvoiceId);
        if (invoice == null)
        {
            var year = DateTime.UtcNow.Year;

            invoice = new Invoice
            {
                Id = SeededInvoiceId,
                TenantId = DefaultTenantId,
                AppointmentId = appt.Id,
                CustomerId = customer.Id,

                InvoiceNumber = $"INV-{year}-0001",
                IssuedAt = DateTime.UtcNow,
                Status = InvoiceStatus.Draft,

                CustomerNameSnapshot = $"{customer.FirstName} {customer.LastName}",
                CustomerEmailSnapshot = customer.Email,
                BusinessNameSnapshot = biz.Name,
                BusinessAddressSnapshot = biz.Address,
                BusinessLogoSnapshot = biz.LogoUrl,
                AppointmentOrderNumberSnapshot = appt.OrderNumber,
                AppointmentDateSnapshot = appt.AppointmentDate,

                Discount = 0m,
                Tax = 0m,

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            invoice.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                NameSnapshot = "Test Service",
                CategorySnapshot = "General",
                Qty = 1,
                UnitPrice = 100m,
                LineTotal = 100m
            });

            invoice.Subtotal = invoice.Lines.Sum(x => x.LineTotal);
            invoice.Total = invoice.Subtotal - invoice.Discount + invoice.Tax;

            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();
        }
    }
}
