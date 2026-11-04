using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Implementation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace AppointMe.Tests.Unit.Services
{
    /// <summary>
    /// Retry behaviour of CreateOrGetForAppointmentAsync against a real DbContext (B7).
    /// Mocked repositories cannot show the bug: the failed invoice stayed tracked as Added,
    /// so the retry inserted it again together with the new one.
    /// </summary>
    public class InvoiceServiceRetryTests
    {
        private readonly InMemoryDatabaseRoot _root = new();
        private readonly string _dbName = $"invoice-retry-{Guid.NewGuid()}";
        private readonly Guid _tenantId = Guid.NewGuid();

        /// <summary>Throws DbUpdateException (like a unique-index violation) while invoices are being inserted.</summary>
        private sealed class FailInvoiceInsertInterceptor : SaveChangesInterceptor
        {
            private int _failuresLeft;
            private readonly Action? _onFailure;

            public FailInvoiceInsertInterceptor(int failures, Action? onFailure = null)
            {
                _failuresLeft = failures;
                _onFailure = onFailure;
            }

            public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                var insertingInvoice = eventData.Context!.ChangeTracker.Entries<Invoice>()
                    .Any(e => e.State == EntityState.Added);

                if (insertingInvoice && _failuresLeft > 0)
                {
                    _failuresLeft--;
                    _onFailure?.Invoke();
                    throw new DbUpdateException("Simulated unique index violation");
                }

                return base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        private ApplicationDbContext NewContext(params IInterceptor[] interceptors)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(_dbName, _root)
                .AddInterceptors(interceptors)
                .Options;
            return new ApplicationDbContext(options);
        }

        private Guid SeedAppointmentWithTwoServices()
        {
            using var db = NewContext();

            db.Businesses.Add(new Business { Id = _tenantId, Name = "Biz", EnableInvoices = true });

            var customer = new Customer
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, CustomerNumber = 1,
                FirstName = "Ana", LastName = "Doe", Email = "ana@test.com",
                PhoneNumber = "070123456", City = "Skopje", State = "MK"
            };
            db.Customers.Add(customer);

            var haircut = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = _tenantId, Name = "Haircut", Price = 100 };
            var wash = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = _tenantId, Name = "Wash", Price = 50 };
            db.ServiceOfferings.AddRange(haircut, wash);

            var appt = new Appointment
            {
                Id = Guid.NewGuid(), TenantId = _tenantId, CustomerId = customer.Id,
                OrderNumber = "ORD-1", Description = "desc", AppointmentDate = DateTime.UtcNow.AddDays(1)
            };
            appt.AppointmentServices.Add(new AppointmentServiceModel { AppointmentId = appt.Id, ServiceOfferingId = haircut.Id, PriceAtBooking = 100 });
            appt.AppointmentServices.Add(new AppointmentServiceModel { AppointmentId = appt.Id, ServiceOfferingId = wash.Id, PriceAtBooking = 50 });
            db.Appointments.Add(appt);

            db.SaveChanges();
            return appt.Id;
        }

        private static InvoiceService BuildService(ApplicationDbContext db)
            => new(new InvoiceRepository(db), new AppointmentRepository(db), new BusinessRepository(db), TimeProvider.System);

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_AfterOneFailedSave_StoresExactlyOneInvoice()
        {
            var apptId = SeedAppointmentWithTwoServices();

            await using var db = NewContext(new FailInvoiceInsertInterceptor(failures: 1));
            var result = await BuildService(db).CreateOrGetForAppointmentAsync(apptId, _tenantId);

            await using var check = NewContext();
            var invoices = await check.Invoices.Include(i => i.Lines).ToListAsync();

            invoices.Should().ContainSingle("the failed attempt must not be inserted again on retry");
            invoices[0].Id.Should().Be(result.Id);
            invoices[0].Lines.Should().HaveCount(2);
            (await check.InvoiceLines.CountAsync()).Should().Be(2, "no orphaned lines from the failed attempt");
        }

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_WhenAllThreeAttemptsFail_Rethrows_AndStoresNothing()
        {
            var apptId = SeedAppointmentWithTwoServices();

            await using var db = NewContext(new FailInvoiceInsertInterceptor(failures: 3));
            Func<Task> act = () => BuildService(db).CreateOrGetForAppointmentAsync(apptId, _tenantId);

            await act.Should().ThrowAsync<DbUpdateException>();

            await using var check = NewContext();
            (await check.Invoices.CountAsync()).Should().Be(0);
        }

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_WhenConcurrentRequestCreatedInvoice_ReturnsThatInvoice()
        {
            var apptId = SeedAppointmentWithTwoServices();

            // While our first save fails, "another request" stores the invoice for the same appointment.
            void ConcurrentInsert()
            {
                using var other = NewContext();
                other.Invoices.Add(new Invoice
                {
                    Id = Guid.NewGuid(), TenantId = _tenantId, AppointmentId = apptId, CustomerId = Guid.NewGuid(),
                    InvoiceNumber = "INV-CONCURRENT", CustomerNameSnapshot = "Ana Doe", BusinessNameSnapshot = "Biz"
                });
                other.SaveChanges();
            }

            await using var db = NewContext(new FailInvoiceInsertInterceptor(failures: 1, ConcurrentInsert));
            var result = await BuildService(db).CreateOrGetForAppointmentAsync(apptId, _tenantId);

            result.InvoiceNumber.Should().Be("INV-CONCURRENT");

            await using var check = NewContext();
            (await check.Invoices.CountAsync()).Should().Be(1);
        }
    }
}
