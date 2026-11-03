using AppointMe.Domain.DomainModels;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static AppointMe.Tests.Integration.SqlServer.SqlServerSeed;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>
    /// Constraints that already exist before Batch 3, checked on real SQL Server. They prove the fixture
    /// works and guard against a migration accidentally dropping them.
    /// </summary>
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", SqlServerCollection.Category)]
    public class ExistingConstraintTests
    {
        private readonly SqlServerTestDatabase _database;

        public ExistingConstraintTests(SqlServerDatabaseFixture fixture)
        {
            _database = fixture.Database;
        }

        [Fact]
        public async Task AllMigrationsAreApplied()
        {
            await using var db = _database.CreateContext();

            (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
            (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(m => m.EndsWith("_ConfirmExistingUsers"));
        }

        [Fact]
        public async Task CustomerEmail_IsUniquePerTenant_ButMayRepeatAcrossTenants()
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);
            var b = await AddBusinessAsync(db);

            db.Customers.Add(NewCustomer(a.Id, 1, "same@test.com"));
            db.Customers.Add(NewCustomer(b.Id, 1, "same@test.com"));
            await db.SaveChangesAsync();

            db.Customers.Add(NewCustomer(a.Id, 2, "same@test.com"));
            await db.ShouldFailWithSqlErrorAsync(UniqueIndexViolation);
        }

        [Fact]
        public async Task CustomerNumber_IsUniquePerTenant()
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);
            var b = await AddBusinessAsync(db);

            db.Customers.Add(NewCustomer(a.Id, 7));
            db.Customers.Add(NewCustomer(b.Id, 7));
            await db.SaveChangesAsync();

            db.Customers.Add(NewCustomer(a.Id, 7));
            await db.ShouldFailWithSqlErrorAsync(UniqueIndexViolation);
        }

        [Fact]
        public async Task OneInvoicePerAppointment()
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);
            var customer = NewCustomer(a.Id);
            var appointment = NewAppointment(a.Id, customer.Id);
            db.AddRange(customer, appointment);
            db.Invoices.Add(NewInvoice(a.Id, appointment.Id, customer.Id, "INV-2026-0001"));
            await db.SaveChangesAsync();

            db.Invoices.Add(NewInvoice(a.Id, appointment.Id, customer.Id, "INV-2026-0002"));
            await db.ShouldFailWithSqlErrorAsync(UniqueIndexViolation);
        }

        [Fact]
        public async Task InvoiceNumber_IsUniquePerTenant()
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);
            var customer = NewCustomer(a.Id);
            var first = NewAppointment(a.Id, customer.Id);
            var second = NewAppointment(a.Id, customer.Id);
            db.AddRange(customer, first, second);
            db.Invoices.Add(NewInvoice(a.Id, first.Id, customer.Id, "INV-2026-0001"));
            await db.SaveChangesAsync();

            db.Invoices.Add(NewInvoice(a.Id, second.Id, customer.Id, "INV-2026-0001"));
            await db.ShouldFailWithSqlErrorAsync(UniqueIndexViolation);
        }

        [Fact]
        public async Task ServiceCategory_RequiresAnExistingBusiness()
        {
            await using var db = _database.CreateContext();

            db.ServiceCategories.Add(new ServiceCategory { Id = Guid.NewGuid(), BusinessId = Guid.NewGuid(), Name = "Hair" });

            await db.ShouldFailWithSqlErrorAsync(ForeignKeyViolation);
        }

        [Fact]
        public async Task ServiceUsedByAnAppointment_CannotBeDeleted()
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);
            var customer = NewCustomer(a.Id);
            var appointment = NewAppointment(a.Id, customer.Id);
            var service = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = a.Id, Name = "Cut", Price = 10 };
            db.AddRange(customer, appointment, service);
            db.AppointmentServices.Add(new AppointmentServiceModel
            {
                AppointmentId = appointment.Id, ServiceOfferingId = service.Id, PriceAtBooking = 10
            });
            await db.SaveChangesAsync();

            // Raw SQL, so EF can't cascade or block it on the client: the database itself must refuse.
            var act = () => db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM ServiceOfferings WHERE Id = {service.Id}");

            (await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>()).Which.Number.Should().Be(ForeignKeyViolation);
        }

        [Fact]
        public async Task DeletingACustomer_CascadesToTheirAppointments()
        {
            await using var db = _database.CreateContext();
            var a = await AddBusinessAsync(db);
            var customer = NewCustomer(a.Id);
            var appointment = NewAppointment(a.Id, customer.Id);
            db.AddRange(customer, appointment);
            await db.SaveChangesAsync();

            await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Customers WHERE Id = {customer.Id}");

            (await db.Appointments.AsNoTracking().AnyAsync(x => x.Id == appointment.Id)).Should().BeFalse();
        }
    }
}
