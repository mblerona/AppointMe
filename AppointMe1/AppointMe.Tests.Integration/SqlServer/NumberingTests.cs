using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.TimeZones;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Implementation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static AppointMe.Tests.Integration.SqlServer.SqlServerSeed;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>
    /// Audit B10 on real SQL Server. Only real unique indexes and real parallel requests show whether
    /// numbers stay unique. Each request gets its own DbContext, as it does in the app.
    /// </summary>
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", SqlServerCollection.Category)]
    public class NumberingTests
    {
        private readonly SqlServerTestDatabase _database;

        public NumberingTests(SqlServerDatabaseFixture fixture)
        {
            _database = fixture.Database;
        }

        private static CustomerService CustomersFor(ApplicationDbContext db) =>
            new(new CustomerRepository(db), new BusinessRepository(db));

        private static InvoiceService InvoicesFor(ApplicationDbContext db) =>
            new(new InvoiceRepository(db), new AppointmentRepository(db), new BusinessRepository(db), TimeProvider.System);

        private static CreateCustomerDTO NewCustomerDto(string email) => new()
        {
            FirstName = "Ana", LastName = "Doe", Email = email, PhoneNumber = "070123456", State = "North Macedonia", City = "Skopje"
        };

        private async Task<Guid> AddBusinessAsync()
        {
            await using var db = _database.CreateContext();
            return (await SqlServerSeed.AddBusinessAsync(db)).Id;
        }

        private async Task<CustomerDTO> CreateCustomerAsync(Guid tenant, string email)
        {
            await using var db = _database.CreateContext();
            return await CustomersFor(db).CreateCustomerAsync(NewCustomerDto(email), tenant);
        }

        [Fact]
        public async Task ParallelNewCustomers_AllSave_WithDifferentNumbers()
        {
            var tenant = await AddBusinessAsync();

            var created = await Task.WhenAll(Enumerable.Range(1, 4)
                .Select(i => Task.Run(() => CreateCustomerAsync(tenant, $"{Guid.NewGuid():N}@test.com"))));

            created.Select(c => c.CustomerNumber).Should().BeEquivalentTo(new[] { 1, 2, 3, 4 });
        }

        [Fact]
        public async Task ParallelCustomersWithTheSameEmail_OneSaves_TheOtherGetsTheFriendlyError()
        {
            var tenant = await AddBusinessAsync();
            var email = $"{Guid.NewGuid():N}@test.com";

            var results = await Task.WhenAll(Enumerable.Range(1, 2).Select(_ => Task.Run(async () =>
            {
                try
                {
                    await CreateCustomerAsync(tenant, email);
                    return null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            })));

            results.Count(r => r == null).Should().Be(1);
            results.Single(r => r != null).Should().BeOfType<BusinessRuleException>().Which.PropertyName.Should().Be("Email");
        }

        [Fact]
        public async Task CustomerNumbers_ArePerTenant()
        {
            var a = await AddBusinessAsync();
            var b = await AddBusinessAsync();

            await CreateCustomerAsync(a, $"{Guid.NewGuid():N}@test.com");
            var inB = await CreateCustomerAsync(b, $"{Guid.NewGuid():N}@test.com");

            inB.CustomerNumber.Should().Be(1);
        }

        private async Task<(Guid Tenant, Guid[] Appointments)> SeedAppointmentsAsync(int count)
        {
            await using var db = _database.CreateContext();
            var business = await SqlServerSeed.AddBusinessAsync(db);
            var customer = NewCustomer(business.Id);
            var appointments = Enumerable.Range(0, count).Select(_ => NewAppointment(business.Id, customer.Id)).ToArray();
            db.Add(customer);
            db.AddRange(appointments.Cast<object>());
            await db.SaveChangesAsync();
            return (business.Id, appointments.Select(a => a.Id).ToArray());
        }

        private async Task<InvoiceDTO> CreateInvoiceAsync(Guid appointment, Guid tenant)
        {
            await using var db = _database.CreateContext();
            return await InvoicesFor(db).CreateOrGetForAppointmentAsync(appointment, tenant);
        }

        [Fact]
        public async Task InvoiceNumbers_KeepCountingPast9999()
        {
            var (tenant, appointments) = await SeedAppointmentsAsync(2);
            var year = BusinessTimeZones.Now(TimeProvider.System, BusinessTimeZones.Default).Year;
            await using (var db = _database.CreateContext())
            {
                var existing = NewInvoice(tenant, Guid.NewGuid(), Guid.NewGuid(), $"INV-{year}-9999");
                existing.InvoiceYear = year;
                existing.InvoiceSequence = 9999;
                db.Invoices.Add(existing);
                await db.SaveChangesAsync();
            }

            (await CreateInvoiceAsync(appointments[0], tenant)).InvoiceNumber.Should().Be($"INV-{year}-10000");
            (await CreateInvoiceAsync(appointments[1], tenant)).InvoiceNumber.Should()
                .Be($"INV-{year}-10001", "as text, 9999 would still sort highest and this would clash with 10000");
        }

        [Fact]
        public async Task ParallelNewInvoices_AllSave_WithDifferentNumbers()
        {
            var (tenant, appointments) = await SeedAppointmentsAsync(3);

            var created = await Task.WhenAll(appointments.Select(a => Task.Run(() => CreateInvoiceAsync(a, tenant))));

            created.Select(i => i.InvoiceNumber).Should().OnlyHaveUniqueItems();
            await using var check = _database.CreateContext();
            var sequences = await check.Invoices.Where(i => i.TenantId == tenant).Select(i => i.InvoiceSequence).ToListAsync();
            sequences.Should().BeEquivalentTo(new[] { 1, 2, 3 });
        }

        [Fact]
        public async Task TheSameYearAndSequence_IsRefusedWithinATenant_ButAllowedAcrossTenants()
        {
            await using var db = _database.CreateContext();
            var a = await SqlServerSeed.AddBusinessAsync(db);
            var b = await SqlServerSeed.AddBusinessAsync(db);

            Invoice Numbered(Guid tenant, string number)
            {
                var invoice = NewInvoice(tenant, Guid.NewGuid(), Guid.NewGuid(), number);
                invoice.InvoiceYear = 2030;
                invoice.InvoiceSequence = 5;
                return invoice;
            }

            db.Invoices.AddRange(Numbered(a.Id, "INV-2030-0005"), Numbered(b.Id, "INV-2030-0005"));
            await db.SaveChangesAsync();

            db.Invoices.Add(Numbered(a.Id, "INV-2030-5"));

            await db.ShouldFailWithSqlErrorAsync(UniqueIndexViolation);
        }
    }
}
