using AppointMe.Repository.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>
    /// A throwaway database on SQL Server LocalDB (named AppointMe_Test_&lt;guid&gt;), dropped on dispose.
    /// Unlike the InMemory provider it enforces unique indexes and foreign keys, and it runs the real
    /// migrations. Never points at AppointMeDb.
    /// </summary>
    public sealed class SqlServerTestDatabase : IAsyncDisposable
    {
        private const string Server = @"(localdb)\mssqllocaldb";

        public string Name { get; } = $"AppointMe_Test_{Guid.NewGuid():N}";

        public string ConnectionString =>
            $"Server={Server};Database={Name};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

        /// <summary>A new context for the test database (dispose it). Each call is a separate unit of work.</summary>
        public ApplicationDbContext CreateContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(ConnectionString)
                .Options);

        /// <summary>Applies all migrations (creates the database if needed).</summary>
        public async Task MigrateAsync()
        {
            await using var db = CreateContext();
            await db.Database.MigrateAsync();
        }

        /// <summary>
        /// Migrates up or down to <paramref name="migration"/> (a migration id such as
        /// "20251224215944_EditOrderNumber", or its name). Used to seed data in an older schema.
        /// </summary>
        public async Task MigrateToAsync(string migration)
        {
            await using var db = CreateContext();
            await db.GetService<IMigrator>().MigrateAsync(migration);
        }

        public async Task ExecuteSqlAsync(string sql)
        {
            await using var db = CreateContext();
            await db.Database.ExecuteSqlRawAsync(sql);
        }

        /// <summary>Runs a query returning one int column named <c>Value</c>.</summary>
        public async Task<int> ScalarAsync(string sql)
        {
            await using var db = CreateContext();
            return await db.Database.SqlQueryRaw<int>(sql).SingleAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await using var db = CreateContext();
            await db.Database.EnsureDeletedAsync();
        }
    }

    /// <summary>
    /// One migrated test database shared by all tests in the "SqlServer" collection. Tests must create
    /// their own tenant (new Guid) so they don't interfere with each other.
    /// </summary>
    public sealed class SqlServerDatabaseFixture : IAsyncLifetime
    {
        public SqlServerTestDatabase Database { get; } = new();

        public Task InitializeAsync() => Database.MigrateAsync();

        public async Task DisposeAsync() => await Database.DisposeAsync();
    }

    [CollectionDefinition(Name)]
    public sealed class SqlServerCollection : ICollectionFixture<SqlServerDatabaseFixture>
    {
        public const string Name = "SqlServer";
        public const string Category = "SqlServer";
    }
}
