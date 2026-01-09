using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using FluentAssertions;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AppointMe.Tests.Integration.Repositories
{
    public class InvoiceRepositoryTests
    {
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

        private static Invoice CreateInvoice(
    Guid tenantId,
    Guid appointmentId,
    string invoiceNumber)
        {
            return new Invoice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AppointmentId = appointmentId,
                CustomerId = Guid.NewGuid(),

                InvoiceNumber = invoiceNumber,
                IssuedAt = DateTime.UtcNow,
                Status = InvoiceStatus.Draft,

                // REQUIRED snapshot fields
                BusinessNameSnapshot = "Test Business",
                BusinessAddressSnapshot = "Skopje",
                CustomerNameSnapshot = "Ana Doe",
                CustomerEmailSnapshot = "ana@test.com",
                AppointmentOrderNumberSnapshot = "ORD-1",
                AppointmentDateSnapshot = DateTime.UtcNow.AddDays(1),

                Subtotal = 100,
                Discount = 0,
                Tax = 0,
                Total = 100,

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        [Fact]
        public async Task GetMaxInvoiceSequenceForYearAsync_ShouldReturnHighestSequence()
        {
            // Arrange
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var year = DateTime.UtcNow.Year;

            db.Invoices.Add(CreateInvoice(tenantId, Guid.NewGuid(), $"INV-{year}-0001"));
            db.Invoices.Add(CreateInvoice(tenantId, Guid.NewGuid(), $"INV-{year}-0005"));
            db.Invoices.Add(CreateInvoice(tenantId, Guid.NewGuid(), $"INV-{year}-0003"));

            await db.SaveChangesAsync();

            // Act
            var max = await repo.GetMaxInvoiceSequenceForYearAsync(tenantId, year);

            // Assert
            max.Should().Be(5);
        }

        [Fact]
        public async Task GetByAppointmentIdAsync_ShouldIncludeInvoiceLines()
        {
            // Arrange
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var appointmentId = Guid.NewGuid();

            var invoice = CreateInvoice(tenantId, appointmentId, "INV-2025-0001");
            invoice.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                NameSnapshot = "Service A",
                Qty = 1,
                UnitPrice = 50,
                LineTotal = 50
            });

            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();

            // Act
            var result = await repo.GetByAppointmentIdAsync(appointmentId, tenantId);

            // Assert
            result.Should().NotBeNull();
            result!.Lines.Should().HaveCount(1);
            result.Lines.First().NameSnapshot.Should().Be("Service A");
        }

        [Fact]
        public async Task GetAllByTenantAsync_ShouldReturnOnlyTenantInvoices()
        {
            // Arrange
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();

            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            db.Invoices.Add(CreateInvoice(tenantA, Guid.NewGuid(), "INV-A-1"));
            db.Invoices.Add(CreateInvoice(tenantA, Guid.NewGuid(), "INV-A-2"));
            db.Invoices.Add(CreateInvoice(tenantB, Guid.NewGuid(), "INV-B-1"));

            await db.SaveChangesAsync();

            // Act
            var result = await repo.GetAllByTenantAsync(tenantA);

            // Assert
            result.Should().HaveCount(2);
            result.All(i => i.TenantId == tenantA).Should().BeTrue();
        }
        [Fact]
        public async Task GetByIdAsync_ShouldIncludeInvoiceLines()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var invoice = CreateInvoice(tenantId, Guid.NewGuid(), "INV-X-1");
            invoice.Lines.Add(new InvoiceLine
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                NameSnapshot = "Line 1",
                Qty = 1,
                UnitPrice = 10,
                LineTotal = 10
            });

            db.Invoices.Add(invoice);
            await db.SaveChangesAsync();

            var loaded = await repo.GetByIdAsync(invoice.Id, tenantId);

            loaded.Should().NotBeNull();
            loaded!.Lines.Should().HaveCount(1);
            loaded.Lines.First().NameSnapshot.Should().Be("Line 1");
        }

        [Fact]
        public async Task GetMaxInvoiceSequenceForYearAsync_WhenNoInvoices_ShouldReturnZero()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var year = DateTime.UtcNow.Year;

            var max = await repo.GetMaxInvoiceSequenceForYearAsync(tenantId, year);

            max.Should().Be(0);
        }
    }
}
