using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Email;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static AppointMe.Tests.Integration.SqlServer.SqlServerSeed;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>
    /// Editing an appointment's services through the real service and repositories on real SQL Server.
    /// Service lines have a composite key that the app sets itself, which makes EF's change tracking
    /// behave differently from entities with generated keys, so mocks can't show whether saving works.
    /// </summary>
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", SqlServerCollection.Category)]
    public class AppointmentServiceLinesTests
    {
        private readonly SqlServerTestDatabase _database;

        public AppointmentServiceLinesTests(SqlServerDatabaseFixture fixture)
        {
            _database = fixture.Database;
        }

        private static AppointmentService ServiceFor(ApplicationDbContext db)
        {
            return new AppointmentService(
                new AppointmentRepository(db),
                new CustomerRepository(db),
                new ServiceOfferingRepository(db),
                new NoHolidays(),
                new BusinessRepository(db),
                new NoEmail(),
                NullLogger<AppointmentService>.Instance,
                TimeProvider.System);
        }

        private sealed class NoHolidays : IHolidayService
        {
            public Task<HashSet<DateOnly>> GetHolidayDatesAsync(int year, string countryCode) => Task.FromResult(new HashSet<DateOnly>());
            public Task<List<HolidayDTO>> GetHolidaysAsync(int year, string countryCode) => Task.FromResult(new List<HolidayDTO>());
        }

        private sealed class NoEmail : IEmailService
        {
            public Task SendEmailAsync(EmailMessage message) => Task.CompletedTask;
            public Task SendEmailWithAttachmentAsync(EmailMessage message, byte[] attachmentBytes, string attachmentFileName, string contentType) => Task.CompletedTask;
        }

        private async Task<(Guid Tenant, Guid Appointment, Guid A, Guid B)> SeedAsync()
        {
            await using var db = _database.CreateContext();
            var business = await AddBusinessAsync(db);
            var customer = NewCustomer(business.Id);
            var appointment = NewAppointment(business.Id, customer.Id);
            var a = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = business.Id, Name = "A", Price = 100 };
            var b = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = business.Id, Name = "B", Price = 200 };
            db.AddRange(customer, appointment, a, b);
            db.AppointmentServices.Add(new AppointmentServiceModel { AppointmentId = appointment.Id, ServiceOfferingId = a.Id, PriceAtBooking = 100 });
            await db.SaveChangesAsync();
            return (business.Id, appointment.Id, a.Id, b.Id);
        }

        private async Task SaveAsync(Guid tenant, Guid appointment, params Guid[] services)
        {
            await using var db = _database.CreateContext();
            var stored = await db.Appointments.AsNoTracking().FirstAsync(x => x.Id == appointment);
            await ServiceFor(db).UpdateAppointmentAsync(appointment, new UpdateAppointmentDTO
            {
                AppointmentDate = stored.AppointmentDate,
                OrderNumber = stored.OrderNumber,
                Description = "Edited",
                Status = AppointmentStatus.Scheduled,
                ServiceOfferingIds = services.ToList()
            }, tenant);
        }

        private async Task<List<Guid>> LinesAsync(Guid appointment)
        {
            await using var db = _database.CreateContext();
            return await db.AppointmentServices.Where(x => x.AppointmentId == appointment).Select(x => x.ServiceOfferingId).ToListAsync();
        }

        [Fact]
        public async Task Edit_KeepsThePriceOfKeptServices_AndStoresTheNewDuration()
        {
            var (tenant, appointment, a, b) = await SeedAsync();
            await using (var db = _database.CreateContext())
            {
                var service = await db.ServiceOfferings.FirstAsync(s => s.Id == a);
                service.Price = 999;
                var added = await db.ServiceOfferings.FirstAsync(s => s.Id == b);
                added.DurationMinutes = 45;
                await db.SaveChangesAsync();
            }

            await SaveAsync(tenant, appointment, a, b);

            await using var check = _database.CreateContext();
            var lines = await check.AppointmentServices.Where(x => x.AppointmentId == appointment).ToListAsync();
            lines.Single(x => x.ServiceOfferingId == a).PriceAtBooking.Should().Be(100);
            lines.Single(x => x.ServiceOfferingId == b).PriceAtBooking.Should().Be(200);
            (await check.Appointments.FirstAsync(x => x.Id == appointment)).DurationMinutes.Should()
                .Be(90, "one slot + 45 = 75, rounded up to the 30-minute grid");
        }

        [Fact]
        public async Task Edit_SameServices_Saves()
        {
            var (tenant, appointment, a, _) = await SeedAsync();

            await SaveAsync(tenant, appointment, a);

            (await LinesAsync(appointment)).Should().BeEquivalentTo(new[] { a });
        }

        [Fact]
        public async Task Edit_AddingAService_Saves()
        {
            var (tenant, appointment, a, b) = await SeedAsync();

            await SaveAsync(tenant, appointment, a, b);

            (await LinesAsync(appointment)).Should().BeEquivalentTo(new[] { a, b });
        }

        [Fact]
        public async Task Edit_RemovingAService_Saves()
        {
            var (tenant, appointment, _, _) = await SeedAsync();

            await SaveAsync(tenant, appointment);

            (await LinesAsync(appointment)).Should().BeEmpty();
        }

        [Fact]
        public async Task Edit_ReplacingAService_Saves()
        {
            var (tenant, appointment, _, b) = await SeedAsync();

            await SaveAsync(tenant, appointment, b);

            (await LinesAsync(appointment)).Should().BeEquivalentTo(new[] { b });
        }
    }
}
