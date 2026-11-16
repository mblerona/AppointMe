using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
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
/// The booking form's day planner (GetDayAvailabilityAsync) must agree with the booking rules: the
/// interval from opening time, closed days, holidays, the business's "now" and the overlap rule.
/// Uses the real repositories, so the date query and the tenant filter are part of the test.
/// </summary>
public class DayAvailabilityTests
{
    // 08:00 UTC is 10:00 in Skopje (summer time). 1 June 2026 is a Monday.
    private static readonly DateTime UtcNow = new(2026, 6, 1, 8, 0, 0);
    private static readonly DateOnly Monday = new(2026, 6, 1);
    private static readonly DateOnly Tuesday = new(2026, 6, 2);
    private static readonly DateOnly Saturday = new(2026, 6, 6);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }

    private readonly ApplicationDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _otherTenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _otherCustomerId = Guid.NewGuid();
    private readonly List<HolidayDTO> _holidays = new();
    private readonly AppointmentService _service;

    public DayAvailabilityTests()
    {
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"day-availability-{Guid.NewGuid()}")
            .Options);

        foreach (var id in new[] { _tenantId, _otherTenantId })
        {
            _db.Businesses.Add(new Business
            {
                Id = id,
                Name = "Biz",
                DefaultSlotMinutes = 30,
                WorkDayStart = new TimeSpan(9, 0, 0),
                WorkDayEnd = new TimeSpan(17, 0, 0),
                OpenMon = true, OpenTue = true, OpenWed = true, OpenThu = true, OpenFri = true
            });
        }

        _db.Customers.Add(new Customer
        {
            Id = _customerId, TenantId = _tenantId, FirstName = "Ana", LastName = "Doe",
            Email = "ana@test.com", PhoneNumber = "+38970123456"
        });
        _db.Customers.Add(new Customer
        {
            Id = _otherCustomerId, TenantId = _otherTenantId, FirstName = "Other", LastName = "Tenant",
            Email = "other@test.com", PhoneNumber = "+38970123456"
        });
        _db.SaveChanges();

        var holidays = new Mock<IHolidayService>();
        holidays.Setup(h => h.GetHolidaysAsync(It.IsAny<int>(), "MK")).ReturnsAsync(() => _holidays.ToList());

        _service = new AppointmentService(
            new AppointmentRepository(_db),
            new CustomerRepository(_db),
            new ServiceOfferingRepository(_db),
            holidays.Object,
            new BusinessRepository(_db),
            new Mock<IEmailService>().Object,
            NullLogger<AppointmentService>.Instance,
            new FixedClock(UtcNow));
    }

    private void Book(DateOnly day, int hour, int minute, int duration, string order,
        AppointmentStatus status = AppointmentStatus.Scheduled, Guid? tenantId = null)
    {
        var tenant = tenantId ?? _tenantId;
        _db.Appointments.Add(new Appointment
        {
            Id = Guid.NewGuid(),
            TenantId = tenant,
            CustomerId = tenant == _tenantId ? _customerId : _otherCustomerId,
            OrderNumber = order,
            Description = "x",
            AppointmentDate = day.ToDateTime(new TimeOnly(hour, minute)),
            DurationMinutes = duration,
            Status = status
        });
        _db.SaveChanges();
    }

    private static DaySlotState StateAt(DayAvailabilityDTO day, int hour, int minute) =>
        day.Slots.Single(s => s.StartMinute == hour * 60 + minute).State;

    [Fact]
    public async Task AnEmptyOpenDay_HasEverySlotFromOpeningToClosing_OnTheInterval()
    {
        var day = await _service.GetDayAvailabilityAsync(Tuesday, _tenantId);

        day.IsOpen.Should().BeTrue();
        day.OpeningMinute.Should().Be(9 * 60);
        day.ClosingMinute.Should().Be(17 * 60);
        day.SlotMinutes.Should().Be(30);
        day.Slots.Select(s => s.StartMinute).Should().Equal(Enumerable.Range(0, 16).Select(i => 9 * 60 + i * 30));
        day.Slots.Should().OnlyContain(s => s.State == DaySlotState.Free);
        day.Bookings.Should().BeEmpty();
    }

    [Fact]
    public async Task Today_SlotsAtOrBeforeTheBusinessesNow_ArePast()
    {
        var day = await _service.GetDayAvailabilityAsync(Monday, _tenantId);

        StateAt(day, 9, 30).Should().Be(DaySlotState.Past);
        StateAt(day, 10, 0).Should().Be(DaySlotState.Past, "booking needs a time after now, and it is 10:00 in Skopje");
        StateAt(day, 10, 30).Should().Be(DaySlotState.Free);
    }

    [Fact]
    public async Task Bookings_BlockEverySlotTheyCover_ButCancelledOnesDont()
    {
        Book(Tuesday, 11, 0, 60, "LONG");
        Book(Tuesday, 13, 0, 30, "CANCELLED", AppointmentStatus.Cancelled);
        Book(Tuesday, 14, 0, 30, "DONE", AppointmentStatus.Completed);

        var day = await _service.GetDayAvailabilityAsync(Tuesday, _tenantId);

        StateAt(day, 10, 30).Should().Be(DaySlotState.Free);
        StateAt(day, 11, 0).Should().Be(DaySlotState.Booked);
        StateAt(day, 11, 30).Should().Be(DaySlotState.Booked, "the 60-minute booking covers it");
        StateAt(day, 12, 0).Should().Be(DaySlotState.Free);
        StateAt(day, 13, 0).Should().Be(DaySlotState.Free, "a cancelled appointment frees its time");
        StateAt(day, 14, 0).Should().Be(DaySlotState.Booked, "the overlap rule counts every status except cancelled");

        day.Bookings.Select(b => b.OrderNumber).Should().Equal("LONG", "DONE");
        var first = day.Bookings[0];
        first.StartMinute.Should().Be(11 * 60);
        first.EndMinute.Should().Be(12 * 60);
        first.CustomerName.Should().Be("Ana Doe");
    }

    [Fact]
    public async Task ABookingOffTheInterval_BlocksBothSlotsItTouches()
    {
        Book(Tuesday, 9, 45, 30, "OFF");

        var day = await _service.GetDayAvailabilityAsync(Tuesday, _tenantId);

        StateAt(day, 9, 30).Should().Be(DaySlotState.Booked);
        StateAt(day, 10, 0).Should().Be(DaySlotState.Booked);
        StateAt(day, 10, 30).Should().Be(DaySlotState.Free);
    }

    [Fact]
    public async Task OnlyThisTenantsAndThisDaysBookings_AreShown()
    {
        Book(Tuesday, 15, 0, 30, "OTHER-TENANT", tenantId: _otherTenantId);
        Book(Tuesday.AddDays(1), 15, 0, 30, "NEXT-DAY");
        Book(Monday, 16, 0, 30, "DAY-BEFORE");

        var day = await _service.GetDayAvailabilityAsync(Tuesday, _tenantId);

        day.Bookings.Should().BeEmpty();
        StateAt(day, 15, 0).Should().Be(DaySlotState.Free);
    }

    [Fact]
    public async Task AClosedWeekday_IsNotOpen_AndHasNoSlots()
    {
        var day = await _service.GetDayAvailabilityAsync(Saturday, _tenantId);

        day.IsOpen.Should().BeFalse();
        day.HolidayName.Should().BeNull();
        day.Slots.Should().BeEmpty();
    }

    [Fact]
    public async Task APublicHoliday_IsNotOpen_AndNamesTheHoliday()
    {
        _holidays.Add(new HolidayDTO { Date = Tuesday.ToDateTime(TimeOnly.MinValue), LocalName = "Празник", Name = "Holiday" });

        var day = await _service.GetDayAvailabilityAsync(Tuesday, _tenantId);

        day.IsOpen.Should().BeFalse();
        day.HolidayName.Should().Be("Празник");
        day.Slots.Should().BeEmpty();
    }

    [Fact]
    public async Task AHolidayWithoutAName_StillSaysPublicHoliday()
    {
        _holidays.Add(new HolidayDTO { Date = Tuesday.ToDateTime(TimeOnly.MinValue), LocalName = "", Name = "" });

        var day = await _service.GetDayAvailabilityAsync(Tuesday, _tenantId);

        day.HolidayName.Should().Be("Public holiday");
    }

    [Fact]
    public async Task AnUnevenInterval_StopsBeforeClosingTime()
    {
        var business = await _db.Businesses.SingleAsync(b => b.Id == _tenantId);
        business.DefaultSlotMinutes = 45;
        await _db.SaveChangesAsync();

        var day = await _service.GetDayAvailabilityAsync(Tuesday, _tenantId);

        day.SlotMinutes.Should().Be(45);
        day.Slots.First().StartMinute.Should().Be(9 * 60);
        day.Slots.Should().OnlyContain(s => s.StartMinute < 17 * 60 && (s.StartMinute - 9 * 60) % 45 == 0);
        day.Slots.Last().StartMinute.Should().Be(16 * 60 + 30);
    }
}
