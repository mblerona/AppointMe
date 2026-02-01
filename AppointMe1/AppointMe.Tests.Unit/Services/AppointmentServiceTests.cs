using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using AppointMe.Service.Calendar;
using AppointMe.Service.Email;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;

namespace AppointMe.Tests.Unit.Services
{
    public class AppointmentServiceTests
    {
        private static ApplicationDbContext CreateDb()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"db-{Guid.NewGuid()}")
                .Options;

            return new ApplicationDbContext(opts);
        }

        private static Business SeedDefaultBusiness(ApplicationDbContext db, Guid tenantId)
        {
            var biz = new Business
            {
                Id = tenantId,
                Name = "Test Business",
                Address = "Skopje",
                DefaultSlotMinutes = 30,
                WorkDayStart = new TimeSpan(9, 0, 0),
                WorkDayEnd = new TimeSpan(17, 0, 0),
                OpenMon = true,
                OpenTue = true,
                OpenWed = true,
                OpenThu = true,
                OpenFri = true,
                OpenSat = false,
                OpenSun = false
            };

            db.Businesses.Add(biz);
            db.SaveChanges();
            return biz;
        }

        private static Customer MakeCustomer(Guid customerId, Guid tenantId, string email = "a@b.com")
        {
            return new Customer
            {
                Id = customerId,
                TenantId = tenantId,
                FirstName = "Ana",
                LastName = "Test",
                Email = email,
                PhoneNumber = "070000000",
                State = "MK",
                City = "Skopje",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private static DateTime NextWorkingDayAt(int hour, int minute)
        {
            // pick a date in the future that lands on a weekday
            var d = DateTime.Now.AddDays(1);
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                d = d.AddDays(1);

            return new DateTime(d.Year, d.Month, d.Day, hour, minute, 0);
        }

        private AppointmentService BuildService(
            ApplicationDbContext db,
            Mock<IAppointmentRepository>? apptRepo = null,
            Mock<ICustomerRepository>? custRepo = null,
            Mock<IServiceOfferingRepository>? svcRepo = null,
            Mock<IHolidayService>? holidaySvc = null,
            Mock<IEmailService>? emailSvc = null)
        {
            apptRepo ??= new Mock<IAppointmentRepository>(MockBehavior.Strict);
            custRepo ??= new Mock<ICustomerRepository>(MockBehavior.Strict);
            svcRepo ??= new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            holidaySvc ??= new Mock<IHolidayService>(MockBehavior.Strict);
            emailSvc ??= new Mock<IEmailService>(MockBehavior.Strict);

            return new AppointmentService(
                apptRepo.Object,
                custRepo.Object,
                svcRepo.Object,
                holidaySvc.Object,
                db,
                emailSvc.Object
            );
        }

        // -----------------------
        // CreateAppointmentAsync
        // -----------------------

        [Fact]
        public async Task CreateAppointmentAsync_Throws_WhenCustomerMissingOrWrongTenant()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var customerId = Guid.NewGuid();
            custRepo.Setup(r => r.GetByIdAsync(customerId)).ReturnsAsync((Customer?)null);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new CreateAppointmentDTO
            {
                CustomerId = customerId,
                OrderNumber = "ORD-1",
                AppointmentDate = NextWorkingDayAt(10, 0),
                Description = "desc",
                NotifyByEmail = false
            };

            Func<Task> act = async () => await service.CreateAppointmentAsync(dto, tenantId);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage($"*Customer with ID {customerId} not found*");
        }

        [Fact]
        public async Task CreateAppointmentAsync_Throws_WhenBusinessNotFound()
        {
            var db = CreateDb(); // no business seeded
            var tenantId = Guid.NewGuid();

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var customerId = Guid.NewGuid();
            custRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(MakeCustomer(customerId, tenantId));

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new CreateAppointmentDTO
            {
                CustomerId = customerId,
                OrderNumber = "ORD-1",
                AppointmentDate = NextWorkingDayAt(10, 0),
                Description = "desc"
            };

            Func<Task> act = async () => await service.CreateAppointmentAsync(dto, tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Business not found.");
        }

        [Fact]
        public async Task CreateAppointmentAsync_Throws_WhenDateIsNotFuture()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var customerId = Guid.NewGuid();
            custRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(MakeCustomer(customerId, tenantId));

            // holiday call will happen during validation
            holiday.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), "MK"))
                .ReturnsAsync(new List<HolidayDTO>());

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new CreateAppointmentDTO
            {
                CustomerId = customerId,
                OrderNumber = "ORD-1",
                AppointmentDate = DateTime.Now.AddMinutes(-5), // past
                Description = "desc"
            };

            Func<Task> act = async () => await service.CreateAppointmentAsync(dto, tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Appointment date/time must be in the future.");
        }

        [Fact]
        public async Task CreateAppointmentAsync_Throws_WhenBusinessClosedOnThatDay()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();

            var biz = SeedDefaultBusiness(db, tenantId);
            biz.OpenSun = false;

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var customerId = Guid.NewGuid();
            custRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(MakeCustomer(customerId, tenantId));

            holiday.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), "MK"))
                .ReturnsAsync(new List<HolidayDTO>());

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            // next Sunday in the future
            var d = DateTime.Now.AddDays(1);
            while (d.DayOfWeek != DayOfWeek.Sunday) d = d.AddDays(1);
            var sundayAt10 = new DateTime(d.Year, d.Month, d.Day, 10, 0, 0);

            var dto = new CreateAppointmentDTO
            {
                CustomerId = customerId,
                OrderNumber = "ORD-1",
                AppointmentDate = sundayAt10,
                Description = "desc"
            };

            Func<Task> act = async () => await service.CreateAppointmentAsync(dto, tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Business is closed on the selected day.");
        }

        [Fact]
        public async Task CreateAppointmentAsync_Throws_WhenOutsideWorkingHours()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            var biz = SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var customerId = Guid.NewGuid();
            custRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(MakeCustomer(customerId, tenantId));

            holiday.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), "MK"))
                .ReturnsAsync(new List<HolidayDTO>());

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dateAt8 = NextWorkingDayAt(8, 0); // before 09:00

            var dto = new CreateAppointmentDTO
            {
                CustomerId = customerId,
                OrderNumber = "ORD-1",
                AppointmentDate = dateAt8,
                Description = "desc"
            };

            Func<Task> act = async () => await service.CreateAppointmentAsync(dto, tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage($"Appointment must be between {biz.WorkDayStart:hh\\:mm} and {biz.WorkDayEnd:hh\\:mm}.");
        }

        [Fact]
        public async Task CreateAppointmentAsync_Throws_WhenPublicHoliday()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var customerId = Guid.NewGuid();
            custRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(MakeCustomer(customerId, tenantId));

            var holidayDate = NextWorkingDayAt(10, 0);
            holiday.Setup(h => h.GetHolidaysAsync(holidayDate.Year, "MK"))
                .ReturnsAsync(new List<HolidayDTO>
                {
                    new HolidayDTO { Date = holidayDate.Date, LocalName = "HolidayName", Name = "HolidayNameEN" }
                });

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new CreateAppointmentDTO
            {
                CustomerId = customerId,
                OrderNumber = "ORD-1",
                AppointmentDate = holidayDate,
                Description = "desc"
            };

            Func<Task> act = async () => await service.CreateAppointmentAsync(dto, tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Cannot create an appointment on a public holiday: HolidayName.");
        }

        [Fact]
        public async Task CreateAppointmentAsync_Throws_WhenTimeSlotOverlaps()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var customerId = Guid.NewGuid();
            custRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(MakeCustomer(customerId, tenantId));

            holiday.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), "MK"))
                .ReturnsAsync(new List<HolidayDTO>());

            apptRepo.Setup(r => r.IsTimeSlotAvailableAsync(It.IsAny<DateTime>(), It.IsAny<int>(), tenantId, null))
                .ReturnsAsync(false);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new CreateAppointmentDTO
            {
                CustomerId = customerId,
                OrderNumber = "ORD-1",
                AppointmentDate = NextWorkingDayAt(10, 0),
                Description = "desc"
            };

            Func<Task> act = async () => await service.CreateAppointmentAsync(dto, tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("The selected time overlaps with another appointment.");
        }

        [Fact]
        public async Task CreateAppointmentAsync_Throws_WhenOrderNumberAlreadyExists()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var customerId = Guid.NewGuid();
            custRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(MakeCustomer(customerId, tenantId));

            holiday.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), "MK"))
                .ReturnsAsync(new List<HolidayDTO>());

            apptRepo.Setup(r => r.IsTimeSlotAvailableAsync(It.IsAny<DateTime>(), It.IsAny<int>(), tenantId, null))
                .ReturnsAsync(true);

            apptRepo.Setup(r => r.OrderNumberExistsAsync("ORD-1", null))
                .ReturnsAsync(true);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new CreateAppointmentDTO
            {
                CustomerId = customerId,
                OrderNumber = "  ORD-1  ", // will be trimmed inside service
                AppointmentDate = NextWorkingDayAt(10, 0),
                Description = "desc"
            };

            Func<Task> act = async () => await service.CreateAppointmentAsync(dto, tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Order number already exists. Please use a different order number.");
        }

        [Fact]
        public async Task CreateAppointmentAsync_Throws_WhenSelectedServicesContainInvalidIds()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var customerId = Guid.NewGuid();
            custRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(MakeCustomer(customerId, tenantId));

            holiday.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), "MK"))
                .ReturnsAsync(new List<HolidayDTO>());

            apptRepo.Setup(r => r.IsTimeSlotAvailableAsync(It.IsAny<DateTime>(), It.IsAny<int>(), tenantId, null))
                .ReturnsAsync(true);

            apptRepo.Setup(r => r.OrderNumberExistsAsync("ORD-1", null))
                .ReturnsAsync(false);

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();
            // return only 1 service even though 2 were requested => invalid
            svcRepo.Setup(r => r.GetByIdsForBusinessAsync(
                    It.Is<List<Guid>>(l => l.Contains(id1) && l.Contains(id2)),
                    tenantId))
                .ReturnsAsync(new List<ServiceOffering>
                {
                    new ServiceOffering { Id = id1, BusinessId = tenantId, Name = "S1", Price = 100 }
                });

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new CreateAppointmentDTO
            {
                CustomerId = customerId,
                OrderNumber = "ORD-1",
                AppointmentDate = NextWorkingDayAt(10, 0),
                Description = "desc",
                ServiceOfferingIds = new List<Guid> { id1, id2 }
            };

            Func<Task> act = async () => await service.CreateAppointmentAsync(dto, tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("One or more selected services are invalid.");
        }

        [Fact]
        public async Task CreateAppointmentAsync_Success_CallsAddAndSave_AndDoesNotSendEmail_WhenNotifyFalse()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var customerId = Guid.NewGuid();
            custRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(MakeCustomer(customerId, tenantId, "x@y.com"));

            holiday.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), "MK"))
                .ReturnsAsync(new List<HolidayDTO>());

            apptRepo.Setup(r => r.IsTimeSlotAvailableAsync(It.IsAny<DateTime>(), It.IsAny<int>(), tenantId, null))
                .ReturnsAsync(true);

            apptRepo.Setup(r => r.OrderNumberExistsAsync("ORD-1", null))
                .ReturnsAsync(false);

            apptRepo.Setup(r => r.AddAsync(It.IsAny<Appointment>()))
                .Returns(Task.CompletedTask);

            apptRepo.Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new CreateAppointmentDTO
            {
                CustomerId = customerId,
                OrderNumber = "ORD-1",
                AppointmentDate = NextWorkingDayAt(10, 0),
                Description = "desc",
                NotifyByEmail = false
            };

            var result = await service.CreateAppointmentAsync(dto, tenantId);

            result.OrderNumber.Should().Be("ORD-1");
            result.CustomerId.Should().Be(customerId);
            result.Status.Should().Be(AppointmentStatus.Scheduled.ToString());

            apptRepo.Verify(r => r.AddAsync(It.IsAny<Appointment>()), Times.Once);
            apptRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
            email.Verify(e => e.SendEmailWithAttachmentAsync(
                It.IsAny<EmailMessage>(),
                It.IsAny<byte[]>(),
                It.IsAny<string>(),
                It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task CreateAppointmentAsync_WhenNotifyTrueAndEmailExists_AttemptsToSendEmail()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var customerId = Guid.NewGuid();
            custRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(MakeCustomer(customerId, tenantId, "customer@test.com"));

            holiday.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), "MK"))
                .ReturnsAsync(new List<HolidayDTO>());

            apptRepo.Setup(r => r.IsTimeSlotAvailableAsync(It.IsAny<DateTime>(), It.IsAny<int>(), tenantId, null))
                .ReturnsAsync(true);

            apptRepo.Setup(r => r.OrderNumberExistsAsync("ORD-1", null))
                .ReturnsAsync(false);

            apptRepo.Setup(r => r.AddAsync(It.IsAny<Appointment>()))
                .Returns(Task.CompletedTask);

            apptRepo.Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            email.Setup(e => e.SendEmailWithAttachmentAsync(
                    It.Is<EmailMessage>(m =>
                        m.MailTo == "customer@test.com" &&
                        m.Subject.Contains("appointment")),
                    It.IsAny<byte[]>(),
                    "appointment.ics",
                    "text/calendar"))
                .Returns(Task.CompletedTask);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new CreateAppointmentDTO
            {
                CustomerId = customerId,
                OrderNumber = "ORD-1",
                AppointmentDate = NextWorkingDayAt(10, 0),
                Description = "desc",
                NotifyByEmail = true
            };

            await service.CreateAppointmentAsync(dto, tenantId);

            email.VerifyAll();
        }

        // -----------------------
        // UpdateAppointmentAsync
        // -----------------------

        [Fact]
        public async Task UpdateAppointmentAsync_Throws_WhenAppointmentNotFound()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var apptId = Guid.NewGuid();
            apptRepo.Setup(r => r.GetWithCustomerAsync(apptId, tenantId))
                .ReturnsAsync((Appointment?)null);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new UpdateAppointmentDTO
            {
                AppointmentDate = NextWorkingDayAt(11, 0),
                OrderNumber = "ORD-2",
                Description = "new",
                Status = AppointmentStatus.Completed
            };

            Func<Task> act = async () => await service.UpdateAppointmentAsync(apptId, dto, tenantId);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage($"*Appointment with ID {apptId} not found*");
        }

        [Fact]
        public async Task UpdateAppointmentAsync_Throws_WhenOrderNumberMissing()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var apptId = Guid.NewGuid();
            var appt = new Appointment
            {
                Id = apptId,
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                OrderNumber = "ORD-1",
                AppointmentDate = NextWorkingDayAt(10, 0),
                Status = AppointmentStatus.Scheduled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            apptRepo.Setup(r => r.GetWithCustomerAsync(apptId, tenantId))
                .ReturnsAsync(appt);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new UpdateAppointmentDTO
            {
                OrderNumber = "   ", // required
                Status = AppointmentStatus.Scheduled
            };

            Func<Task> act = async () => await service.UpdateAppointmentAsync(apptId, dto, tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Order number is required.");
        }

        [Fact]
        public async Task UpdateAppointmentAsync_Throws_WhenNewOrderNumberAlreadyExists()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var apptId = Guid.NewGuid();
            var appt = new Appointment
            {
                Id = apptId,
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                OrderNumber = "ORD-1",
                AppointmentDate = NextWorkingDayAt(10, 0),
                Status = AppointmentStatus.Scheduled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            apptRepo.Setup(r => r.GetWithCustomerAsync(apptId, tenantId))
                .ReturnsAsync(appt);

            apptRepo.Setup(r => r.OrderNumberExistsAsync("ORD-NEW", apptId))
                .ReturnsAsync(true);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new UpdateAppointmentDTO
            {
                OrderNumber = "ORD-NEW",
                Status = AppointmentStatus.Scheduled
            };

            Func<Task> act = async () => await service.UpdateAppointmentAsync(apptId, dto, tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("Order number already exists. Please use a different order number.");
        }

        [Fact]
        public async Task UpdateAppointmentAsync_WhenChangingDate_CallsAvailabilityWithExcludeId()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var apptId = Guid.NewGuid();
            var oldDate = NextWorkingDayAt(10, 0);

            var appt = new Appointment
            {
                Id = apptId,
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                OrderNumber = "ORD-1",
                AppointmentDate = oldDate,
                Status = AppointmentStatus.Scheduled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            apptRepo.Setup(r => r.GetWithCustomerAsync(apptId, tenantId))
                .ReturnsAsync(appt);

            holiday.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), "MK"))
                .ReturnsAsync(new List<HolidayDTO>());

            var newDate = NextWorkingDayAt(11, 0);

            apptRepo.Setup(r => r.IsTimeSlotAvailableAsync(newDate, It.IsAny<int>(), tenantId, apptId))
                .ReturnsAsync(true);

            apptRepo.Setup(r => r.UpdateAsync(appt)).Returns(Task.CompletedTask);
            apptRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new UpdateAppointmentDTO
            {
                AppointmentDate = newDate,
                OrderNumber = "ORD-1",
                Status = AppointmentStatus.Completed
            };

            var result = await service.UpdateAppointmentAsync(apptId, dto, tenantId);

            appt.AppointmentDate.Should().Be(newDate);
            appt.Status.Should().Be(AppointmentStatus.Completed);

            apptRepo.Verify(r => r.IsTimeSlotAvailableAsync(newDate, It.IsAny<int>(), tenantId, apptId), Times.Once);
            apptRepo.Verify(r => r.UpdateAsync(appt), Times.Once);
            apptRepo.Verify(r => r.SaveChangesAsync(), Times.Once);

            result.Status.Should().Be(AppointmentStatus.Completed.ToString());
        }

        [Fact]
        public async Task UpdateAppointmentAsync_Throws_WhenSelectedServicesInvalid()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var apptId = Guid.NewGuid();
            var appt = new Appointment
            {
                Id = apptId,
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                OrderNumber = "ORD-1",
                AppointmentDate = NextWorkingDayAt(10, 0),
                Status = AppointmentStatus.Scheduled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            apptRepo.Setup(r => r.GetWithCustomerAsync(apptId, tenantId))
                .ReturnsAsync(appt);

            var id1 = Guid.NewGuid();
            var id2 = Guid.NewGuid();

            svcRepo.Setup(r => r.GetByIdsForBusinessAsync(
                    It.Is<List<Guid>>(l => l.Contains(id1) && l.Contains(id2)),
                    tenantId))
                .ReturnsAsync(new List<ServiceOffering>
                {
                    new ServiceOffering { Id = id1, BusinessId = tenantId, Name = "S1", Price = 100 }
                });

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new UpdateAppointmentDTO
            {
                OrderNumber = "ORD-1",
                Status = AppointmentStatus.Scheduled,
                ServiceOfferingIds = new List<Guid> { id1, id2 }
            };

            Func<Task> act = async () => await service.UpdateAppointmentAsync(apptId, dto, tenantId);

            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("One or more selected services are invalid.");
        }

        [Fact]
        public async Task UpdateAppointmentAsync_Success_ReplacesServiceLines_AndSaves()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var apptId = Guid.NewGuid();

            var appt = new Appointment
            {
                Id = apptId,
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                OrderNumber = "ORD-1",
                AppointmentDate = NextWorkingDayAt(10, 0),
                Status = AppointmentStatus.Scheduled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                AppointmentServices = new List<AppointmentServiceModel>
                {
                    new AppointmentServiceModel{ AppointmentId = apptId, ServiceOfferingId = Guid.NewGuid(), PriceAtBooking = 50 }
                }
            };

            apptRepo.Setup(r => r.GetWithCustomerAsync(apptId, tenantId))
                .ReturnsAsync(appt);

            // order unchanged => no call to OrderNumberExistsAsync
            var s1 = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = tenantId, Name = "S1", Price = 100 };
            var s2 = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = tenantId, Name = "S2", Price = 200 };

            svcRepo.Setup(r => r.GetByIdsForBusinessAsync(
                    It.Is<List<Guid>>(l => l.Count == 2 && l.Contains(s1.Id) && l.Contains(s2.Id)),
                    tenantId))
                .ReturnsAsync(new List<ServiceOffering> { s1, s2 });

            apptRepo.Setup(r => r.UpdateAsync(appt)).Returns(Task.CompletedTask);
            apptRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            var dto = new UpdateAppointmentDTO
            {
                OrderNumber = "ORD-1",
                Status = AppointmentStatus.Completed,
                Description = "updated",
                ServiceOfferingIds = new List<Guid> { s1.Id, s2.Id }
            };

            var result = await service.UpdateAppointmentAsync(apptId, dto, tenantId);

            appt.AppointmentServices.Should().HaveCount(2);
            appt.AppointmentServices.Select(x => x.ServiceOfferingId).Should().BeEquivalentTo(new[] { s1.Id, s2.Id });
            appt.AppointmentServices.First(x => x.ServiceOfferingId == s1.Id).PriceAtBooking.Should().Be(100);
            appt.AppointmentServices.First(x => x.ServiceOfferingId == s2.Id).PriceAtBooking.Should().Be(200);

            apptRepo.Verify(r => r.UpdateAsync(appt), Times.Once);
            apptRepo.Verify(r => r.SaveChangesAsync(), Times.Once);

            result.Services.Should().HaveCount(2);
            result.TotalPrice.Should().Be(300m);
        }

        // -----------------------
        // SetStatusAsync / Delete
        // -----------------------

        [Fact]
        public async Task SetStatusAsync_Throws_WhenAppointmentNotFound()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            apptRepo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), tenantId))
                .ReturnsAsync((Appointment?)null);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            Func<Task> act = async () => await service.SetStatusAsync(Guid.NewGuid(), AppointmentStatus.NoShow, tenantId);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage("Appointment not found.");
        }

        [Fact]
        public async Task SetStatusAsync_UpdatesAndSaves()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var apptId = Guid.NewGuid();
            var appt = new Appointment
            {
                Id = apptId,
                TenantId = tenantId,
                CustomerId = Guid.NewGuid(),
                OrderNumber = "ORD-1",
                AppointmentDate = NextWorkingDayAt(10, 0),
                Status = AppointmentStatus.Scheduled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            apptRepo.Setup(r => r.GetByIdAsync(apptId, tenantId))
                .ReturnsAsync(appt);

            apptRepo.Setup(r => r.UpdateAsync(appt)).Returns(Task.CompletedTask);
            apptRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            await service.SetStatusAsync(apptId, AppointmentStatus.Completed, tenantId);

            appt.Status.Should().Be(AppointmentStatus.Completed);
            apptRepo.Verify(r => r.UpdateAsync(appt), Times.Once);
            apptRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task DeleteAppointmentAsync_Throws_WhenNotFound()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var apptId = Guid.NewGuid();
            apptRepo.Setup(r => r.GetByIdAsync(apptId, tenantId))
                .ReturnsAsync((Appointment?)null);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            Func<Task> act = async () => await service.DeleteAppointmentAsync(apptId, tenantId);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage($"*Appointment with ID {apptId} not found*");
        }

        [Fact]
        public async Task DeleteAppointmentAsync_DeletesAndSaves()
        {
            var db = CreateDb();
            var tenantId = Guid.NewGuid();
            SeedDefaultBusiness(db, tenantId);

            var apptRepo = new Mock<IAppointmentRepository>(MockBehavior.Strict);
            var custRepo = new Mock<ICustomerRepository>(MockBehavior.Strict);
            var svcRepo = new Mock<IServiceOfferingRepository>(MockBehavior.Strict);
            var holiday = new Mock<IHolidayService>(MockBehavior.Strict);
            var email = new Mock<IEmailService>(MockBehavior.Strict);

            var apptId = Guid.NewGuid();
            var appt = new Appointment { Id = apptId, TenantId = tenantId };

            apptRepo.Setup(r => r.GetByIdAsync(apptId, tenantId))
                .ReturnsAsync(appt);

            apptRepo.Setup(r => r.DeleteAsync(appt)).Returns(Task.CompletedTask);
            apptRepo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);

            var service = BuildService(db, apptRepo, custRepo, svcRepo, holiday, email);

            await service.DeleteAppointmentAsync(apptId, tenantId);

            apptRepo.Verify(r => r.DeleteAsync(appt), Times.Once);
            apptRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }
    }
}
