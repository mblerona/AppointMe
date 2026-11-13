using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Repository.Interface;
using AppointMe.Domain.Exceptions;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using AppointMe.Service.Pdf;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services
{
    public class InvoiceServiceTests
    {
        // -------------------------
        // Helpers
        // -------------------------

        private static ApplicationDbContext CreateDb()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"invoice-db-{Guid.NewGuid()}")
                .Options;

            return new ApplicationDbContext(options);
        }

        private static Business SeedBusiness(ApplicationDbContext db, Guid tenantId, bool enableInvoices = true, bool enableServices = true)
        {
            var biz = new Business
            {
                Id = tenantId,
                Name = "Test Business",
                Address = "Skopje",
                EnableInvoices = enableInvoices,
                EnableServices = enableServices
            };

            db.Businesses.Add(biz);
            db.SaveChanges();
            return biz;
        }

        private static Appointment BuildAppointment(
            Guid appointmentId,
            Guid tenantId,
            bool withServices = true)
        {
            var appt = new Appointment
            {
                Id = appointmentId,
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                OrderNumber = "ORD-1",
                AppointmentDate = DateTime.UtcNow.AddDays(1),
                Customer = new Customer
                {
                    FirstName = "Ana",
                    LastName = "Doe",
                    Email = "ana@test.com"
                },
                AppointmentServices = new List<AppointmentServiceModel>()
            };

            if (withServices)
            {
                appt.AppointmentServices.Add(new AppointmentServiceModel
                {
                    PriceAtBooking = 100,
                    ServiceOffering = new ServiceOffering
                    {
                        Name = "Haircut",
                       
                    }
                });

                appt.AppointmentServices.Add(new AppointmentServiceModel
                {
                    PriceAtBooking = 50,
                    ServiceOffering = new ServiceOffering
                    {
                        Name = "Wash",
                     
                    }
                });
            }

            return appt;
        }

        private InvoiceService BuildService(
            ApplicationDbContext db,
            Mock<IInvoiceRepository> invoiceRepo,
            Mock<IAppointmentRepository> apptRepo,
            Mock<ILogoStorage>? logoStorage = null,
            Mock<IInvoicePdfGenerator>? pdfGenerator = null)
        {
            return new InvoiceService(
                invoiceRepo.Object,
                apptRepo.Object,
                new BusinessRepository(db),
                TimeProvider.System,
                (logoStorage ?? new Mock<ILogoStorage>(MockBehavior.Strict)).Object,
                (pdfGenerator ?? new Mock<IInvoicePdfGenerator>(MockBehavior.Strict)).Object
            );
        }

        // -------------------------
        // Tests
        // -------------------------

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_BusinessNotFound_ShouldThrow()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();

            var invoiceRepo = new Mock<IInvoiceRepository>();
            var apptRepo = new Mock<IAppointmentRepository>();

            var service = BuildService(db, invoiceRepo, apptRepo);

            Func<Task> act = async () =>
                await service.CreateOrGetForAppointmentAsync(Guid.NewGuid(), tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Business not found.");
        }

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_InvoicesDisabled_ShouldThrow()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId, enableInvoices: false);

            var invoiceRepo = new Mock<IInvoiceRepository>();
            var apptRepo = new Mock<IAppointmentRepository>();

            var service = BuildService(db, invoiceRepo, apptRepo);

            Func<Task> act = async () =>
                await service.CreateOrGetForAppointmentAsync(Guid.NewGuid(), tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Invoices are disabled in settings.");
        }

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_ExistingInvoice_ShouldReturnIt()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var invoice = new Invoice
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                InvoiceNumber = "INV-2025-0001",
                Lines = new List<InvoiceLine>()
            };

            var invoiceRepo = new Mock<IInvoiceRepository>();
            var apptRepo = new Mock<IAppointmentRepository>();

            invoiceRepo.Setup(r => r.GetByAppointmentIdAsync(It.IsAny<Guid>(), tenantId))
                .ReturnsAsync(invoice);

            var service = BuildService(db, invoiceRepo, apptRepo);

            var result = await service.CreateOrGetForAppointmentAsync(Guid.NewGuid(), tenantId);

            result.InvoiceNumber.Should().Be("INV-2025-0001");
            invoiceRepo.Verify(r => r.AddAsync(It.IsAny<Invoice>()), Times.Never);
        }

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_AppointmentNotFound_ShouldThrow()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var invoiceRepo = new Mock<IInvoiceRepository>();
            var apptRepo = new Mock<IAppointmentRepository>();

            invoiceRepo.Setup(r => r.GetByAppointmentIdAsync(It.IsAny<Guid>(), tenantId))
                .ReturnsAsync((Invoice)null!);

            apptRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), tenantId))
                .ReturnsAsync((Appointment)null!);

            var service = BuildService(db, invoiceRepo, apptRepo);

            Func<Task> act = async () =>
                await service.CreateOrGetForAppointmentAsync(Guid.NewGuid(), tenantId);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage("Appointment not found.");
        }

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_NoServices_IsRefused_AndCreatesNothing()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var appt = BuildAppointment(Guid.NewGuid(), tenantId, withServices: false);

            var invoiceRepo = new Mock<IInvoiceRepository>();
            var apptRepo = new Mock<IAppointmentRepository>();

            invoiceRepo.Setup(r => r.GetByAppointmentIdAsync(appt.Id, tenantId))
                .ReturnsAsync((Invoice)null!);

            apptRepo.Setup(r => r.GetByIdAsync(appt.Id, tenantId))
                .ReturnsAsync(appt);

            var service = BuildService(db, invoiceRepo, apptRepo);

            var act = () => service.CreateOrGetForAppointmentAsync(appt.Id, tenantId);

            await act.Should().ThrowExactlyAsync<BusinessRuleException>()
                .WithMessage(InvoiceService.NoServicesMessage);
            invoiceRepo.Verify(r => r.AddAsync(It.IsAny<Invoice>()), Times.Never);
            invoiceRepo.Verify(r => r.SaveChangesAsync(), Times.Never);
        }

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_ExistingInvoice_IsReturned_EvenIfTheServicesWereRemovedSince()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var appointmentId = Guid.NewGuid();
            var existing = new Invoice { Id = Guid.NewGuid(), TenantId = tenantId, AppointmentId = appointmentId, InvoiceNumber = "INV-2026-0001" };

            var invoiceRepo = new Mock<IInvoiceRepository>();
            invoiceRepo.Setup(r => r.GetByAppointmentIdAsync(appointmentId, tenantId)).ReturnsAsync(existing);
            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict); // never loaded

            var result = await BuildService(db, invoiceRepo, apptRepo).CreateOrGetForAppointmentAsync(appointmentId, tenantId);

            result.Id.Should().Be(existing.Id);
        }

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_ServicesOff_IsRefused_EvenIfInvoicesAreStoredAsOn()
        {
            // "Invoices require services" is applied when reading, so the stored choice can stay on.
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId, enableInvoices: true, enableServices: false);

            var invoiceRepo = new Mock<IInvoiceRepository>(MockBehavior.Strict);
            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);

            var act = () => BuildService(db, invoiceRepo, apptRepo).CreateOrGetForAppointmentAsync(Guid.NewGuid(), tenantId);

            await act.Should().ThrowExactlyAsync<BusinessRuleException>()
                .WithMessage("Invoices are disabled in settings.");
        }

        // -------------------------
        // PDF
        // -------------------------

        private static Invoice StoredInvoice(Guid tenantId, string? logoUrl) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            InvoiceNumber = "INV-2026-0001",
            BusinessNameSnapshot = "Test Business",
            CustomerNameSnapshot = "Ana Doe",
            BusinessLogoSnapshot = logoUrl,
            IssuedAt = DateTime.UtcNow
        };

        [Fact]
        public async Task GeneratePdfAsync_PassesTheUploadedLogosBytesToTheGenerator()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var invoice = StoredInvoice(tenantId, "/uploads/logos/logo.png");
            var invoiceRepo = new Mock<IInvoiceRepository>();
            invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, tenantId)).ReturnsAsync(invoice);

            var logoBytes = new byte[] { 1, 2, 3, 4 };
            var logos = new Mock<ILogoStorage>(MockBehavior.Strict);
            logos.Setup(l => l.ReadAsync("/uploads/logos/logo.png")).ReturnsAsync(logoBytes);

            var pdf = new Mock<IInvoicePdfGenerator>(MockBehavior.Strict);
            var pdfBytes = new byte[] { 9, 9 };
            pdf.Setup(g => g.Generate(It.Is<InvoiceDTO>(i => i.Id == invoice.Id), It.IsAny<DateTime>(), logoBytes))
                .Returns(pdfBytes);

            var result = await BuildService(db, invoiceRepo, new Mock<IAppointmentRepository>(), logos, pdf)
                .GeneratePdfAsync(invoice.Id, tenantId);

            result.Should().BeSameAs(pdfBytes);
            pdf.Verify(g => g.Generate(It.IsAny<InvoiceDTO>(), It.IsAny<DateTime>(), logoBytes), Times.Once);
        }

        [Fact]
        public async Task GeneratePdfAsync_WithoutALogo_DoesNotReadStorage_AndPassesNull()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var invoice = StoredInvoice(tenantId, logoUrl: null);
            var invoiceRepo = new Mock<IInvoiceRepository>();
            invoiceRepo.Setup(r => r.GetByIdAsync(invoice.Id, tenantId)).ReturnsAsync(invoice);

            var pdf = new Mock<IInvoicePdfGenerator>(MockBehavior.Strict);
            pdf.Setup(g => g.Generate(It.IsAny<InvoiceDTO>(), It.IsAny<DateTime>(), null)).Returns(new byte[] { 1 });

            // A strict storage mock: any read would throw.
            await BuildService(db, invoiceRepo, new Mock<IAppointmentRepository>(), new Mock<ILogoStorage>(MockBehavior.Strict), pdf)
                .GeneratePdfAsync(invoice.Id, tenantId);

            pdf.Verify(g => g.Generate(It.IsAny<InvoiceDTO>(), It.IsAny<DateTime>(), null), Times.Once);
        }

        [Fact]
        public async Task GeneratePdfAsync_AnotherTenantsInvoice_IsNotFound()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var invoiceRepo = new Mock<IInvoiceRepository>();
            invoiceRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), tenantId)).ReturnsAsync((Invoice?)null);

            var act = () => BuildService(db, invoiceRepo, new Mock<IAppointmentRepository>()).GeneratePdfAsync(Guid.NewGuid(), tenantId);

            await act.Should().ThrowAsync<KeyNotFoundException>();
        }

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_WithServices_ShouldUsePriceAtBooking_AndCalculateTotal()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var appt = BuildAppointment(Guid.NewGuid(), tenantId, withServices: true);

            var invoiceRepo = new Mock<IInvoiceRepository>();
            var apptRepo = new Mock<IAppointmentRepository>();

            invoiceRepo.Setup(r => r.GetByAppointmentIdAsync(appt.Id, tenantId))
                .ReturnsAsync((Invoice)null!);

            invoiceRepo.Setup(r => r.GetMaxInvoiceSequenceForYearAsync(tenantId, It.IsAny<int>()))
                .ReturnsAsync(0);

            invoiceRepo.Setup(r => r.AddAsync(It.IsAny<Invoice>()))
                .Returns(Task.CompletedTask);

            invoiceRepo.Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            apptRepo.Setup(r => r.GetByIdAsync(appt.Id, tenantId))
                .ReturnsAsync(appt);

            var service = BuildService(db, invoiceRepo, apptRepo);

            var result = await service.CreateOrGetForAppointmentAsync(appt.Id, tenantId);

            result.Lines.Should().HaveCount(2);
            result.Subtotal.Should().Be(150);
            result.Total.Should().Be(150);
        }

        [Fact]
        public async Task CreateOrGetForAppointmentAsync_ShouldRetryOnDbUpdateException()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var appt = BuildAppointment(Guid.NewGuid(), tenantId);

            var invoiceRepo = new Mock<IInvoiceRepository>();
            var apptRepo = new Mock<IAppointmentRepository>();

            invoiceRepo.Setup(r => r.GetByAppointmentIdAsync(appt.Id, tenantId))
                .ReturnsAsync((Invoice)null!);

            invoiceRepo.Setup(r => r.GetMaxInvoiceSequenceForYearAsync(tenantId, It.IsAny<int>()))
                .ReturnsAsync(0);

            invoiceRepo.SetupSequence(r => r.AddAsync(It.IsAny<Invoice>()))
                .ThrowsAsync(new DbUpdateException())
                .Returns(Task.CompletedTask);

            invoiceRepo.Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            apptRepo.Setup(r => r.GetByIdAsync(appt.Id, tenantId))
                .ReturnsAsync(appt);

            var service = BuildService(db, invoiceRepo, apptRepo);

            var result = await service.CreateOrGetForAppointmentAsync(appt.Id, tenantId);

            result.InvoiceNumber.Should().StartWith("INV-");
            invoiceRepo.Verify(r => r.AddAsync(It.IsAny<Invoice>()), Times.Exactly(2));
        }
    }
}
