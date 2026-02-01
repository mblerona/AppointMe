using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AppointMe.Tests.Integration.Repositories
{
    public class CustomerRepositoryTests
    {
        // ----------------- seeding helpers -----------------

        private static Business SeedBusiness(ApplicationDbContext db, Guid tenantId)
        {
            var biz = new Business
            {
                Id = tenantId,
                Name = "Test Business",
                Address = "Skopje",
                EnableInvoices = true
            };

            db.Businesses.Add(biz);
            return biz;
        }

        private static Customer SeedCustomer(ApplicationDbContext db, Guid tenantId,
            string email = "ana@test.com",
            string first = "Ana",
            string last = "Doe",
            string phone = "070123456",
            string state = "North Macedonia",
            int customerNumber = 1)
        {
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                FirstName = first,
                LastName = last,
                Email = email,
                PhoneNumber = phone,
                State = state,
                City = "Skopje",
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
                Price = 100,
                IsActive = true,
                CategoryId = cat.Id
            };
            db.ServiceOfferings.Add(svc);

            return (cat, svc);
        }

        // ----------------- GetByEmailAsync -----------------

        [Fact]
        public async Task GetByEmailAsync_WhenExistsForTenant_ReturnsCustomer()
        {
            var db = DbContextFactory.Create();
            var repo = new CustomerRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var email = "exists@test.com";
            var customer = SeedCustomer(db, tenantId, email: email);

            await db.SaveChangesAsync();

            var found = await repo.GetByEmailAsync(email, tenantId);

            found.Should().NotBeNull();
            found!.Id.Should().Be(customer.Id);
            found.TenantId.Should().Be(tenantId);
        }

        [Fact]
        public async Task GetByEmailAsync_WhenEmailExistsButDifferentTenant_ReturnsNull()
        {
            var db = DbContextFactory.Create();
            var repo = new CustomerRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            var email = "same@test.com";
            SeedCustomer(db, tenantA, email: email);

            await db.SaveChangesAsync();

            var found = await repo.GetByEmailAsync(email, tenantB);

            found.Should().BeNull();
        }

        // ----------------- GetAllByTenantAsync -----------------

        [Fact]
        public async Task GetAllByTenantAsync_ReturnsOnlyTenantCustomers()
        {
            var db = DbContextFactory.Create();
            var repo = new CustomerRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            SeedCustomer(db, tenantA, email: "a1@test.com", customerNumber: 1);
            SeedCustomer(db, tenantA, email: "a2@test.com", customerNumber: 2);
            SeedCustomer(db, tenantB, email: "b1@test.com", customerNumber: 1);

            await db.SaveChangesAsync();

            var list = (await repo.GetAllByTenantAsync(tenantA)).ToList();

            list.Should().HaveCount(2);
            list.All(c => c.TenantId == tenantA).Should().BeTrue();
        }

        // ----------------- SearchAsync -----------------
        // NOTE: Search uses string.Contains (case sensitivity depends on provider).
        // With EF InMemory, it is case-sensitive, so we match exact casing.

        [Fact]
        public async Task SearchAsync_FiltersByTenant_AndMatchesAcrossFields()
        {
            var db = DbContextFactory.Create();
            var repo = new CustomerRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            // tenant A customers
            SeedCustomer(db, tenantA, first: "John", last: "Smith", email: "john@test.com", phone: "071000000", state: "MK", customerNumber: 1);
            SeedCustomer(db, tenantA, first: "Ana", last: "Doe", email: "ana@test.com", phone: "070123456", state: "North Macedonia", customerNumber: 2);

            // tenant B customer (should not appear)
            SeedCustomer(db, tenantB, first: "John", last: "Other", email: "john@tenantb.com", phone: "071999999", state: "MK", customerNumber: 1);

            await db.SaveChangesAsync();

            var results = (await repo.SearchAsync("John", tenantA)).ToList();

            results.Should().HaveCount(1);
            results[0].TenantId.Should().Be(tenantA);
            results[0].FirstName.Should().Be("John");
        }

        [Fact]
        public async Task SearchAsync_WhenNoMatches_ReturnsEmpty()
        {
            var db = DbContextFactory.Create();
            var repo = new CustomerRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            SeedCustomer(db, tenantId, first: "Ana", last: "Doe", email: "ana@test.com");

            await db.SaveChangesAsync();

            var results = (await repo.SearchAsync("NO_MATCH", tenantId)).ToList();

            results.Should().BeEmpty();
        }

        // ----------------- GetMaxCustomerNumberAsync -----------------

        [Fact]
        public async Task GetMaxCustomerNumberAsync_WhenNoCustomers_ReturnsZero()
        {
            var db = DbContextFactory.Create();
            var repo = new CustomerRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            await db.SaveChangesAsync();

            var max = await repo.GetMaxCustomerNumberAsync(tenantId);

            max.Should().Be(0);
        }

        [Fact]
        public async Task GetMaxCustomerNumberAsync_ReturnsHighestNumberForTenantOnly()
        {
            var db = DbContextFactory.Create();
            var repo = new CustomerRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            SeedCustomer(db, tenantA, email: "a1@test.com", customerNumber: 1);
            SeedCustomer(db, tenantA, email: "a9@test.com", customerNumber: 9);
            SeedCustomer(db, tenantB, email: "b99@test.com", customerNumber: 99);

            await db.SaveChangesAsync();

            var maxA = await repo.GetMaxCustomerNumberAsync(tenantA);

            maxA.Should().Be(9);
        }

        // ----------------- GetWithAppointmentsAsync (deep include) -----------------
        // This verifies your Include/ThenInclude chain is working:
        // Customer -> Appointments -> AppointmentServices -> ServiceOffering -> Category
        // as implemented in CustomerRepository.:contentReference[oaicite:2]{index=2}

        [Fact]
        public async Task GetWithAppointmentsAsync_IncludesAppointments_AndServiceOffering_AndCategory()
        {
            var db = DbContextFactory.Create();
            var repo = new CustomerRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var customer = SeedCustomer(db, tenantId, email: "profile@test.com");

            var (cat, svc) = SeedCategoryAndService(db, tenantId);

            var appointment = new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = customer.Id,
                OrderNumber = "ORD-1",
                Description = "Test appointment",
                AppointmentDate = DateTime.Today.AddDays(1).AddHours(10),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.Appointments.Add(appointment);

            db.AppointmentServices.Add(new AppointmentServiceModel
            {
                AppointmentId = appointment.Id,
                ServiceOfferingId = svc.Id,
                PriceAtBooking = svc.Price
            });

            await db.SaveChangesAsync();

            // detach to ensure we really load via Include (not from tracking)
            db.ChangeTracker.Clear();

            var loaded = await repo.GetWithAppointmentsAsync(customer.Id, tenantId);

            loaded.Should().NotBeNull();
            loaded!.Appointments.Should().NotBeNull();
            loaded.Appointments.Should().HaveCount(1);

            var appt = loaded.Appointments.First();
            appt.AppointmentServices.Should().HaveCount(1);

            var line = appt.AppointmentServices.First();
            line.ServiceOffering.Should().NotBeNull();
            line.ServiceOffering!.Name.Should().Be("Haircut");

            line.ServiceOffering.Category.Should().NotBeNull();
            line.ServiceOffering.Category!.Name.Should().Be("Hair");
        }

        [Fact]
        public async Task GetWithAppointmentsAsync_WhenCustomerNotInTenant_ReturnsNull()
        {
            var db = DbContextFactory.Create();
            var repo = new CustomerRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            var customerA = SeedCustomer(db, tenantA, email: "a@test.com");

            await db.SaveChangesAsync();

            var loaded = await repo.GetWithAppointmentsAsync(customerA.Id, tenantB);

            loaded.Should().BeNull();
        }
    }
}
