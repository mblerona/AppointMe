using FluentAssertions;
using Xunit;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>
    /// Runs migrations on their own throwaway databases: seed rows in an older schema with raw SQL,
    /// migrate up, check the data, then migrate down. Each test owns its database, so keep them few:
    /// creating a LocalDB database and running all migrations takes about 10 s. The empty-database case
    /// is covered by SqlServerDatabaseFixture, which migrates an empty database to the latest version.
    /// One test class per migration, so the classes run in parallel (tests inside one class run one after another).
    /// </summary>
    internal static class MigrationSql
    {
        public const string EditOrderNumber = "20251224215944_EditOrderNumber";
        public const string ConfirmExistingUsers = "20261103141300_ConfirmExistingUsers";
        public const string TenantForeignKeys = "20261103152752_TenantForeignKeysAndPerTenantOrderNumber";

        public static string User(string id, string? tenantSql, bool confirmed) => $@"
INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
    PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FirstName, LastName, CreatedAt, UpdatedAt, TenantId)
VALUES ('{id}', '{id}@x', UPPER('{id}@X'), '{id}@x', UPPER('{id}@X'), {(confirmed ? 1 : 0)},
    0, 0, 1, 0, 'F', 'L', GETUTCDATE(), GETUTCDATE(), {tenantSql ?? "NULL"});";

        public static string Business(string id, string name) => $@"
INSERT INTO Businesses (Id, Name, DefaultSlotMinutes, WorkDayStart, WorkDayEnd,
    OpenMon, OpenTue, OpenWed, OpenThu, OpenFri, OpenSat, OpenSun)
VALUES ('{id}', N'{name}', 30, '09:00', '17:00', 1, 1, 1, 1, 1, 0, 0);";

        public static string Customer(string id, string tenant, int number, string? shadowBusinessId = null) => $@"
INSERT INTO Customers (Id, TenantId, CustomerNumber, FirstName, LastName, Email, PhoneNumber, State, City, BusinessId)
VALUES ('{id}', '{tenant}', {number}, 'Ana', 'Doe', '{id}@x.com', '070123456', 'MK', 'Skopje',
    {(shadowBusinessId == null ? "NULL" : $"'{shadowBusinessId}'")});";

        public static string Appointment(string id, string tenant, string customer, string order) => $@"
INSERT INTO Appointments (Id, TenantId, CustomerId, OrderNumber, AppointmentDate, Description)
VALUES ('{id}', '{tenant}', '{customer}', '{order}', '2026-12-01T10:00:00', 'Test');";
    }

    [Trait("Category", SqlServerCollection.Category)]
    public class ConfirmExistingUsersMigrationTests
    {
        [Fact]
        public async Task ConfirmsUsers_AndCreatesMissingBusinesses()
        {
            await using var database = new SqlServerTestDatabase();
            await database.MigrateToAsync(MigrationSql.EditOrderNumber);

            const string existingBusiness = "11111111-0000-0000-0000-000000000001";
            const string orphanTenant = "22222222-0000-0000-0000-000000000002";
            await database.ExecuteSqlAsync($@"
INSERT INTO Businesses (Id, Name, DefaultSlotMinutes, WorkDayStart, WorkDayEnd,
    OpenMon, OpenTue, OpenWed, OpenThu, OpenFri, OpenSat, OpenSun)
VALUES ('{existingBusiness}', N'Existing', 30, '09:00', '17:00', 1, 1, 1, 1, 1, 0, 0);
{MigrationSql.User("ok", $"'{existingBusiness}'", confirmed: true)}
{MigrationSql.User("no-tenant", null, confirmed: false)}
{MigrationSql.User("orphan-1", $"'{orphanTenant}'", confirmed: false)}
{MigrationSql.User("orphan-2", $"'{orphanTenant}'", confirmed: false)}");

            await database.MigrateToAsync(MigrationSql.ConfirmExistingUsers);

            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM AspNetUsers WHERE EmailConfirmed = 0")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM AspNetUsers WHERE TenantId IS NULL")).Should().Be(0);
            (await database.ScalarAsync(@"SELECT COUNT(*) AS Value FROM AspNetUsers u
                WHERE NOT EXISTS (SELECT 1 FROM Businesses b WHERE b.Id = u.TenantId)")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Businesses")).Should()
                .Be(3, "the existing one, one for the user without a tenant, one shared by both orphans");
            (await database.ScalarAsync($"SELECT COUNT(*) AS Value FROM Businesses WHERE Id = '{existingBusiness}' AND Name = N'Existing'"))
                .Should().Be(1, "an existing Business is left alone");

            // Down is intentionally empty (data-only migration), so rolling back must work and keep the data.
            await database.MigrateToAsync(MigrationSql.EditOrderNumber);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Businesses")).Should().Be(3);
        }

    }

    [Trait("Category", SqlServerCollection.Category)]
    public class TenantForeignKeysMigrationTests
    {
        [Fact]
        public async Task CreatesPlaceholdersForOrphans_AndRollsBack()
        {
            await using var database = new SqlServerTestDatabase();
            await database.MigrateToAsync(MigrationSql.ConfirmExistingUsers);

            const string real = "aaaaaaaa-0000-0000-0000-000000000001";
            const string orphanCustomers = "bbbbbbbb-0000-0000-0000-000000000002";  // customers + appointments
            const string orphanInvoices = "cccccccc-0000-0000-0000-000000000003";   // only an invoice
            const string orphanUser = "dddddddd-0000-0000-0000-000000000004";       // only a user
            const string realCustomer = "10000000-0000-0000-0000-000000000001";
            const string orphanCustomer = "20000000-0000-0000-0000-000000000002";
            await database.ExecuteSqlAsync($@"
{MigrationSql.Business(real, "Real business")}
{MigrationSql.Customer(realCustomer, real, 1, shadowBusinessId: real)}
{MigrationSql.Customer(orphanCustomer, orphanCustomers, 1)}
{MigrationSql.Appointment("30000000-0000-0000-0000-000000000001", real, realCustomer, "ORD-1")}
{MigrationSql.Appointment("30000000-0000-0000-0000-000000000002", orphanCustomers, orphanCustomer, "ORD-2")}
INSERT INTO Invoices (Id, TenantId, AppointmentId, CustomerId, InvoiceNumber, IssuedAt, CustomerNameSnapshot,
    BusinessNameSnapshot, AppointmentDateSnapshot, Subtotal, Discount, Tax, Total, CreatedAt, UpdatedAt)
VALUES (NEWID(), '{orphanInvoices}', '30000000-0000-0000-0000-000000000001', '{realCustomer}', 'INV-2026-0001',
    GETUTCDATE(), 'Ana Doe', 'X', GETUTCDATE(), 0, 0, 0, 0, GETUTCDATE(), GETUTCDATE());
{MigrationSql.User("orphan-user", $"'{orphanUser}'", confirmed: true)}");

            await database.MigrateToAsync(MigrationSql.TenantForeignKeys);

            // One placeholder per orphaned tenant id; the real Business is untouched.
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Businesses WHERE Name = N'Recovered business'")).Should().Be(3);
            foreach (var id in new[] { orphanCustomers, orphanInvoices, orphanUser })
                (await database.ScalarAsync($"SELECT COUNT(*) AS Value FROM Businesses WHERE Id = '{id}'")).Should().Be(1);
            (await database.ScalarAsync($"SELECT COUNT(*) AS Value FROM Businesses WHERE Id = '{real}' AND Name = N'Real business'")).Should().Be(1);

            // No rows were lost, the shadow column is gone, the six foreign keys exist without cascade.
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Customers")).Should().Be(2);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Appointments")).Should().Be(2);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Invoices")).Should().Be(1);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Customers' AND COLUMN_NAME = 'BusinessId'")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID('Businesses') AND delete_referential_action = 0")).Should().Be(6);

            // Two tenants may now share an order number, which blocks the rollback with a clear error...
            await database.ExecuteSqlAsync(MigrationSql.Appointment("30000000-0000-0000-0000-000000000003", orphanCustomers, orphanCustomer, "ORD-1"));
            var blockedDown = () => database.MigrateToAsync(MigrationSql.ConfirmExistingUsers);
            (await blockedDown.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>())
                .WithMessage("*order numbers are used by more than one tenant*");
            (await database.ScalarAsync($"SELECT COUNT(*) AS Value FROM __EFMigrationsHistory WHERE MigrationId = '{MigrationSql.TenantForeignKeys}'"))
                .Should().Be(1, "the failed rollback changed nothing");

            // ...and once the duplicate is gone, Down restores the old schema.
            await database.ExecuteSqlAsync("DELETE FROM Appointments WHERE Id = '30000000-0000-0000-0000-000000000003'");
            await database.MigrateToAsync(MigrationSql.ConfirmExistingUsers);

            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Customers' AND COLUMN_NAME = 'BusinessId'")).Should().Be(1);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM sys.indexes WHERE name = 'UX_Appointment_OrderNumber'")).Should().Be(1);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM sys.foreign_keys WHERE referenced_object_id = OBJECT_ID('Businesses') AND delete_referential_action = 1")).Should()
                .Be(2, "ServiceOfferings and ServiceCategories cascade again");
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Businesses WHERE Name = N'Recovered business'")).Should()
                .Be(3, "placeholders are data, so Down keeps them");
        }
    }
}
