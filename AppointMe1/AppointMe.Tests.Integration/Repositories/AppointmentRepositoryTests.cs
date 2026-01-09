using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using FluentAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace AppointMe.Tests.Integration.Repositories
{
    public class AppointmentRepositoryTests
    {
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

        private static Customer SeedCustomer(ApplicationDbContext db, Guid tenantId)
        {
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FirstName = "Ana",
                LastName = "Doe",
                Email = "ana@test.com",
                PhoneNumber = "070123456",
                City = "Skopje",
                State = "North Macedonia",
                CustomerNumber = 1
            };

            db.Customers.Add(customer);
            return customer;
        }

        [Fact]
        public async Task IsTimeSlotAvailableAsync_WhenOverlappingAppointmentExists_ShouldReturnFalse()
        {
            // Arrange
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            var date = DateTime.Today.AddDays(1).AddHours(10);

            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customer.Id,
                OrderNumber = "TEST-ORD-1",
                Description = "Test description",
                AppointmentDate = date,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            // Act
            var available = await repo.IsTimeSlotAvailableAsync(
                date,
                durationMinutes: 30,
                tenantId: tenantId,
                excludeAppointmentId: null
            );

            // Assert
            available.Should().BeFalse();
        }

        [Fact]
        public async Task IsTimeSlotAvailableAsync_WhenNoOverlap_ShouldReturnTrue()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            var date = DateTime.Today.AddDays(1).AddHours(10);

            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customer.Id,
                OrderNumber = "TEST-ORD-1",
                Description = "Test description",
                AppointmentDate = date,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            var available = await repo.IsTimeSlotAvailableAsync(
                date.AddHours(2),
                durationMinutes: 30,
                tenantId: tenantId,
                excludeAppointmentId: null
            );

            available.Should().BeTrue();
        }

        [Fact]
        public async Task IsTimeSlotAvailableAsync_ShouldIgnoreOtherTenants()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            var date = DateTime.Today.AddDays(1).AddHours(10);

            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            var customerA = SeedCustomer(db, tenantA);

            db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantA,
                CustomerId = customerA.Id,
                OrderNumber = "TEST-ORD-1",
                Description = "Test description",
                AppointmentDate = date,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            var available = await repo.IsTimeSlotAvailableAsync(
                date,
                durationMinutes: 30,
                tenantId: tenantB,
                excludeAppointmentId: null
            );

            available.Should().BeTrue();
        }
        [Fact]
        public async Task IsTimeSlotAvailableAsync_WhenExcludingSameAppointment_ShouldReturnTrue()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            var date = DateTime.Today.AddDays(1).AddHours(10);

            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            var existingId = Guid.NewGuid();

            db.Appointments.Add(new Appointment
            {
                Id = existingId,
                TenantId = tenantId,
                CustomerId = customer.Id,
                OrderNumber = "TEST-ORD-EXCLUDE",
                Description = "Test description",
                AppointmentDate = date,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            // Act: same slot, but exclude the existing appointment
            var available = await repo.IsTimeSlotAvailableAsync(
                date,
                durationMinutes: 30,
                tenantId: tenantId,
                excludeAppointmentId: existingId
            );

            available.Should().BeTrue();
        }

        [Fact]
        public async Task IsTimeSlotAvailableAsync_WhenAppointmentStartsExactlyAtEnd_ShouldReturnTrue()
        {
            var db = DbContextFactory.Create();
            var repo = new AppointmentRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);
            var customer = SeedCustomer(db, tenantId);

            // Existing appointment: 10:00
            var start = DateTime.Today.AddDays(1).AddHours(10);

            db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customer.Id,
                OrderNumber = "TEST-ORD-BOUNDARY",
                Description = "Test description",
                AppointmentDate = start,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync();

            // New appointment starts at 10:30 (end boundary for 30min slot)
            var newStart = start.AddMinutes(30);

            var available = await repo.IsTimeSlotAvailableAsync(
                newStart,
                durationMinutes: 30,
                tenantId: tenantId,
                excludeAppointmentId: null
            );

            available.Should().BeTrue();
        }

    }
}
