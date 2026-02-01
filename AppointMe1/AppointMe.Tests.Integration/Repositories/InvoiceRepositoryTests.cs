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
            string invoiceNumber,
            DateTime? issuedAt = null,
            int linesCount = 0)
        {
            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                AppointmentId = appointmentId,
                CustomerId = Guid.NewGuid(),

                InvoiceNumber = invoiceNumber,
                IssuedAt = issuedAt ?? DateTime.UtcNow,
                Status = InvoiceStatus.Draft,

                // required snapshot fields
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

            for (int i = 0; i < linesCount; i++)
            {
                invoice.Lines.Add(new InvoiceLine
                {
                    Id = Guid.NewGuid(),
                    InvoiceId = invoice.Id,
                    NameSnapshot = $"Service {i + 1}",
                    Qty = 1,
                    UnitPrice = 50,
                    LineTotal = 50
                });
            }

            return invoice;
        }

        [Fact]
        public async Task GetByIdAsync_WhenExistsForTenant_ReturnsInvoice_WithLines()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var inv = CreateInvoice(tenantId, Guid.NewGuid(), "INV-2026-0001", linesCount: 2);
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();

            var loaded = await repo.GetByIdAsync(inv.Id, tenantId);

            loaded.Should().NotBeNull();
            loaded!.Id.Should().Be(inv.Id);
            loaded.Lines.Should().HaveCount(2);
        }

        [Fact]
        public async Task GetByIdAsync_WhenWrongTenant_ReturnsNull()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            var inv = CreateInvoice(tenantA, Guid.NewGuid(), "INV-2026-0001", linesCount: 1);
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();

            var loaded = await repo.GetByIdAsync(inv.Id, tenantB);

            loaded.Should().BeNull();
        }

        [Fact]
        public async Task GetByIdAsync_WhenNotFound_ReturnsNull()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var loaded = await repo.GetByIdAsync(Guid.NewGuid(), tenantId);

            loaded.Should().BeNull();
        }

        [Fact]
        public async Task GetByAppointmentIdAsync_WhenExistsForTenant_ReturnsInvoice_WithLines()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var appointmentId = Guid.NewGuid();
            var inv = CreateInvoice(tenantId, appointmentId, "INV-2026-0002", linesCount: 1);
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();

            var loaded = await repo.GetByAppointmentIdAsync(appointmentId, tenantId);

            loaded.Should().NotBeNull();
            loaded!.AppointmentId.Should().Be(appointmentId);
            loaded.Lines.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetByAppointmentIdAsync_WhenWrongTenant_ReturnsNull()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            var appointmentId = Guid.NewGuid();
            var inv = CreateInvoice(tenantA, appointmentId, "INV-2026-0002", linesCount: 1);
            db.Invoices.Add(inv);
            await db.SaveChangesAsync();

            var loaded = await repo.GetByAppointmentIdAsync(appointmentId, tenantB);

            loaded.Should().BeNull();
        }

        [Fact]
        public async Task GetAllByTenantAsync_ReturnsOnlyTenantInvoices_InIssuedAtDescendingOrder_AndIncludesLines()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            var older = CreateInvoice(tenantA, Guid.NewGuid(), "INV-2026-0001", issuedAt: DateTime.UtcNow.AddDays(-5), linesCount: 1);
            var newer = CreateInvoice(tenantA, Guid.NewGuid(), "INV-2026-0002", issuedAt: DateTime.UtcNow.AddDays(-1), linesCount: 2);
            var otherTenant = CreateInvoice(tenantB, Guid.NewGuid(), "INV-2026-0003", issuedAt: DateTime.UtcNow, linesCount: 3);

            db.Invoices.AddRange(older, newer, otherTenant);
            await db.SaveChangesAsync();

            var result = (await repo.GetAllByTenantAsync(tenantA)).ToList();

            result.Should().HaveCount(2);
            result.All(x => x.TenantId == tenantA).Should().BeTrue();

            // ordering check (IssuedAt desc)
            result[0].InvoiceNumber.Should().Be("INV-2026-0002");
            result[1].InvoiceNumber.Should().Be("INV-2026-0001");

            // include lines check (Include(i => i.Lines))
            result[0].Lines.Should().HaveCount(2);
            result[1].Lines.Should().HaveCount(1);
        }

        [Fact]
        public async Task GetMaxInvoiceSequenceForYearAsync_WhenNoInvoices_ReturnsZero()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var year = DateTime.UtcNow.Year;

            var max = await repo.GetMaxInvoiceSequenceForYearAsync(tenantId, year);

            max.Should().Be(0);
        }

        [Fact]
        public async Task GetMaxInvoiceSequenceForYearAsync_IgnoresOtherYears()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var year = 2026;
            db.Invoices.Add(CreateInvoice(tenantId, Guid.NewGuid(), "INV-2025-0010"));
            db.Invoices.Add(CreateInvoice(tenantId, Guid.NewGuid(), "INV-2026-0003"));
            await db.SaveChangesAsync();

            var max = await repo.GetMaxInvoiceSequenceForYearAsync(tenantId, year);

            max.Should().Be(3);
        }

        [Fact]
        public async Task GetMaxInvoiceSequenceForYearAsync_IgnoresOtherTenants()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantA = Guid.NewGuid();
            var tenantB = Guid.NewGuid();
            SeedBusiness(db, tenantA);
            SeedBusiness(db, tenantB);

            var year = 2026;

            db.Invoices.Add(CreateInvoice(tenantA, Guid.NewGuid(), "INV-2026-0002"));
            db.Invoices.Add(CreateInvoice(tenantB, Guid.NewGuid(), "INV-2026-0099"));
            await db.SaveChangesAsync();

            var maxA = await repo.GetMaxInvoiceSequenceForYearAsync(tenantA, year);

            maxA.Should().Be(2);
        }

        [Fact]
        public async Task GetMaxInvoiceSequenceForYearAsync_WhenLastInvoiceNumberMalformed_ReturnsZero()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var year = 2026;

            // matches prefix but suffix isn't numeric -> TryParse fails -> expected 0
            db.Invoices.Add(CreateInvoice(tenantId, Guid.NewGuid(), "INV-2026-ABCD"));
            await db.SaveChangesAsync();

            var max = await repo.GetMaxInvoiceSequenceForYearAsync(tenantId, year);

            max.Should().Be(0);
        }

        [Fact]
        public async Task GetMaxInvoiceSequenceForYearAsync_ReturnsHighest_WhenNumbersAreZeroPadded()
        {
            var db = DbContextFactory.Create();
            var repo = new InvoiceRepository(db);

            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var year = 2026;

            db.Invoices.Add(CreateInvoice(tenantId, Guid.NewGuid(), "INV-2026-0001"));
            db.Invoices.Add(CreateInvoice(tenantId, Guid.NewGuid(), "INV-2026-0010"));
            db.Invoices.Add(CreateInvoice(tenantId, Guid.NewGuid(), "INV-2026-0009"));
            await db.SaveChangesAsync();

            var max = await repo.GetMaxInvoiceSequenceForYearAsync(tenantId, year);

            max.Should().Be(10);
        }
    }
}
