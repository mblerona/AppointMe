using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AppointMe.Tests.Integration.Repositories
{
    public class AppointmentRepositoryTests
    {
        // ----------------- seed helpers -----------------

        private static Business SeedBusiness(ApplicationDbContext db, Guid tenantId)
        {
            var biz = new Business
            {
                Id = tenantId,
                Name = "Test Business",
                Address = "Skopje",
                EnableInvoices = true,
                OpenMon = true,
                OpenTue = true,
                OpenWed = true,
                OpenThu = true,
                OpenFri = true,
                OpenSat = false,
                OpenSun = false,
                WorkDayStart = new TimeSpan(8, 0, 0),
                WorkDayEnd = new TimeSpan(18, 0, 0),
                DefaultSlotMinutes = 30
            };

            db.Businesses.Add(biz);
            return biz;
        }

        private static Customer SeedCustomer(ApplicationDbContext db, Guid tenantId, string email = "ana@test.com", int customerNumber = 1)
        {
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FirstName = "Ana",
                LastName = "Doe",
                Email = email,
                PhoneNumber = "070123456",
                City = "Skopje",
                State = "North Macedonia",
                CustomerNumber = customerNumber
            };

            db.Customers.Add(customer);
            return customer;
        }

        private static (ServiceCategory cat, ServiceOffering svc) SeedCategoryAndService(ApplicationDbContext db, Guid tenantId)
        {
            var cat = new ServiceCategory
            {
                Id = Guid.NewGuid(),
                BusinessId = tenantId,
                Name = "Hair"
            };
            db.ServiceCategories.Add(cat);

            var svc = new ServiceOffering
            {
                Id = Guid.NewGuid(),
                BusinessId = tenantId,
                Name = "Haircut",
                Price = 500,
                IsActive = true,
                CategoryId = cat.Id,
                Category = cat
            };
            db.ServiceOfferings.Add(svc);

            return (cat, svc);
        }

        private static Appointment CreateAppointment(Guid tenantId, Guid customerId, DateTime date, string orderNumber, AppointmentStatus status = AppointmentStatus.Scheduled)
        {
            return new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customerId,
                OrderNumber = orderNumber,
                Description = "Test description",
                AppointmentDate = date,
                Status = status,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        // ----------------- tests -----------------

        [Fact]
        public async Task GetByIdAsync_ShouldLoadCustomerAndServicesGraph()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);
            var (_, svc) = SeedCategoryAndService(db, tenantId);

            var appt = CreateAppointment(tenantId, customer.Id, DateTime.Today.AddDays(1).AddHours(10), "ORD-1");
            appt.AppointmentServices.Add(new AppointmentServiceModel
            {
                AppointmentId = appt.Id,
                ServiceOfferingId = svc.Id,
                PriceAtBooking = svc.Price
            });

            db.Appointments.Add(appt);
            await db.SaveChangesAsync();

            var loaded = await repo.GetByIdAsync(appt.Id, tenantId);

            loaded.Should().NotBeNull();
            loaded!.Customer.Should().NotBeNull();
            loaded.AppointmentServices.Should().HaveCount(1);
            loaded.AppointmentServices.First().ServiceOffering.Should().NotBeNull();
            loaded.AppointmentServices.First().ServiceOffering!.Category.Should().NotBeNull();
        }

        [Fact]
        public async Task GetByIdAsync_ShouldReturnNull_ForOtherTenant()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            var customerA = SeedCustomer(db, tenantA);

            var appt = CreateAppointment(tenantA, customerA.Id, DateTime.Today.AddDays(1).AddHours(10), "ORD-2");
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();

            var loaded = await repo.GetByIdAsync(appt.Id, tenantB);
            loaded.Should().BeNull();
        }

        [Fact]
        public async Task GetWithCustomerAsync_ShouldLoadCustomer_AndRespectTenant()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            var appt = CreateAppointment(tenantId, customer.Id, DateTime.Today.AddDays(1).AddHours(9), "ORD-3");
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();

            var loaded = await repo.GetWithCustomerAsync(appt.Id, tenantId);

            loaded.Should().NotBeNull();
            loaded!.Customer.Should().NotBeNull();
            loaded.Customer!.Email.Should().Be(customer.Email);
        }

        [Fact]
        public async Task GetByCustomerIdAsync_ShouldReturnOnlyTenantAppointments_OrderedDesc()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            var customerA = SeedCustomer(db, tenantA, "a@test.com");

            var older = CreateAppointment(tenantA, customerA.Id, DateTime.Today.AddDays(1).AddHours(10), "ORD-4");
            var newer = CreateAppointment(tenantA, customerA.Id, DateTime.Today.AddDays(2).AddHours(10), "ORD-5");

            db.Appointments.AddRange(older, newer);

            // noise: other tenant
            var customerB = SeedCustomer(db, tenantB, "b@test.com");
            db.Appointments.Add(CreateAppointment(tenantB, customerB.Id, DateTime.Today.AddDays(2).AddHours(10), "ORD-6"));

            await db.SaveChangesAsync();

            var result = (await repo.GetByCustomerIdAsync(customerA.Id, tenantA)).ToList();

            result.Should().HaveCount(2);
            result.First().OrderNumber.Should().Be("ORD-5"); // newest first
            result.Last().OrderNumber.Should().Be("ORD-4");
            result.All(a => a.TenantId == tenantA).Should().BeTrue();
        }

        [Fact]
        public async Task GetByDateRangeAsync_ShouldBeInclusive_AndOrderedAsc()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            var d1 = DateTime.Today.AddDays(1).AddHours(9);
            var d2 = DateTime.Today.AddDays(1).AddHours(10);
            var d3 = DateTime.Today.AddDays(1).AddHours(11);

            db.Appointments.AddRange(
                CreateAppointment(tenantId, customer.Id, d1, "ORD-7"),
                CreateAppointment(tenantId, customer.Id, d2, "ORD-8"),
                CreateAppointment(tenantId, customer.Id, d3, "ORD-9")
            );
            await db.SaveChangesAsync();

            var result = (await repo.GetByDateRangeAsync(d1, d2, tenantId)).ToList();

            result.Select(x => x.OrderNumber).Should().ContainInOrder("ORD-7", "ORD-8");
            result.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetByStatusAsync_ShouldFilterCorrectly_AndOrderAsc()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            var t1 = DateTime.Today.AddDays(1).AddHours(9);
            var t2 = DateTime.Today.AddDays(1).AddHours(10);

            db.Appointments.AddRange(
                CreateAppointment(tenantId, customer.Id, t2, "ORD-10", AppointmentStatus.Scheduled),
                CreateAppointment(tenantId, customer.Id, t1, "ORD-11", AppointmentStatus.Scheduled),
                CreateAppointment(tenantId, customer.Id, t1.AddHours(5), "ORD-12", AppointmentStatus.Cancelled)
            );
            await db.SaveChangesAsync();

            var result = (await repo.GetByStatusAsync(AppointmentStatus.Scheduled, tenantId)).ToList();

            result.Should().HaveCount(2);
            result.Select(x => x.OrderNumber).Should().ContainInOrder("ORD-11", "ORD-10"); // asc by date
        }

        [Fact]
        public async Task OrderNumberExistsAsync_ShouldReturnTrue_WhenExists()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            db.Appointments.Add(CreateAppointment(tenantId, customer.Id, DateTime.Today.AddDays(1), "ORD-13"));
            await db.SaveChangesAsync();

            var exists = await repo.OrderNumberExistsAsync("ORD-13");
            exists.Should().BeTrue();
        }

        [Fact]
        public async Task OrderNumberExistsAsync_ShouldReturnFalse_WhenNotExists()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var exists = await repo.OrderNumberExistsAsync("NOPE");
            exists.Should().BeFalse();
        }

        [Fact]
        public async Task OrderNumberExistsAsync_ShouldIgnoreSameAppointment_WhenExcluded()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            var appt = CreateAppointment(tenantId, customer.Id, DateTime.Today.AddDays(1), "ORD-14");
            db.Appointments.Add(appt);
            await db.SaveChangesAsync();

            var exists = await repo.OrderNumberExistsAsync("ORD-14", appt.Id);
            exists.Should().BeFalse();
        }

        [Fact]
        public async Task IsTimeSlotAvailableAsync_ShouldIgnoreCancelledAppointments()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            var start = DateTime.Today.AddDays(1).AddHours(10);

            db.Appointments.Add(CreateAppointment(tenantId, customer.Id, start, "ORD-15", AppointmentStatus.Cancelled));
            await db.SaveChangesAsync();

            var available = await repo.IsTimeSlotAvailableAsync(start, 30, tenantId);
            available.Should().BeTrue();
        }

        [Fact]
        public async Task IsTimeSlotAvailableAsync_WhenDurationIsZero_ShouldDefaultTo30()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            var start = DateTime.Today.AddDays(1).AddHours(10);

            // existing appointment at 10:00 (duration assumed 30 by repo when checking)
            db.Appointments.Add(CreateAppointment(tenantId, customer.Id, start, "ORD-16", AppointmentStatus.Scheduled));
            await db.SaveChangesAsync();

            // durationMinutes = 0 -> repo should treat it as 30
            var available = await repo.IsTimeSlotAvailableAsync(start, 0, tenantId);
            available.Should().BeFalse();
        }

        [Fact]
        public async Task IsTimeSlotAvailableAsync_WhenNewStartsExactlyAtEnd_ShouldReturnTrue()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            var start = DateTime.Today.AddDays(1).AddHours(10);
            db.Appointments.Add(CreateAppointment(tenantId, customer.Id, start, "ORD-17", AppointmentStatus.Scheduled));
            await db.SaveChangesAsync();

            var newStart = start.AddMinutes(30);

            var available = await repo.IsTimeSlotAvailableAsync(newStart, 30, tenantId);
            available.Should().BeTrue();
        }
    }
}
