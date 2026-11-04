using AppointMe.Repository.Implementation;
using FluentAssertions;
using Xunit;
using static AppointMe.Tests.Integration.SqlServer.SqlServerSeed;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>
    /// Audit B11: the overlap check adds a column value to a date inside the query. InMemory runs that
    /// as plain C#, so only real SQL Server proves that it translates and returns the right answer.
    /// </summary>
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", SqlServerCollection.Category)]
    public class AppointmentDurationTests
    {
        private readonly SqlServerTestDatabase _database;

        public AppointmentDurationTests(SqlServerDatabaseFixture fixture)
        {
            _database = fixture.Database;
        }

        [Fact]
        public async Task OverlapCheck_UsesEachAppointmentsStoredLength()
        {
            await using var db = _database.CreateContext();
            var business = await AddBusinessAsync(db);
            var customer = NewCustomer(business.Id);
            var existing = NewAppointment(business.Id, customer.Id);
            existing.DurationMinutes = 60;
            db.AddRange(customer, existing);
            await db.SaveChangesAsync();

            var repo = new AppointmentRepository(db);
            var start = existing.AppointmentDate;

            (await repo.IsTimeSlotAvailableAsync(start.AddMinutes(30), 15, business.Id)).Should().BeFalse("10:30 is inside 10:00-11:00");
            (await repo.IsTimeSlotAvailableAsync(start.AddMinutes(60), 15, business.Id)).Should().BeTrue("11:00 is exactly where it ends");
            (await repo.IsTimeSlotAvailableAsync(start.AddMinutes(30), 15, business.Id, existing.Id)).Should().BeTrue("moving it must not clash with itself");
        }
    }
}
