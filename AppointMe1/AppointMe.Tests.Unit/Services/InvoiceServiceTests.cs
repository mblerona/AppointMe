using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using AppointMe.Service.Implementation;
using FluentAssertions;
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

        private static Business SeedBusiness(ApplicationDbContext db, Guid tenantId, bool enableInvoices = true)
        {
            var biz = new Business
            {
                Id = tenantId,
                Name = "Test Business",
                Address = "Skopje",
                EnableInvoices = enableInvoices
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
            Mock<IAppointmentRepository> apptRepo)
        {
            return new InvoiceService(
                invoiceRepo.Object,
                apptRepo.Object,
                db
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
        public async Task CreateOrGetForAppointmentAsync_NoServices_ShouldCreateZeroTotalInvoice()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedBusiness(db, tenantId);

            var appt = BuildAppointment(Guid.NewGuid(), tenantId, withServices: false);

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

            result.Lines.Should().BeEmpty();
            result.Total.Should().Be(0);
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
