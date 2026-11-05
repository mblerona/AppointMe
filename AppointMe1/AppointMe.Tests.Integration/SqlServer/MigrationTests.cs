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
        public const string AddAppointmentDuration = "20261104114652_AddAppointmentDuration";
        public const string AddServiceDuration = "20261104122910_AddServiceDuration";
        public const string AddBusinessTimeZone = "20261104134128_AddBusinessTimeZone";
        public const string AddInvoiceYearAndSequence = "20261104140948_AddInvoiceYearAndSequence";
        public const string AddBusinessSuspensionAndActionLog = "20261105153755_AddBusinessSuspensionAndActionLog";
        public const string AddBusinessDeletionRequest = "20261105162458_AddBusinessDeletionRequest";
        public const string AddUserLastLogin = "20261105165944_AddUserLastLogin";

        public static string Invoice(string tenant, string number, string issuedAt) => $@"
INSERT INTO Invoices (Id, TenantId, AppointmentId, CustomerId, InvoiceNumber, IssuedAt, CustomerNameSnapshot,
    BusinessNameSnapshot, AppointmentDateSnapshot, Subtotal, Discount, Tax, Total, CreatedAt, UpdatedAt)
VALUES (NEWID(), '{tenant}', NEWID(), NEWID(), '{number}', '{issuedAt}', 'Ana Doe', 'X', '{issuedAt}', 0, 0, 0, 0, '{issuedAt}', '{issuedAt}');";

        public static string User(string id, string? tenantSql, bool confirmed) => $@"
INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
    PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FirstName, LastName, CreatedAt, UpdatedAt, TenantId)
VALUES ('{id}', '{id}@x', UPPER('{id}@X'), '{id}@x', UPPER('{id}@X'), {(confirmed ? 1 : 0)},
    0, 0, 1, 0, 'F', 'L', GETUTCDATE(), GETUTCDATE(), {tenantSql ?? "NULL"});";

        public static string Business(string id, string name, int slotMinutes = 30) => $@"
INSERT INTO Businesses (Id, Name, DefaultSlotMinutes, WorkDayStart, WorkDayEnd,
    OpenMon, OpenTue, OpenWed, OpenThu, OpenFri, OpenSat, OpenSun)
VALUES ('{id}', N'{name}', {slotMinutes}, '09:00', '17:00', 1, 1, 1, 1, 1, 0, 0);";

        /// <summary>For migrations after TenantForeignKeys, which removed the shadow BusinessId column.</summary>
        public static string CustomerWithoutShadowColumn(string id, string tenant, int number) => $@"
INSERT INTO Customers (Id, TenantId, CustomerNumber, FirstName, LastName, Email, PhoneNumber, State, City)
VALUES ('{id}', '{tenant}', {number}, 'Ana', 'Doe', '{id}@x.com', '070123456', 'MK', 'Skopje');";

        public static string Customer(string id, string tenant, int number, string? shadowBusinessId = null) => $@"
INSERT INTO Customers (Id, TenantId, CustomerNumber, FirstName, LastName, Email, PhoneNumber, State, City, BusinessId)
VALUES ('{id}', '{tenant}', {number}, 'Ana', 'Doe', '{id}@x.com', '070123456', 'MK', 'Skopje',
    {(shadowBusinessId == null ? "NULL" : $"'{shadowBusinessId}'")});";

        public static string Appointment(string id, string tenant, string customer, string order) => $@"
INSERT INTO Appointments (Id, TenantId, CustomerId, OrderNumber, AppointmentDate, Description)
VALUES ('{id}', '{tenant}', '{customer}', '{order}', '2026-12-01T10:00:00', 'Test');";
    }

    [Trait("Category", SqlServerCollection.Category)]
    public class AddUserLastLoginMigrationTests
    {
        [Fact]
        public async Task ExistingUsersHaveNoLastLogin_AndRollsBack_KeepingTheUsers()
        {
            await using var database = new SqlServerTestDatabase();
            await database.MigrateToAsync(MigrationSql.AddBusinessDeletionRequest);

            await database.ExecuteSqlAsync(MigrationSql.User("existing", null, confirmed: true));

            await database.MigrateToAsync(MigrationSql.AddUserLastLogin);

            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM AspNetUsers WHERE Id = 'existing' AND LastLoginAt IS NULL"))
                .Should().Be(1, "there is no history to fill in");

            await database.ExecuteSqlAsync("UPDATE AspNetUsers SET LastLoginAt = GETUTCDATE() WHERE Id = 'existing'");

            await database.MigrateToAsync(MigrationSql.AddBusinessDeletionRequest);

            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = 'LastLoginAt'")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM AspNetUsers")).Should().Be(1, "rolling back keeps the users");
        }
    }

    [Trait("Category", SqlServerCollection.Category)]
    public class AddBusinessDeletionRequestMigrationTests
    {
        [Fact]
        public async Task ExistingBusinessesHaveNoRequest_AndRollsBack()
        {
            await using var database = new SqlServerTestDatabase();
            await database.MigrateToAsync(MigrationSql.AddBusinessSuspensionAndActionLog);

            const string existing = "aaaaaaaa-0000-0000-0000-000000000001";
            await database.ExecuteSqlAsync(MigrationSql.Business(existing, "Existing"));

            await database.MigrateToAsync(MigrationSql.AddBusinessDeletionRequest);

            (await database.ScalarAsync($@"SELECT COUNT(*) AS Value FROM Businesses WHERE Id = '{existing}' AND Name = N'Existing'
                AND DeletionRequestedAt IS NULL AND DeletionRequestReason IS NULL")).Should().Be(1);

            await database.ExecuteSqlAsync($"UPDATE Businesses SET DeletionRequestedAt = GETUTCDATE(), DeletionRequestReason = N'Closing' WHERE Id = '{existing}'");

            await database.MigrateToAsync(MigrationSql.AddBusinessSuspensionAndActionLog);

            (await database.ScalarAsync(@"SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Businesses'
                AND COLUMN_NAME IN ('DeletionRequestedAt', 'DeletionRequestReason')")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Businesses")).Should().Be(1, "rolling back keeps the businesses");
        }
    }

    [Trait("Category", SqlServerCollection.Category)]
    public class AddBusinessSuspensionAndActionLogMigrationTests
    {
        [Fact]
        public async Task ExistingBusinessesBecomeActive_LogAcceptsAnyBusinessId_AndRollsBack()
        {
            await using var database = new SqlServerTestDatabase();
            await database.MigrateToAsync(MigrationSql.AddInvoiceYearAndSequence);

            const string existing = "aaaaaaaa-0000-0000-0000-000000000001";
            const string added = "aaaaaaaa-0000-0000-0000-000000000002";
            await database.ExecuteSqlAsync(MigrationSql.Business(existing, "Existing"));

            await database.MigrateToAsync(MigrationSql.AddBusinessSuspensionAndActionLog);

            (await database.ScalarAsync($@"SELECT COUNT(*) AS Value FROM Businesses WHERE Id = '{existing}' AND Name = N'Existing'
                AND Status = 0 AND SuspendedAt IS NULL AND SuspensionReason IS NULL AND SuspensionNote IS NULL"))
                .Should().Be(1, "existing businesses stay usable");

            await database.ExecuteSqlAsync(MigrationSql.Business(added, "Added by a script"));
            (await database.ScalarAsync($"SELECT COUNT(*) AS Value FROM Businesses WHERE Id = '{added}' AND Status = 0"))
                .Should().Be(1, "an insert that doesn't mention the column gets the default");

            await database.ExecuteSqlAsync($@"
UPDATE Businesses SET Status = 1, SuspendedAt = GETUTCDATE(), SuspensionReason = 4, SuspensionNote = N'A note' WHERE Id = '{existing}';
INSERT INTO AdminActionLogs (At, Action, BusinessId, BusinessName, AdminEmail, Reason, Note)
VALUES (GETUTCDATE(), 1, '{existing}', N'Existing', 'admin@x', N'Other', N'A note');
INSERT INTO AdminActionLogs (At, Action, BusinessId, BusinessName)
VALUES (GETUTCDATE(), 0, 'dddddddd-0000-0000-0000-000000000009', N'Deleted long ago');");
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM AdminActionLogs"))
                .Should().Be(2, "the log has no foreign key, so it can outlive a deleted tenant");

            await database.MigrateToAsync(MigrationSql.AddInvoiceYearAndSequence);

            (await database.ScalarAsync(@"SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Businesses'
                AND COLUMN_NAME IN ('Status', 'SuspendedAt', 'SuspensionReason', 'SuspensionNote')")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'AdminActionLogs'")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Businesses")).Should().Be(2, "rolling back keeps the businesses");
        }
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
    public class AddInvoiceYearAndSequenceMigrationTests
    {
        [Fact]
        public async Task BackfillsFromTheNumbers_CopesWithMessyOnes_AndRollsBack()
        {
            await using var database = new SqlServerTestDatabase();
            await database.MigrateToAsync(MigrationSql.AddBusinessTimeZone);

            const string tenantA = "aaaaaaaa-0000-0000-0000-000000000001";
            const string tenantB = "aaaaaaaa-0000-0000-0000-000000000002";
            await database.ExecuteSqlAsync($@"
{MigrationSql.Business(tenantA, "A")}
{MigrationSql.Business(tenantB, "B")}
{MigrationSql.Invoice(tenantA, "INV-2026-0001", "2026-01-01T10:00:00")}
{MigrationSql.Invoice(tenantA, "INV-2026-9999", "2026-01-02T10:00:00")}
{MigrationSql.Invoice(tenantA, "INV-2026-10000", "2026-01-03T10:00:00")}
{MigrationSql.Invoice(tenantA, "INV-2025-0003", "2025-05-01T10:00:00")}
{MigrationSql.Invoice(tenantA, "INV-2026-1", "2026-01-04T10:00:00")}
{MigrationSql.Invoice(tenantA, "LEGACY-7", "2026-01-05T10:00:00")}
{MigrationSql.Invoice(tenantA, "INV-2026-ABCD", "2026-01-06T10:00:00")}
{MigrationSql.Invoice(tenantA, "OLD", "2024-03-01T10:00:00")}
{MigrationSql.Invoice(tenantB, "INV-2026-0001", "2026-01-01T10:00:00")}");

            await database.MigrateToAsync(MigrationSql.AddInvoiceYearAndSequence);

            Task<int> Count(string tenant, string number, int year, int sequence) => database.ScalarAsync(
                $"SELECT COUNT(*) AS Value FROM Invoices WHERE TenantId = '{tenant}' AND InvoiceNumber = '{number}' AND InvoiceYear = {year} AND InvoiceSequence = {sequence}");

            // The usual form keeps its values, compared as numbers.
            (await Count(tenantA, "INV-2026-0001", 2026, 1)).Should().Be(1);
            (await Count(tenantA, "INV-2026-9999", 2026, 9999)).Should().Be(1);
            (await Count(tenantA, "INV-2026-10000", 2026, 10000)).Should().Be(1);
            (await Count(tenantA, "INV-2025-0003", 2025, 3)).Should().Be(1);
            (await Count(tenantB, "INV-2026-0001", 2026, 1)).Should().Be(1, "each tenant has its own sequence");

            // The rest continue after the highest of their year, oldest first. Their numbers don't change.
            (await Count(tenantA, "INV-2026-1", 2026, 10001)).Should().Be(1, "same value as INV-2026-0001, which came first");
            (await Count(tenantA, "LEGACY-7", 2026, 10002)).Should().Be(1);
            (await Count(tenantA, "INV-2026-ABCD", 2026, 10003)).Should().Be(1);
            (await Count(tenantA, "OLD", 2024, 1)).Should().Be(1, "the first of its year");

            await database.MigrateToAsync(MigrationSql.AddBusinessTimeZone);

            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME IN ('InvoiceYear', 'InvoiceSequence')")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM sys.indexes WHERE name = 'UX_Invoice_TenantId_Year_Sequence'")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Invoices")).Should().Be(9, "rolling back keeps the invoices");
        }
    }

    [Trait("Category", SqlServerCollection.Category)]
    public class AddBusinessTimeZoneMigrationTests
    {
        [Fact]
        public async Task ExistingBusinessesGetSkopje_NewRowsToo_AndRollsBack()
        {
            await using var database = new SqlServerTestDatabase();
            await database.MigrateToAsync(MigrationSql.AddServiceDuration);

            const string existing = "aaaaaaaa-0000-0000-0000-000000000001";
            const string added = "aaaaaaaa-0000-0000-0000-000000000002";
            await database.ExecuteSqlAsync(MigrationSql.Business(existing, "Existing"));

            await database.MigrateToAsync(MigrationSql.AddBusinessTimeZone);

            (await database.ScalarAsync($"SELECT COUNT(*) AS Value FROM Businesses WHERE Id = '{existing}' AND TimeZoneId = 'Europe/Skopje' AND Name = N'Existing'"))
                .Should().Be(1, "the time the app used so far was the server's, which is in Skopje");

            await database.ExecuteSqlAsync(MigrationSql.Business(added, "Added by a script"));
            (await database.ScalarAsync($"SELECT COUNT(*) AS Value FROM Businesses WHERE Id = '{added}' AND TimeZoneId = 'Europe/Skopje'"))
                .Should().Be(1, "an insert that doesn't mention the column gets the default");

            await database.MigrateToAsync(MigrationSql.AddServiceDuration);

            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Businesses' AND COLUMN_NAME = 'TimeZoneId'")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Businesses")).Should().Be(2, "rolling back keeps the businesses");
        }
    }

    [Trait("Category", SqlServerCollection.Category)]
    public class AddServiceDurationMigrationTests
    {
        [Fact]
        public async Task ExistingServicesGetNoDuration_AndRollsBack()
        {
            await using var database = new SqlServerTestDatabase();
            await database.MigrateToAsync(MigrationSql.AddAppointmentDuration);

            const string business = "aaaaaaaa-0000-0000-0000-000000000001";
            const string service = "40000000-0000-0000-0000-000000000001";
            await database.ExecuteSqlAsync($@"
{MigrationSql.Business(business, "Services")}
INSERT INTO ServiceOfferings (Id, BusinessId, Name, Price) VALUES ('{service}', '{business}', N'Haircut', 500);");

            await database.MigrateToAsync(MigrationSql.AddServiceDuration);

            (await database.ScalarAsync($"SELECT COUNT(*) AS Value FROM ServiceOfferings WHERE Id = '{service}' AND DurationMinutes IS NULL AND Price = 500 AND Name = N'Haircut'"))
                .Should().Be(1, "the service is unchanged and has no duration until the business sets one");

            await database.ExecuteSqlAsync($"UPDATE ServiceOfferings SET DurationMinutes = 45 WHERE Id = '{service}'");

            await database.MigrateToAsync(MigrationSql.AddAppointmentDuration);

            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ServiceOfferings' AND COLUMN_NAME = 'DurationMinutes'")).Should().Be(0);
            (await database.ScalarAsync($"SELECT COUNT(*) AS Value FROM ServiceOfferings WHERE Id = '{service}'")).Should().Be(1, "rolling back keeps the service");
        }
    }

    [Trait("Category", SqlServerCollection.Category)]
    public class AddAppointmentDurationMigrationTests
    {
        [Fact]
        public async Task BackfillsFromEachBusinessesSlotLength_AndRollsBack()
        {
            await using var database = new SqlServerTestDatabase();
            await database.MigrateToAsync(MigrationSql.TenantForeignKeys);

            const string slot45 = "aaaaaaaa-0000-0000-0000-000000000045";
            const string slot0 = "aaaaaaaa-0000-0000-0000-000000000000";
            const string customer45 = "10000000-0000-0000-0000-000000000045";
            const string customer0 = "10000000-0000-0000-0000-000000000000";
            const string appointment45 = "30000000-0000-0000-0000-000000000045";
            const string appointment0 = "30000000-0000-0000-0000-000000000000";
            await database.ExecuteSqlAsync($@"
{MigrationSql.Business(slot45, "Slot 45", slotMinutes: 45)}
{MigrationSql.Business(slot0, "Slot 0", slotMinutes: 0)}
{MigrationSql.CustomerWithoutShadowColumn(customer45, slot45, 1)}
{MigrationSql.CustomerWithoutShadowColumn(customer0, slot0, 1)}
{MigrationSql.Appointment(appointment45, slot45, customer45, "ORD-45")}
{MigrationSql.Appointment(appointment0, slot0, customer0, "ORD-0")}");

            await database.MigrateToAsync(MigrationSql.AddAppointmentDuration);

            (await database.ScalarAsync($"SELECT DurationMinutes AS Value FROM Appointments WHERE Id = '{appointment45}'")).Should().Be(45);
            (await database.ScalarAsync($"SELECT DurationMinutes AS Value FROM Appointments WHERE Id = '{appointment0}'")).Should()
                .Be(30, "a business without a valid slot length gets the same fallback the app uses");

            await database.MigrateToAsync(MigrationSql.TenantForeignKeys);

            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Appointments' AND COLUMN_NAME = 'DurationMinutes'")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Appointments")).Should().Be(2, "rolling back keeps the appointments");
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
