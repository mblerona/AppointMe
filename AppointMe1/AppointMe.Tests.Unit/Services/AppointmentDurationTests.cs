using System.Text;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Email;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

/// <summary>
/// Audit B11: an appointment keeps the length it was booked with, that length must fit before closing
/// time, and a cancelled appointment can't come back onto a taken time. Uses the real repositories,
/// because the behaviour under test is how the service and the overlap query work together.
/// </summary>
public class AppointmentDurationTests
{
    private readonly ApplicationDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Mock<IEmailService> _email = new();
    private readonly AppointmentService _service;

    public AppointmentDurationTests()
    {
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"duration-{Guid.NewGuid()}")
            .Options);

        _db.Businesses.Add(new Business
        {
            Id = _tenantId,
            Name = "Test Business",
            DefaultSlotMinutes = 30,
            WorkDayStart = new TimeSpan(9, 0, 0),
            WorkDayEnd = new TimeSpan(17, 0, 0),
            OpenMon = true, OpenTue = true, OpenWed = true, OpenThu = true, OpenFri = true, OpenSat = true, OpenSun = true
        });
        _db.Customers.Add(new Customer
        {
            Id = _customerId, TenantId = _tenantId, FirstName = "Ana", LastName = "Test", Email = "ana@test.com",
            PhoneNumber = "+38970123456", State = "MK", City = "Skopje"
        });
        _db.SaveChanges();

        var holidays = new Mock<IHolidayService>();
        holidays.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), "MK")).ReturnsAsync(new List<HolidayDTO>());

        _service = new AppointmentService(
            new AppointmentRepository(_db),
            new CustomerRepository(_db),
            new ServiceOfferingRepository(_db),
            holidays.Object,
            new BusinessRepository(_db),
            _email.Object,
            NullLogger<AppointmentService>.Instance);
    }

    private static DateTime Tomorrow(int hour, int minute = 0) => DateTime.Today.AddDays(1).AddHours(hour).AddMinutes(minute);

    private async Task SetSlotLengthAsync(int minutes)
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == _tenantId);
        business.DefaultSlotMinutes = minutes;
        await _db.SaveChangesAsync();
        _db.Entry(business).State = EntityState.Detached;
    }

    private Task<AppointmentDTO> BookAsync(DateTime when, string orderNumber, bool notify = false) =>
        _service.CreateAppointmentAsync(new CreateAppointmentDTO
        {
            CustomerId = _customerId,
            OrderNumber = orderNumber,
            AppointmentDate = when,
            Description = "Duration test",
            NotifyByEmail = notify
        }, _tenantId);

    private Task<AppointmentDTO> MoveAsync(Guid id, DateTime when, string orderNumber) =>
        _service.UpdateAppointmentAsync(id, new UpdateAppointmentDTO
        {
            AppointmentDate = when,
            OrderNumber = orderNumber,
            Status = AppointmentStatus.Scheduled
        }, _tenantId);

    /// <summary>Adds a row directly, for states the service would refuse to create now.</summary>
    private async Task<Appointment> AddAppointmentAsync(
        DateTime when, int durationMinutes, AppointmentStatus status, string orderNumber, Guid? tenantId = null)
    {
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId ?? _tenantId,
            CustomerId = _customerId,
            OrderNumber = orderNumber,
            AppointmentDate = when,
            DurationMinutes = durationMinutes,
            Description = "Seeded",
            Status = status
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment;
    }

    private Task<int> StoredDurationAsync(Guid id) =>
        _db.Appointments.Where(a => a.Id == id).Select(a => a.DurationMinutes).FirstAsync();

    [Fact]
    public async Task Create_StoresTheBusinessesCurrentSlotLength()
    {
        await SetSlotLengthAsync(45);

        var result = await BookAsync(Tomorrow(10), "ORD-1");

        result.DurationMinutes.Should().Be(45);
        (await StoredDurationAsync(result.Id)).Should().Be(45);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public async Task Create_WhenSlotLengthIsInvalid_FallsBackTo30(int slotMinutes)
    {
        await SetSlotLengthAsync(slotMinutes);

        var result = await BookAsync(Tomorrow(10), "ORD-1");

        (await StoredDurationAsync(result.Id)).Should().Be(30);
    }

    [Fact]
    public async Task ShorterSlotLength_DoesNotShortenExistingAppointments()
    {
        await SetSlotLengthAsync(60);
        var existing = await BookAsync(Tomorrow(10), "ORD-1");

        await SetSlotLengthAsync(15);

        var inside = () => BookAsync(Tomorrow(10, 30), "ORD-2");
        await inside.Should().ThrowAsync<BusinessRuleException>().WithMessage("*overlaps*");

        var after = await BookAsync(Tomorrow(11), "ORD-3");
        after.DurationMinutes.Should().Be(15, "new bookings use the new slot length");
        (await StoredDurationAsync(existing.Id)).Should().Be(60);
    }

    [Fact]
    public async Task LongerSlotLength_DoesNotLengthenExistingAppointments()
    {
        await SetSlotLengthAsync(15);
        await BookAsync(Tomorrow(10), "ORD-1");

        await SetSlotLengthAsync(60);

        var result = await BookAsync(Tomorrow(10, 15), "ORD-2");

        result.DurationMinutes.Should().Be(60);
    }

    [Fact]
    public async Task Move_ChecksOverlapWithItsOwnLength_NotTheCurrentSlotLength()
    {
        await SetSlotLengthAsync(60);
        var moving = await BookAsync(Tomorrow(9), "ORD-1");
        await BookAsync(Tomorrow(14), "ORD-2");

        await SetSlotLengthAsync(15);

        // 13:30 + 15 minutes would fit, but this appointment is 60 minutes long.
        var clash = () => MoveAsync(moving.Id, Tomorrow(13, 30), "ORD-1");
        await clash.Should().ThrowAsync<BusinessRuleException>().WithMessage("*overlaps*");

        var moved = await MoveAsync(moving.Id, Tomorrow(12), "ORD-1");

        moved.AppointmentDate.Should().Be(Tomorrow(12));
        moved.DurationMinutes.Should().Be(60, "moving doesn't change the length");
        (await StoredDurationAsync(moving.Id)).Should().Be(60);
    }

    [Fact]
    public async Task Edit_WithoutMoving_KeepsTheLength()
    {
        await SetSlotLengthAsync(60);
        var existing = await BookAsync(Tomorrow(10), "ORD-1");

        await SetSlotLengthAsync(15);

        var edited = await MoveAsync(existing.Id, Tomorrow(10), "ORD-1-RENAMED");

        edited.OrderNumber.Should().Be("ORD-1-RENAMED");
        (await StoredDurationAsync(existing.Id)).Should().Be(60);
    }

    [Fact]
    public async Task ClosingTime_AppointmentEndingExactlyAtClose_IsAllowed()
    {
        await SetSlotLengthAsync(60);

        var result = await BookAsync(Tomorrow(16), "ORD-1");

        result.AppointmentDate.Should().Be(Tomorrow(16));
    }

    [Theory]
    [InlineData(16, 5)]
    [InlineData(16, 30)]
    [InlineData(16, 59)]
    public async Task ClosingTime_AppointmentEndingAfterClose_IsRefused(int hour, int minute)
    {
        await SetSlotLengthAsync(60);

        var act = () => BookAsync(Tomorrow(hour, minute), "ORD-1");

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("This appointment takes 60 minutes and would end after closing time (17:00)*");
        (await _db.Appointments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ClosingTime_StartingAtClose_StillGetsTheWorkingHoursMessage()
    {
        var act = () => BookAsync(Tomorrow(17), "ORD-1");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Appointment must be between 09:00 and 17:00.");
    }

    [Fact]
    public async Task ClosingTime_EndPastMidnight_DoesNotWrapAroundAndSlipThrough()
    {
        await SetSlotLengthAsync(240);
        var business = await _db.Businesses.FirstAsync(b => b.Id == _tenantId);
        business.WorkDayEnd = new TimeSpan(23, 30, 0);
        await _db.SaveChangesAsync();
        _db.Entry(business).State = EntityState.Detached;

        var act = () => BookAsync(Tomorrow(23), "ORD-1");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*closing time (23:30)*");
    }

    [Fact]
    public async Task ClosingTime_Move_UsesTheAppointmentsOwnLength()
    {
        await SetSlotLengthAsync(60);
        var moving = await BookAsync(Tomorrow(9), "ORD-1");

        await SetSlotLengthAsync(15);

        // 16:30 + 15 minutes would fit, but this appointment is 60 minutes long.
        var tooLate = () => MoveAsync(moving.Id, Tomorrow(16, 30), "ORD-1");
        await tooLate.Should().ThrowAsync<BusinessRuleException>().WithMessage("*takes 60 minutes*closing time*");

        var moved = await MoveAsync(moving.Id, Tomorrow(16), "ORD-1");
        moved.AppointmentDate.Should().Be(Tomorrow(16), "ending exactly at closing time is allowed");
    }

    [Fact]
    public async Task ClosingTime_EditWithoutMoving_IsNotBlockedByAnOlderBooking()
    {
        // Booked before this rule existed, or before the business started closing earlier.
        var existing = await AddAppointmentAsync(Tomorrow(16, 45), durationMinutes: 60, AppointmentStatus.Scheduled, "OLD-1");

        var edited = await MoveAsync(existing.Id, Tomorrow(16, 45), "OLD-1-RENAMED");

        edited.OrderNumber.Should().Be("OLD-1-RENAMED");
    }

    [Theory]
    [InlineData(AppointmentStatus.Scheduled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task Reactivate_SetStatus_WhenTimeIsTaken_IsRefusedAndStaysCancelled(AppointmentStatus newStatus)
    {
        var cancelled = await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Cancelled, "CAN-1");
        await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Scheduled, "NEW-1");

        var act = () => _service.SetStatusAsync(cancelled.Id, newStatus, _tenantId);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*can't be reactivated*overlaps*");
        cancelled.Status.Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Reactivate_EndingAfterClose_IsRefusedAndStaysCancelled()
    {
        var cancelled = await AddAppointmentAsync(Tomorrow(16, 30), 60, AppointmentStatus.Cancelled, "CAN-1");

        var viaStatus = () => _service.SetStatusAsync(cancelled.Id, AppointmentStatus.Scheduled, _tenantId);
        await viaStatus.Should().ThrowAsync<BusinessRuleException>().WithMessage("*takes 60 minutes*closing time (17:00)*");

        var viaEdit = () => MoveAsync(cancelled.Id, Tomorrow(16, 30), "CAN-1");
        await viaEdit.Should().ThrowAsync<BusinessRuleException>().WithMessage("*takes 60 minutes*closing time (17:00)*");

        cancelled.Status.Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Reactivate_EndingExactlyAtClose_IsAllowed()
    {
        var cancelled = await AddAppointmentAsync(Tomorrow(16), 60, AppointmentStatus.Cancelled, "CAN-1");

        await _service.SetStatusAsync(cancelled.Id, AppointmentStatus.Scheduled, _tenantId);

        cancelled.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task StatusChange_NotFromCancelled_DoesNotCheckClosingTime()
    {
        var existing = await AddAppointmentAsync(Tomorrow(16, 30), 60, AppointmentStatus.Scheduled, "OLD-1");

        await _service.SetStatusAsync(existing.Id, AppointmentStatus.Completed, _tenantId);

        existing.Status.Should().Be(AppointmentStatus.Completed);
    }

    [Fact]
    public async Task Reactivate_SetStatus_WhenTimeIsFree_Succeeds()
    {
        var cancelled = await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Cancelled, "CAN-1");
        await AddAppointmentAsync(Tomorrow(10, 30), 30, AppointmentStatus.Scheduled, "NEXT-1");
        await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Cancelled, "CAN-2");

        await _service.SetStatusAsync(cancelled.Id, AppointmentStatus.Scheduled, _tenantId);

        (await _db.Appointments.AsNoTracking().FirstAsync(a => a.Id == cancelled.Id)).Status.Should()
            .Be(AppointmentStatus.Scheduled, "the next one starts where it ends, and another cancelled one takes no time");
    }

    [Fact]
    public async Task Reactivate_UsesTheAppointmentsOwnLength()
    {
        await SetSlotLengthAsync(15);
        var cancelled = await AddAppointmentAsync(Tomorrow(10), 60, AppointmentStatus.Cancelled, "CAN-1");
        await AddAppointmentAsync(Tomorrow(10, 30), 15, AppointmentStatus.Scheduled, "NEW-1");

        var act = () => _service.SetStatusAsync(cancelled.Id, AppointmentStatus.Scheduled, _tenantId);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*can't be reactivated*");
    }

    [Fact]
    public async Task Reactivate_AnotherTenantsAppointment_DoesNotBlock()
    {
        var otherTenant = Guid.NewGuid();
        var cancelled = await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Cancelled, "CAN-1");
        await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Scheduled, "OTHER-1", otherTenant);

        await _service.SetStatusAsync(cancelled.Id, AppointmentStatus.Scheduled, _tenantId);

        cancelled.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task StatusChange_NotFromCancelled_DoesNotRunTheOverlapCheck()
    {
        // Two overlapping rows can exist from before these checks; they must stay manageable.
        var first = await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Scheduled, "OLD-1");
        await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Scheduled, "OLD-2");

        await _service.SetStatusAsync(first.Id, AppointmentStatus.Completed, _tenantId);
        first.Status.Should().Be(AppointmentStatus.Completed);

        await _service.SetStatusAsync(first.Id, AppointmentStatus.Cancelled, _tenantId);
        first.Status.Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task StatusChange_CancelledToCancelled_IsAllowedWhenTimeIsTaken()
    {
        var cancelled = await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Cancelled, "CAN-1");
        await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Scheduled, "NEW-1");

        await _service.SetStatusAsync(cancelled.Id, AppointmentStatus.Cancelled, _tenantId);

        cancelled.Status.Should().Be(AppointmentStatus.Cancelled);
    }

    [Fact]
    public async Task Reactivate_EditForm_WhenTimeIsTaken_IsRefused()
    {
        var cancelled = await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Cancelled, "CAN-1");
        await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Scheduled, "NEW-1");

        var act = () => MoveAsync(cancelled.Id, Tomorrow(10), "CAN-1");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*can't be reactivated*overlaps*");
    }

    [Fact]
    public async Task Reactivate_EditForm_MovingToAFreeTimeAtTheSameTime_Succeeds()
    {
        var cancelled = await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Cancelled, "CAN-1");
        await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Scheduled, "NEW-1");

        var moved = await MoveAsync(cancelled.Id, Tomorrow(12), "CAN-1");

        moved.Status.Should().Be(AppointmentStatus.Scheduled.ToString());
        moved.AppointmentDate.Should().Be(Tomorrow(12));
    }

    [Fact]
    public async Task Reactivate_EditForm_MovingToATakenTime_IsRefused()
    {
        var cancelled = await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Cancelled, "CAN-1");
        await AddAppointmentAsync(Tomorrow(12), 30, AppointmentStatus.Scheduled, "NEW-1");

        var act = () => MoveAsync(cancelled.Id, Tomorrow(12), "CAN-1");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("The selected time overlaps*");
    }

    [Fact]
    public async Task ConfirmationCalendarFile_EndsAfterTheStoredLength()
    {
        await SetSlotLengthAsync(45);
        byte[]? ics = null;
        _email.Setup(e => e.SendEmailWithAttachmentAsync(It.IsAny<EmailMessage>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<EmailMessage, byte[], string, string>((_, bytes, _, _) => ics = bytes)
            .Returns(Task.CompletedTask);

        await BookAsync(Tomorrow(10), "ORD-1", notify: true);

        ics.Should().NotBeNull();
        Encoding.UTF8.GetString(ics!).Should().Contain($"DTEND:{Tomorrow(10, 45):yyyyMMdd'T'HHmmss}");
    }
}
