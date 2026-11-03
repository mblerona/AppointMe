using FluentAssertions;
using Xunit;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>
    /// Runs migrations on their own throwaway databases: seed rows in an older schema with raw SQL,
    /// migrate up, check the data, then migrate down. Each test owns its database, so keep them few:
    /// creating a LocalDB database and running all migrations takes about 10 s. The empty-database case
    /// is covered by SqlServerDatabaseFixture, which migrates an empty database to the latest version.
    /// </summary>
    [Trait("Category", SqlServerCollection.Category)]
    public class MigrationTests
    {
        private const string BeforeConfirmExistingUsers = "20251224215944_EditOrderNumber";
        private const string ConfirmExistingUsers = "20261103141300_ConfirmExistingUsers";

        private static string User(string id, string? tenantSql, bool confirmed) => $@"
INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
    PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnabled, AccessFailedCount, FirstName, LastName, CreatedAt, UpdatedAt, TenantId)
VALUES ('{id}', '{id}@x', UPPER('{id}@X'), '{id}@x', UPPER('{id}@X'), {(confirmed ? 1 : 0)},
    0, 0, 1, 0, 'F', 'L', GETUTCDATE(), GETUTCDATE(), {tenantSql ?? "NULL"});";

        [Fact]
        public async Task ConfirmExistingUsers_ConfirmsUsers_AndCreatesMissingBusinesses()
        {
            await using var database = new SqlServerTestDatabase();
            await database.MigrateToAsync(BeforeConfirmExistingUsers);

            const string existingBusiness = "11111111-0000-0000-0000-000000000001";
            const string orphanTenant = "22222222-0000-0000-0000-000000000002";
            await database.ExecuteSqlAsync($@"
INSERT INTO Businesses (Id, Name, DefaultSlotMinutes, WorkDayStart, WorkDayEnd,
    OpenMon, OpenTue, OpenWed, OpenThu, OpenFri, OpenSat, OpenSun)
VALUES ('{existingBusiness}', N'Existing', 30, '09:00', '17:00', 1, 1, 1, 1, 1, 0, 0);
{User("ok", $"'{existingBusiness}'", confirmed: true)}
{User("no-tenant", null, confirmed: false)}
{User("orphan-1", $"'{orphanTenant}'", confirmed: false)}
{User("orphan-2", $"'{orphanTenant}'", confirmed: false)}");

            await database.MigrateToAsync(ConfirmExistingUsers);

            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM AspNetUsers WHERE EmailConfirmed = 0")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM AspNetUsers WHERE TenantId IS NULL")).Should().Be(0);
            (await database.ScalarAsync(@"SELECT COUNT(*) AS Value FROM AspNetUsers u
                WHERE NOT EXISTS (SELECT 1 FROM Businesses b WHERE b.Id = u.TenantId)")).Should().Be(0);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Businesses")).Should()
                .Be(3, "the existing one, one for the user without a tenant, one shared by both orphans");
            (await database.ScalarAsync($"SELECT COUNT(*) AS Value FROM Businesses WHERE Id = '{existingBusiness}' AND Name = N'Existing'"))
                .Should().Be(1, "an existing Business is left alone");

            // Down is intentionally empty (data-only migration), so rolling back must work and keep the data.
            await database.MigrateToAsync(BeforeConfirmExistingUsers);
            (await database.ScalarAsync("SELECT COUNT(*) AS Value FROM Businesses")).Should().Be(3);
        }
    }
}
