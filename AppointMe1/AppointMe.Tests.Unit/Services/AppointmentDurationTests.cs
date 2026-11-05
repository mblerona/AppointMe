using System.Text;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Email;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using AwesomeAssertions;
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
            NullLogger<AppointmentService>.Instance,
            TimeProvider.System);
    }

    private static DateTime Tomorrow(int hour, int minute = 0) => DateTime.Today.AddDays(1).AddHours(hour).AddMinutes(minute);

    private async Task SetSlotLengthAsync(int minutes)
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == _tenantId);
        business.DefaultSlotMinutes = minutes;
        await _db.SaveChangesAsync();
        _db.Entry(business).State = EntityState.Detached;
    }

    private Task<AppointmentDTO> BookAsync(DateTime when, string orderNumber, bool notify = false, params ServiceOffering[] services) =>
        _service.CreateAppointmentAsync(new CreateAppointmentDTO
        {
            CustomerId = _customerId,
            OrderNumber = orderNumber,
            AppointmentDate = when,
            Description = "Duration test",
            NotifyByEmail = notify,
            ServiceOfferingIds = services.Select(s => s.Id).ToList()
        }, _tenantId);

    private Task<AppointmentDTO> MoveAsync(Guid id, DateTime when, string orderNumber) =>
        _service.UpdateAppointmentAsync(id, new UpdateAppointmentDTO
        {
            AppointmentDate = when,
            OrderNumber = orderNumber,
            Status = AppointmentStatus.Scheduled
        }, _tenantId);

    /// <summary>Saves the Edit form with this selection of services, keeping the date and order number.</summary>
    private async Task<AppointmentDTO> EditServicesAsync(
        Guid id, AppointmentStatus status = AppointmentStatus.Scheduled, DateTime? moveTo = null, params ServiceOffering[] services)
    {
        var stored = await _db.Appointments.AsNoTracking().FirstAsync(a => a.Id == id);
        return await _service.UpdateAppointmentAsync(id, new UpdateAppointmentDTO
        {
            AppointmentDate = moveTo ?? stored.AppointmentDate,
            OrderNumber = stored.OrderNumber,
            Status = status,
            ServiceOfferingIds = services.Select(s => s.Id).ToList()
        }, _tenantId);
    }

    private async Task<ServiceOffering> AddServiceAsync(int? durationMinutes, decimal price = 100, bool isActive = true, Guid? tenantId = null)
    {
        var service = new ServiceOffering
        {
            Id = Guid.NewGuid(), BusinessId = tenantId ?? _tenantId, Name = $"Service {Guid.NewGuid():N}",
            Price = price, DurationMinutes = durationMinutes, IsActive = isActive
        };
        _db.ServiceOfferings.Add(service);
        await _db.SaveChangesAsync();
        return service;
    }

    private async Task SetHoursAsync(TimeSpan open, TimeSpan close)
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == _tenantId);
        business.WorkDayStart = open;
        business.WorkDayEnd = close;
        await _db.SaveChangesAsync();
        _db.Entry(business).State = EntityState.Detached;
    }

    private async Task DisableServicesAsync()
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == _tenantId);
        business.EnableServices = false;
        await _db.SaveChangesAsync();
        _db.Entry(business).State = EntityState.Detached;
    }

    private Task<List<AppointmentServiceModel>> LinesOfAsync(Guid id) =>
        _db.AppointmentServices.AsNoTracking().Where(x => x.AppointmentId == id).ToListAsync();

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

        var result = await BookAsync(Tomorrow(9, 45), "ORD-1");

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
        await BookAsync(Tomorrow(9, 45), "ORD-1");

        await SetSlotLengthAsync(60);

        // The first one still ends at 10:00. With the new length it would have run until 10:45.
        var result = await BookAsync(Tomorrow(10), "ORD-2");

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
    [InlineData(16, 30, 60)]
    [InlineData(16, 0, 90)]
    [InlineData(15, 30, 120)]
    public async Task ClosingTime_AppointmentEndingAfterClose_IsRefused(int hour, int minute, int serviceMinutes)
    {
        var service = await AddServiceAsync(serviceMinutes);

        var act = () => BookAsync(Tomorrow(hour, minute), "ORD-1", false, service);

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage($"This appointment takes {serviceMinutes} minutes and would end after closing time (17:00)*");
        (await _db.Appointments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ClosingTime_LastIntervalBeforeAClosingTimeOffTheGrid_IsRefusedWithoutServices()
    {
        await SetHoursAsync(new TimeSpan(9, 0, 0), new TimeSpan(17, 10, 0));

        var act = () => BookAsync(Tomorrow(17), "ORD-1");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*takes 30 minutes*closing time (17:10)*");
    }

    [Theory]
    [InlineData(20, 9, 0)]
    [InlineData(20, 9, 20)]
    [InlineData(20, 9, 40)]
    [InlineData(15, 10, 45)]
    [InlineData(45, 9, 45)]
    [InlineData(45, 11, 15)]
    [InlineData(60, 16, 0)]
    public async Task Interval_StartOnTheInterval_IsAllowed(int slot, int hour, int minute)
    {
        await SetSlotLengthAsync(slot);

        var result = await BookAsync(Tomorrow(hour, minute), "ORD-1");

        result.AppointmentDate.Should().Be(Tomorrow(hour, minute));
    }

    [Theory]
    [InlineData(20, 9, 15, "Try 09:00 or 09:20.")]
    [InlineData(30, 10, 5, "Try 10:00 or 10:30.")]
    [InlineData(30, 10, 29, "Try 10:00 or 10:30.")]
    [InlineData(45, 10, 0, "Try 09:45 or 10:30.")]
    [InlineData(60, 10, 30, "Try 10:00 or 11:00.")]
    public async Task Interval_StartOffTheInterval_IsRefusedAndSuggestsTimes(int slot, int hour, int minute, string suggestion)
    {
        await SetSlotLengthAsync(slot);

        var act = () => BookAsync(Tomorrow(hour, minute), "ORD-1");

        await act.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage($"Appointments start every {slot} minutes from opening time (09:00). {suggestion}");
        (await _db.Appointments.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Interval_IsCountedFromOpeningTime_NotFromTheHour()
    {
        await SetHoursAsync(new TimeSpan(9, 10, 0), new TimeSpan(17, 0, 0));

        (await BookAsync(Tomorrow(9, 40), "ORD-1")).AppointmentDate.Should().Be(Tomorrow(9, 40));

        var onTheHalfHour = () => BookAsync(Tomorrow(10), "ORD-2");
        await onTheHalfHour.Should().ThrowAsync<BusinessRuleException>()
            .WithMessage("Appointments start every 30 minutes from opening time (09:10). Try 09:40 or 10:10.");
    }

    [Fact]
    public async Task Interval_NearClosing_OnlySuggestsATimeBeforeClosing()
    {
        var act = () => BookAsync(Tomorrow(16, 45), "ORD-1");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*Try 16:30.");
    }

    [Fact]
    public async Task Interval_WithAnInvalidStoredLength_UsesTheFallback()
    {
        await SetSlotLengthAsync(0);

        var act = () => BookAsync(Tomorrow(10, 15), "ORD-1");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Appointments start every 30 minutes*");
    }

    [Fact]
    public async Task Interval_MovingOffTheInterval_IsRefused()
    {
        var booked = await BookAsync(Tomorrow(10), "ORD-1");

        var act = () => MoveAsync(booked.Id, Tomorrow(11, 15), "ORD-1");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Appointments start every 30 minutes*");
    }

    [Fact]
    public async Task Interval_OlderBookingOffTheInterval_StaysEditable()
    {
        var older = await AddAppointmentAsync(Tomorrow(10, 15), 30, AppointmentStatus.Scheduled, "OLD-1");

        // Same date, other fields and services change.
        var edited = await EditServicesAsync(older.Id, services: await AddServiceAsync(30));
        edited.AppointmentDate.Should().Be(Tomorrow(10, 15));

        await _service.SetStatusAsync(older.Id, AppointmentStatus.Completed, _tenantId);
        older.Status.Should().Be(AppointmentStatus.Completed);
    }

    [Fact]
    public async Task Interval_ReactivatingAnOlderBookingOffTheInterval_IsAllowed()
    {
        var cancelled = await AddAppointmentAsync(Tomorrow(10, 15), 30, AppointmentStatus.Cancelled, "CAN-1");

        await _service.SetStatusAsync(cancelled.Id, AppointmentStatus.Scheduled, _tenantId);

        cancelled.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public async Task Interval_ChangingTheInterval_DoesNotAffectExistingAppointments_ButMovingUsesTheNewOne()
    {
        var booked = await BookAsync(Tomorrow(9, 30), "ORD-1");

        await SetSlotLengthAsync(60); // 09:30 is no longer on the interval

        (await EditServicesAsync(booked.Id)).AppointmentDate.Should().Be(Tomorrow(9, 30));

        var offTheNewInterval = () => MoveAsync(booked.Id, Tomorrow(10, 30), "ORD-1");
        await offTheNewInterval.Should().ThrowAsync<BusinessRuleException>().WithMessage("Appointments start every 60 minutes*");

        (await MoveAsync(booked.Id, Tomorrow(11), "ORD-1")).AppointmentDate.Should().Be(Tomorrow(11));
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
        await SetHoursAsync(new TimeSpan(9, 0, 0), new TimeSpan(23, 30, 0));

        // 21:00 + 4 hours is 01:00 the next day, which would look earlier than closing if it wrapped.
        var act = () => BookAsync(Tomorrow(21), "ORD-1");

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
    public async Task Book_WithServices_StoresTheRoundedSum()
    {
        var result = await BookAsync(Tomorrow(10), "ORD-1", false, await AddServiceAsync(45), await AddServiceAsync(30));

        result.DurationMinutes.Should().Be(90, "45 + 30 = 75, rounded up to the 30-minute grid");
        (await StoredDurationAsync(result.Id)).Should().Be(90);
    }

    [Fact]
    public async Task Book_ServiceWithoutDuration_CountsAsOneSlot()
    {
        await SetSlotLengthAsync(15);

        var result = await BookAsync(Tomorrow(10), "ORD-1", false, await AddServiceAsync(null), await AddServiceAsync(20));

        result.DurationMinutes.Should().Be(45, "15 + 20 = 35, rounded up to the 15-minute grid");
    }

    [Fact]
    public async Task Book_WithServicesDisabled_IgnoresPostedServices()
    {
        var service = await AddServiceAsync(120);
        await DisableServicesAsync();

        var result = await BookAsync(Tomorrow(10), "ORD-1", false, service);

        result.DurationMinutes.Should().Be(30);
        (await LinesOfAsync(result.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Book_DurationPastClosing_IsRefused_EndingExactlyAtClosing_IsAllowed()
    {
        var service = await AddServiceAsync(120);

        var tooLate = () => BookAsync(Tomorrow(15, 30), "ORD-1", false, service);
        await tooLate.Should().ThrowAsync<BusinessRuleException>().WithMessage("*takes 120 minutes*closing time*");

        var fits = await BookAsync(Tomorrow(15), "ORD-2", false, service);
        fits.DurationMinutes.Should().Be(120);
    }

    [Fact]
    public async Task Book_DurationRunningIntoTheNextAppointment_IsRefused()
    {
        await BookAsync(Tomorrow(11), "ORD-1");
        var service = await AddServiceAsync(90);

        var clash = () => BookAsync(Tomorrow(10), "ORD-2", false, service);

        await clash.Should().ThrowAsync<BusinessRuleException>().WithMessage("*overlaps*");
    }

    [Fact]
    public async Task Book_AnotherTenantsService_IsRefused()
    {
        var other = await AddServiceAsync(45, tenantId: Guid.NewGuid());

        var act = () => BookAsync(Tomorrow(10), "ORD-1", false, other);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*services are invalid*");
    }

    [Fact]
    public async Task Book_InactiveService_IsRefused()
    {
        var inactive = await AddServiceAsync(45, isActive: false);

        var act = () => BookAsync(Tomorrow(10), "ORD-1", false, inactive);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*services are invalid*");
    }

    [Fact]
    public async Task EditServices_Adding_Recalculates()
    {
        var first = await AddServiceAsync(30);
        var booked = await BookAsync(Tomorrow(10), "ORD-1", false, first);

        var edited = await EditServicesAsync(booked.Id, services: new[] { first, await AddServiceAsync(45) });

        edited.DurationMinutes.Should().Be(90);
        (await StoredDurationAsync(booked.Id)).Should().Be(90);
    }

    [Fact]
    public async Task EditServices_RemovingAll_GoesBackToTheCurrentSlotLength()
    {
        var booked = await BookAsync(Tomorrow(10), "ORD-1", false, await AddServiceAsync(120));
        await SetSlotLengthAsync(15);

        var edited = await EditServicesAsync(booked.Id);

        edited.DurationMinutes.Should().Be(15);
        (await LinesOfAsync(booked.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task ChangingAServicesDuration_DoesNotAffectExistingAppointments_EvenAfterAnUnrelatedEdit()
    {
        var service = await AddServiceAsync(60);
        var booked = await BookAsync(Tomorrow(10), "ORD-1", false, service);

        service.DurationMinutes = 120;
        await _db.SaveChangesAsync();

        (await StoredDurationAsync(booked.Id)).Should().Be(60);

        var edited = await EditServicesAsync(booked.Id, services: service);

        edited.DurationMinutes.Should().Be(60, "the selection didn't change, so the snapshot stays");
        (await StoredDurationAsync(booked.Id)).Should().Be(60);
    }

    [Fact]
    public async Task EditServices_WhenTheSelectionChanges_UsesTheServicesCurrentDurations()
    {
        var kept = await AddServiceAsync(30);
        var booked = await BookAsync(Tomorrow(10), "ORD-1", false, kept);
        kept.DurationMinutes = 60;
        await _db.SaveChangesAsync();

        var edited = await EditServicesAsync(booked.Id, services: new[] { kept, await AddServiceAsync(30) });

        edited.DurationMinutes.Should().Be(90);
    }

    [Fact]
    public async Task EditServices_KeptServicesKeepTheirPrice_NewOnesGetTodaysPrice()
    {
        var kept = await AddServiceAsync(30, price: 100);
        var removed = await AddServiceAsync(30, price: 50);
        var booked = await BookAsync(Tomorrow(10), "ORD-1", false, kept, removed);
        kept.Price = 999;
        await _db.SaveChangesAsync();
        var added = await AddServiceAsync(30, price: 200);

        await EditServicesAsync(booked.Id, services: new[] { kept, added });

        var lines = await LinesOfAsync(booked.Id);
        lines.Should().HaveCount(2);
        lines.Single(x => x.ServiceOfferingId == kept.Id).PriceAtBooking.Should().Be(100);
        lines.Single(x => x.ServiceOfferingId == added.Id).PriceAtBooking.Should().Be(200);
    }

    [Fact]
    public async Task UnrelatedEdit_KeepsThePriceAtBooking()
    {
        var service = await AddServiceAsync(30, price: 100);
        var booked = await BookAsync(Tomorrow(10), "ORD-1", false, service);
        service.Price = 999;
        await _db.SaveChangesAsync();

        await EditServicesAsync(booked.Id, services: service);

        (await LinesOfAsync(booked.Id)).Single().PriceAtBooking.Should().Be(100);
    }

    [Fact]
    public async Task EditServices_LongerDurationOverlapping_IsRefusedAndChangesNothing()
    {
        var first = await AddServiceAsync(30);
        var booked = await BookAsync(Tomorrow(10), "ORD-1", false, first);
        await BookAsync(Tomorrow(10, 30), "ORD-2");
        var tracked = await _db.Appointments.FirstAsync(a => a.Id == booked.Id);
        var added = await AddServiceAsync(30);

        var act = () => EditServicesAsync(booked.Id, services: new[] { first, added });

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*60 minutes long*overlaps*");
        tracked.DurationMinutes.Should().Be(30);
        tracked.AppointmentServices.Should().ContainSingle();
    }

    [Fact]
    public async Task EditServices_LongerDurationPastClosing_IsRefused()
    {
        var first = await AddServiceAsync(30);
        var booked = await BookAsync(Tomorrow(16, 30), "ORD-1", false, first);
        var added = await AddServiceAsync(30);

        var act = () => EditServicesAsync(booked.Id, services: new[] { first, added });

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*takes 60 minutes*closing time*");
        (await StoredDurationAsync(booked.Id)).Should().Be(30);
    }

    [Fact]
    public async Task EditServices_ShorterDuration_IsAllowedOnAnOlderConflictingBooking()
    {
        // Overlaps another booking and runs past closing, from before these checks existed.
        var existing = await AddAppointmentAsync(Tomorrow(16, 30), 120, AppointmentStatus.Scheduled, "OLD-1");
        await AddAppointmentAsync(Tomorrow(16, 30), 30, AppointmentStatus.Scheduled, "OLD-2");

        var edited = await EditServicesAsync(existing.Id, services: await AddServiceAsync(60));

        edited.DurationMinutes.Should().Be(60);
    }

    [Fact]
    public async Task EditServices_OnAPastAppointment_DoesNotRunTheFutureDateRule()
    {
        var past = await AddAppointmentAsync(new DateTime(2025, 3, 10, 10, 0, 0), 30, AppointmentStatus.Scheduled, "OLD-1");

        var edited = await EditServicesAsync(past.Id, services: await AddServiceAsync(45));

        edited.DurationMinutes.Should().Be(60);
    }

    [Fact]
    public async Task EditServices_OnACancelledAppointment_IsNotChecked_UntilItIsReactivated()
    {
        var cancelled = await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Cancelled, "CAN-1");
        await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Scheduled, "NEW-1");
        var service = await AddServiceAsync(60);

        var edited = await EditServicesAsync(cancelled.Id, AppointmentStatus.Cancelled, services: service);
        edited.DurationMinutes.Should().Be(60);

        var reactivate = () => _service.SetStatusAsync(cancelled.Id, AppointmentStatus.Scheduled, _tenantId);
        await reactivate.Should().ThrowAsync<BusinessRuleException>().WithMessage("*can't be reactivated*");
    }

    [Fact]
    public async Task EditServices_ReactivatingAtTheSameTime_ChecksTheNewDuration()
    {
        var cancelled = await AddAppointmentAsync(Tomorrow(10), 30, AppointmentStatus.Cancelled, "CAN-1");
        await AddAppointmentAsync(Tomorrow(10, 30), 30, AppointmentStatus.Scheduled, "NEXT-1");
        var longer = await AddServiceAsync(60);

        var act = () => EditServicesAsync(cancelled.Id, services: longer);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*can't be reactivated*");
        (await StoredDurationAsync(cancelled.Id)).Should().Be(30);
    }

    [Fact]
    public async Task EditServices_MovingAtTheSameTime_ChecksTheNewDuration()
    {
        var booked = await BookAsync(Tomorrow(10), "ORD-1");
        var longer = await AddServiceAsync(120);

        var tooLate = () => EditServicesAsync(booked.Id, moveTo: Tomorrow(16), services: longer);
        await tooLate.Should().ThrowAsync<BusinessRuleException>().WithMessage("*takes 120 minutes*closing time*");

        var moved = await EditServicesAsync(booked.Id, moveTo: Tomorrow(15), services: longer);
        moved.DurationMinutes.Should().Be(120);
        moved.AppointmentDate.Should().Be(Tomorrow(15));
    }

    [Fact]
    public async Task EditServices_MovingAtTheSameTime_ChecksOverlapWithTheNewDuration()
    {
        var booked = await BookAsync(Tomorrow(10), "ORD-1");
        await BookAsync(Tomorrow(14, 30), "ORD-2");
        var longer = await AddServiceAsync(60);

        // 14:00 + the old 30 minutes would fit, but with this service it runs into 14:30.
        var clash = () => EditServicesAsync(booked.Id, moveTo: Tomorrow(14), services: longer);

        await clash.Should().ThrowAsync<BusinessRuleException>().WithMessage("The selected time overlaps*");
        (await StoredDurationAsync(booked.Id)).Should().Be(30);
    }

    [Fact]
    public async Task Edit_WithServicesDisabled_KeepsTheServicesAndTheDuration()
    {
        var service = await AddServiceAsync(60, price: 100);
        var booked = await BookAsync(Tomorrow(10), "ORD-1", false, service);
        await DisableServicesAsync();

        // With services disabled the form has no checkboxes, so it posts an empty list.
        var edited = await EditServicesAsync(booked.Id);

        edited.DurationMinutes.Should().Be(60);
        (await LinesOfAsync(booked.Id)).Single().ServiceOfferingId.Should().Be(service.Id);
    }

    [Fact]
    public async Task EditServices_AttachedInactiveServiceCanStay_ButCantBeNewlyAdded()
    {
        var attached = await AddServiceAsync(30);
        var booked = await BookAsync(Tomorrow(10), "ORD-1", false, attached);
        attached.IsActive = false;
        await _db.SaveChangesAsync();

        (await EditServicesAsync(booked.Id, services: attached)).Id.Should().Be(booked.Id);

        var inactive = await AddServiceAsync(30, isActive: false);
        var act = () => EditServicesAsync(booked.Id, services: new[] { attached, inactive });
        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*services are invalid*");
    }

    [Fact]
    public async Task EditServices_AnotherTenantsService_IsRefused()
    {
        var booked = await BookAsync(Tomorrow(10), "ORD-1");
        var other = await AddServiceAsync(45, tenantId: Guid.NewGuid());

        var act = () => EditServicesAsync(booked.Id, services: other);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*services are invalid*");
    }

    [Fact]
    public async Task ConfirmationCalendarFile_EndsAfterTheStoredLength()
    {
        await SetSlotLengthAsync(45);
        byte[]? ics = null;
        _email.Setup(e => e.SendEmailWithAttachmentAsync(It.IsAny<EmailMessage>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<EmailMessage, byte[], string, string>((_, bytes, _, _) => ics = bytes)
            .Returns(Task.CompletedTask);

        await BookAsync(Tomorrow(9, 45), "ORD-1", notify: true);

        ics.Should().NotBeNull();
        var endUtc = AppointMe.Domain.TimeZones.BusinessTimeZones.ToUtc(Tomorrow(10, 30), "Europe/Skopje");
        Encoding.UTF8.GetString(ics!).Should().Contain($"DTEND:{endUtc:yyyyMMdd'T'HHmmss}Z");
    }
}
