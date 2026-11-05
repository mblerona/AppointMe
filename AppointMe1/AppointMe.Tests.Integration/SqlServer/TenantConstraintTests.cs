using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static AppointMe.Tests.Integration.SqlServer.SqlServerSeed;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>Batch 3 step 2 (S3, A6, A7) on real SQL Server.</summary>
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", SqlServerCollection.Category)]
    public class TenantConstraintTests
    {
        private readonly SqlServerTestDatabase _database;

        public TenantConstraintTests(SqlServerDatabaseFixture fixture)
        {
            _database = fixture.Database;
        }

        // ---------------- S3: order number unique per tenant ----------------

        [Fact]
        public async Task SameOrderNumber_IsAllowedInDifferentTenants()
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);
            var b = await AddBusinessAsync(db);
            var ca = NewCustomer(a.Id);
            var cb = NewCustomer(b.Id);
            db.AddRange(ca, cb, NewAppointment(a.Id, ca.Id, "ORD-0001"), NewAppointment(b.Id, cb.Id, "ORD-0001"));

            var act = () => db.SaveChangesAsync();

            await act.Should().NotThrowAsync();
        }

        [Fact]
        public async Task SameOrderNumber_IsRejectedWithinOneTenant()
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);
            var c = NewCustomer(a.Id);
            db.AddRange(c, NewAppointment(a.Id, c.Id, "ORD-0002"));
            await db.SaveChangesAsync();

            db.Appointments.Add(NewAppointment(a.Id, c.Id, "ORD-0002"));

            await db.ShouldFailWithSqlErrorAsync(UniqueIndexViolation);
        }

        // ---------------- A7: foreign keys to Businesses ----------------

        [Fact]
        public async Task Customer_WithUnknownTenant_IsRejected()
        {
            await using var db = _database.CreateContext();
            db.Customers.Add(NewCustomer(Guid.NewGuid()));

            await db.ShouldFailWithSqlErrorAsync(ForeignKeyViolation);
        }

        [Fact]
        public async Task Appointment_WithUnknownTenant_IsRejected()
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);
            var c = NewCustomer(a.Id);
            db.Customers.Add(c);
            await db.SaveChangesAsync();

            db.Appointments.Add(NewAppointment(Guid.NewGuid(), c.Id));

            await db.ShouldFailWithSqlErrorAsync(ForeignKeyViolation);
        }

        [Fact]
        public async Task Invoice_WithUnknownTenant_IsRejected()
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);
            var c = NewCustomer(a.Id);
            var appt = NewAppointment(a.Id, c.Id);
            db.AddRange(c, appt);
            await db.SaveChangesAsync();

            db.Invoices.Add(NewInvoice(Guid.NewGuid(), appt.Id, c.Id, "INV-2026-0001"));

            await db.ShouldFailWithSqlErrorAsync(ForeignKeyViolation);
        }

        [Fact]
        public async Task User_WithoutTenant_IsAllowed_ButAnUnknownTenantIsRejected()
        {
            await using var db = _database.CreateContext();

            db.Users.Add(NewUser(tenantId: null));
            await db.SaveChangesAsync();

            db.Users.Add(NewUser(tenantId: Guid.NewGuid()));
            await db.ShouldFailWithSqlErrorAsync(ForeignKeyViolation);
        }

        [Theory]
        [InlineData("Customers")]
        [InlineData("Appointments")]
        [InlineData("ServiceOfferings")]
        [InlineData("ServiceCategories")]
        [InlineData("AspNetUsers")]
        public async Task Business_WithData_CannotBeDeleted(string dependent)
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);
            var c = NewCustomer(a.Id);
            switch (dependent)
            {
                case "Customers": db.Customers.Add(c); break;
                case "Appointments": db.AddRange(c, NewAppointment(a.Id, c.Id)); break;
                case "ServiceOfferings": db.ServiceOfferings.Add(new ServiceOffering { Id = Guid.NewGuid(), BusinessId = a.Id, Name = "Cut" }); break;
                case "ServiceCategories": db.ServiceCategories.Add(new ServiceCategory { Id = Guid.NewGuid(), BusinessId = a.Id, Name = "Hair" }); break;
                case "AspNetUsers": db.Users.Add(NewUser(a.Id)); break;
            }
            await db.SaveChangesAsync();

            // Raw SQL so the database itself decides (no cascade, no client-side delete).
            var act = () => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Businesses WHERE Id = {a.Id}");

            (await act.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(ForeignKeyViolation);
            (await db.Businesses.AsNoTracking().AnyAsync(b => b.Id == a.Id)).Should().BeTrue();
        }

        [Fact]
        public async Task Business_WithoutData_CanBeDeleted()
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);

            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Businesses WHERE Id = {a.Id}");

            (await db.Businesses.AsNoTracking().AnyAsync(b => b.Id == a.Id)).Should().BeFalse();
        }

        [Fact]
        public async Task AllForeignKeysToBusinesses_AreNoAction()
        {
            var cascading = await _database.ScalarAsync(@"SELECT COUNT(*) AS Value FROM sys.foreign_keys
                WHERE referenced_object_id = OBJECT_ID('Businesses') AND delete_referential_action <> 0");
            var total = await _database.ScalarAsync(@"SELECT COUNT(*) AS Value FROM sys.foreign_keys
                WHERE referenced_object_id = OBJECT_ID('Businesses')");

            cascading.Should().Be(0);
            total.Should().Be(6, "Customers, Appointments, Invoices, AspNetUsers, ServiceOfferings, ServiceCategories");
        }

        // ---------------- A6: shadow column ----------------

        [Fact]
        public async Task Customers_NoLongerHaveTheShadowBusinessIdColumn()
        {
            (await _database.ScalarAsync(@"SELECT COUNT(*) AS Value FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Customers' AND COLUMN_NAME = 'BusinessId'")).Should().Be(0);
        }

        private static AppointMeAppUser NewUser(Guid? tenantId)
        {
            var name = $"{Guid.NewGuid():N}@test.com";
            return new AppointMeAppUser
            {
                Id = Guid.NewGuid().ToString(),
                UserName = name,
                NormalizedUserName = name.ToUpperInvariant(),
                Email = name,
                NormalizedEmail = name.ToUpperInvariant(),
                FirstName = "T",
                LastName = "U",
                TenantId = tenantId
            };
        }
    }
}
