using AppointMe.Repository.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.InMemory.Storage;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace AppointMe.Tests.Web.Infrastructure;

public class NoAuthWebAppFactory : WebApplicationFactory<Program>
{
    private readonly InMemoryDatabaseRoot _dbRoot = new();
    private readonly string _dbName = $"webtests-noauth-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", TestConnection.Placeholder);

        builder.ConfigureServices(services =>
        {
            var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (dbDescriptor != null) services.Remove(dbDescriptor);

            services.AddSingleton(_dbRoot);

            services.AddDbContext<ApplicationDbContext>((sp, opt) =>
            {
                var root = sp.GetRequiredService<InMemoryDatabaseRoot>();
                opt.UseInMemoryDatabase(_dbName, root);
                // Registration uses an explicit transaction; the in-memory provider has none, so ignore the warning.
                // Every factory builds its own EF service provider (own InMemoryDatabaseRoot); after 20 in one test
                // run EF throws ManyServiceProvidersCreatedWarning, which is only a performance hint here.
                opt.ConfigureWarnings(w => w
                    .Ignore(InMemoryEventId.TransactionIgnoredWarning)
                    .Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
            });

            var spBuilt = services.BuildServiceProvider();
            using var scope = spBuilt.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        });
    }
}
