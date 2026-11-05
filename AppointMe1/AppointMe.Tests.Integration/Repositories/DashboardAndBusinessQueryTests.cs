using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AwesomeAssertions;
using Xunit;

namespace AppointMe.Tests.Integration.Repositories
{
    /// <summary>
    /// Repository methods added in Batch 2 step 3: BusinessRepository, the dashboard queries,
    /// CustomerRepository.CountByTenantAsync and the S2 category check. All are tenant-filtered.
    /// </summary>
    public class DashboardAndBusinessQueryTests
    {
        private static readonly Guid TenantA = Guid.NewGuid();
        private static readonly Guid TenantB = Guid.NewGuid();

        private static Customer AddCustomer(ApplicationDbContext db, Guid tenantId, string firstName, string phone = "070123456")
        {
            var c = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FirstName = firstName,
                LastName = "Doe",
                Email = $"{Guid.NewGuid():N}@test.com",
                PhoneNumber = phone,
                City = "Skopje",
                State = "North Macedonia",
                CustomerNumber = Random.Shared.Next(1, int.MaxValue)
            };
            db.Customers.Add(c);
            return c;
        }

        private static Appointment AddAppointment(
            ApplicationDbContext db, Guid tenantId, Customer customer, DateTime date,
            AppointmentStatus status = AppointmentStatus.Scheduled, string? orderNumber = null)
        {
            var a = new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customer.Id,
                Customer = customer,
                OrderNumber = orderNumber ?? $"ORD-{Guid.NewGuid():N}",
                Description = "Test",
                AppointmentDate = date,
                Status = status
            };
            db.Appointments.Add(a);
            return a;
        }

        // ---------------- BusinessRepository ----------------

        [Fact]
        public async Task BusinessRepository_ReturnsOnlyTheRequestedTenant()
        {
            using var db = DbContextFactory.Create();
            db.Businesses.Add(new Business { Id = TenantA, Name = "A" });
            db.Businesses.Add(new Business { Id = TenantB, Name = "B" });
            await db.SaveChangesAsync();
            var repo = new BusinessRepository(db);

            (await repo.GetByIdReadOnlyAsync(TenantA))!.Name.Should().Be("A");
            (await repo.GetByIdAsync(TenantB))!.Name.Should().Be("B");
            (await repo.GetByIdAsync(Guid.NewGuid())).Should().BeNull();
        }

        [Fact]
        public async Task BusinessRepository_ReadOnly_IsNotTracked_ButGetById_IsTracked()
        {
            using var db = DbContextFactory.Create();
            db.Businesses.Add(new Business { Id = TenantA, Name = "A" });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            var repo = new BusinessRepository(db);

            var readOnly = await repo.GetByIdReadOnlyAsync(TenantA);
            readOnly!.Name = "changed";
            await repo.SaveChangesAsync();
            db.ChangeTracker.Clear();
            (await repo.GetByIdReadOnlyAsync(TenantA))!.Name.Should().Be("A");

            var tracked = await repo.GetByIdAsync(TenantA);
            tracked!.Name = "changed";
            await repo.SaveChangesAsync();
            db.ChangeTracker.Clear();
            (await repo.GetByIdReadOnlyAsync(TenantA))!.Name.Should().Be("changed");
        }

        [Fact]
        public async Task BusinessRepository_Detach_PreventsAnAddedBusinessFromBeingSaved()
        {
            using var db = DbContextFactory.Create();
            var repo = new BusinessRepository(db);
            var business = new Business { Id = TenantA, Name = "A" };

            repo.Add(business);
            repo.Detach(business);
            await repo.SaveChangesAsync();

            (await repo.GetByIdReadOnlyAsync(TenantA)).Should().BeNull();
        }

        // ---------------- AppointmentRepository dashboard queries ----------------

        [Fact]
        public async Task CountByStatus_CountsOnlyTheTenantsAppointments()
        {
            using var db = DbContextFactory.Create();
            var a = AddCustomer(db, TenantA, "Ana");
            var b = AddCustomer(db, TenantB, "Bob");
            AddAppointment(db, TenantA, a, DateTime.Today, AppointmentStatus.Scheduled);
            AddAppointment(db, TenantA, a, DateTime.Today, AppointmentStatus.Scheduled);
            AddAppointment(db, TenantA, a, DateTime.Today, AppointmentStatus.Completed);
            AddAppointment(db, TenantB, b, DateTime.Today, AppointmentStatus.Cancelled);
            await db.SaveChangesAsync();

            var counts = await new AppointmentRepository(db).CountByStatusAsync(TenantA);

            counts[AppointmentStatus.Scheduled].Should().Be(2);
            counts[AppointmentStatus.Completed].Should().Be(1);
            counts.Should().NotContainKey(AppointmentStatus.Cancelled);
        }

        [Fact]
        public async Task GetForDashboard_FiltersByTenantRangeStatusAndSearch_NewestFirst()
        {
            using var db = DbContextFactory.Create();
            var ana = AddCustomer(db, TenantA, "Ana", phone: "070111222");
            var marko = AddCustomer(db, TenantA, "Marko");
            var other = AddCustomer(db, TenantB, "Ana");
            var day = new DateTime(2026, 11, 3);

            var older = AddAppointment(db, TenantA, ana, day.AddHours(9));
            var newer = AddAppointment(db, TenantA, ana, day.AddHours(15));
            AddAppointment(db, TenantA, ana, day.AddDays(1).AddHours(9));                      // outside range
            AddAppointment(db, TenantA, ana, day.AddHours(10), AppointmentStatus.Cancelled);   // other status
            AddAppointment(db, TenantA, marko, day.AddHours(11));                              // no search match
            AddAppointment(db, TenantB, other, day.AddHours(12));                              // other tenant
            await db.SaveChangesAsync();
            var repo = new AppointmentRepository(db);

            var rows = await repo.GetForDashboardAsync(
                TenantA, day, day.AddDays(1), AppointmentStatus.Scheduled, " Ana ", normalizedPhone: null, take: 20);

            rows.Select(r => r.Id).Should().Equal(newer.Id, older.Id);
            rows.Should().OnlyContain(r => r.Customer != null);

            (await repo.GetForDashboardAsync(TenantA, null, null, null, "070111", null, 20))
                .Should().HaveCount(4, "the search also matches phone numbers");
        }

        [Fact]
        public async Task GetForDashboard_RespectsTake_AndIgnoresRangeWithOnlyOneBound()
        {
            using var db = DbContextFactory.Create();
            var ana = AddCustomer(db, TenantA, "Ana");
            for (var i = 0; i < 5; i++)
                AddAppointment(db, TenantA, ana, new DateTime(2026, 1, 1).AddDays(i));
            await db.SaveChangesAsync();

            var rows = await new AppointmentRepository(db)
                .GetForDashboardAsync(TenantA, new DateTime(2026, 1, 3), null, null, null, null, take: 3);

            rows.Should().HaveCount(3);
            rows[0].AppointmentDate.Should().Be(new DateTime(2026, 1, 5));
        }

        // ---------------- CustomerRepository / ServiceOfferingRepository ----------------

        [Fact]
        public async Task NormalizedPhone_MatchesFirstOrSecondNumber_OnlyForTheTenant()
        {
            using var db = DbContextFactory.Create();
            var first = AddCustomer(db, TenantA, "First", phone: "+38970123456");
            var second = AddCustomer(db, TenantA, "Second", phone: "+38971000000");
            second.SecondPhoneNumber = "+38970123456";
            AddCustomer(db, TenantA, "Other", phone: "+38975000000");
            var otherTenant = AddCustomer(db, TenantB, "OtherTenant", phone: "+38970123456");
            AddAppointment(db, TenantA, first, DateTime.Today);
            AddAppointment(db, TenantB, otherTenant, DateTime.Today);
            await db.SaveChangesAsync();

            // The raw term "070 123 456" matches nothing; the E.164 form matches both numbers.
            var customers = await new CustomerRepository(db).SearchAsync("070 123 456", TenantA, "+38970123456");
            customers.Select(c => c.FirstName).Should().BeEquivalentTo("First", "Second");

            var rows = await new AppointmentRepository(db)
                .GetForDashboardAsync(TenantA, null, null, null, "070 123 456", "+38970123456", 20);
            rows.Should().ContainSingle().Which.CustomerId.Should().Be(first.Id);
        }

        [Fact]
        public async Task CustomerCountByTenant_CountsOnlyTheTenantsCustomers()
        {
            using var db = DbContextFactory.Create();
            AddCustomer(db, TenantA, "A1");
            AddCustomer(db, TenantA, "A2");
            AddCustomer(db, TenantB, "B1");
            await db.SaveChangesAsync();

            (await new CustomerRepository(db).CountByTenantAsync(TenantA)).Should().Be(2);
        }

        [Fact]
        public async Task InvoiceRepository_Detach_StopsTrackingTheInvoiceAndItsLines()
        {
            using var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                TenantId = TenantA,
                InvoiceNumber = "INV-DETACH",
                Lines = new List<InvoiceLine>
                {
                    new() { Id = Guid.NewGuid(), NameSnapshot = "A", Qty = 1, UnitPrice = 1, LineTotal = 1 },
                    new() { Id = Guid.NewGuid(), NameSnapshot = "B", Qty = 1, UnitPrice = 2, LineTotal = 2 }
                }
            };
            await repo.AddAsync(invoice);
            db.ChangeTracker.Entries().Should().HaveCount(3);

            repo.Detach(invoice);
            await repo.SaveChangesAsync();

            db.ChangeTracker.Entries().Should().BeEmpty();
            db.Invoices.Should().BeEmpty();
        }

        [Fact]
        public async Task CategoryBelongsToBusiness_IsTrueOnlyForTheOwningBusiness()
        {
            using var db = DbContextFactory.Create();
            var category = new ServiceCategory { Id = Guid.NewGuid(), BusinessId = TenantA, Name = "Hair" };
            db.ServiceCategories.Add(category);
            await db.SaveChangesAsync();
            var repo = new ServiceOfferingRepository(db);

            (await repo.CategoryBelongsToBusinessAsync(category.Id, TenantA)).Should().BeTrue();
            (await repo.CategoryBelongsToBusinessAsync(category.Id, TenantB)).Should().BeFalse();
            (await repo.CategoryBelongsToBusinessAsync(Guid.NewGuid(), TenantA)).Should().BeFalse();
        }
    }
}
