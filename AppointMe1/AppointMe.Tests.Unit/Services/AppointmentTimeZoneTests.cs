using System.Text;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Email;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

/// <summary>
/// Audit B5: "now" is the business's clock, not the server's. The clock is fixed at 20:00 UTC on 1 June 2026,
/// and the zones are far from that, so any code still using the server's time fails here.
/// </summary>
public class AppointmentTimeZoneTests
{
    private static readonly DateTime UtcNow = new(2026, 6, 1, 20, 0, 0);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }

    private readonly ApplicationDbContext _db;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Mock<IEmailService> _email = new();
    private readonly AppointmentService _service;

    public AppointmentTimeZoneTests()
    {
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"timezone-{Guid.NewGuid()}")
            .Options);

        _db.Businesses.Add(new Business
        {
            Id = _tenantId,
            Name = "Test Business",
            DefaultSlotMinutes = 30,
            WorkDayStart = TimeSpan.Zero,
            WorkDayEnd = new TimeSpan(23, 30, 0),
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
            new AppointmentRepository(_db), new CustomerRepository(_db), new ServiceOfferingRepository(_db),
            holidays.Object, new BusinessRepository(_db), _email.Object,
            NullLogger<AppointmentService>.Instance, new FixedClock(UtcNow));
    }

    private async Task SetZoneAsync(string zone)
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == _tenantId);
        business.TimeZoneId = zone;
        await _db.SaveChangesAsync();
        _db.Entry(business).State = EntityState.Detached;
    }

    private Task<AppointmentDTO> BookAsync(DateTime when, string orderNumber = "ORD-1", bool notify = false) =>
        _service.CreateAppointmentAsync(new CreateAppointmentDTO
        {
            CustomerId = _customerId, OrderNumber = orderNumber, AppointmentDate = when, Description = "Zone test", NotifyByEmail = notify
        }, _tenantId);

    // ---------------- what counts as the past ----------------

    [Fact]
    public async Task ZoneAheadOfUtc_ATimeStillAheadOfTheServerClock_IsAlreadyPast()
    {
        await SetZoneAsync("Asia/Tokyo"); // it's 05:00 on 2 June there

        var act = () => BookAsync(new DateTime(2026, 6, 1, 21, 0, 0));

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*must be in the future*");
    }

    [Fact]
    public async Task ZoneAheadOfUtc_LaterThatDay_IsAllowed()
    {
        await SetZoneAsync("Asia/Tokyo");

        var booked = await BookAsync(new DateTime(2026, 6, 2, 5, 30, 0));

        booked.AppointmentDate.Should().Be(new DateTime(2026, 6, 2, 5, 30, 0), "the clock time is stored as it was entered");
    }

    [Fact]
    public async Task ZoneBehindUtc_ATimeAlreadyPastOnTheServerClock_IsStillAllowed()
    {
        await SetZoneAsync("America/Los_Angeles"); // it's 13:00 on 1 June there

        var booked = await BookAsync(new DateTime(2026, 6, 1, 14, 0, 0));

        booked.AppointmentDate.Should().Be(new DateTime(2026, 6, 1, 14, 0, 0));
    }

    [Fact]
    public async Task ZoneBehindUtc_EarlierThatDay_IsPast()
    {
        await SetZoneAsync("America/Los_Angeles");

        var act = () => BookAsync(new DateTime(2026, 6, 1, 12, 30, 0));

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*must be in the future*");
    }

    [Fact]
    public async Task ExactlyNow_IsPast()
    {
        await SetZoneAsync("America/Los_Angeles");

        var act = () => BookAsync(new DateTime(2026, 6, 1, 13, 0, 0));

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*must be in the future*");
    }

    [Fact]
    public async Task Moving_UsesTheBusinessesClockToo()
    {
        await SetZoneAsync("Asia/Tokyo");
        var booked = await BookAsync(new DateTime(2026, 6, 2, 10, 0, 0));

        var act = () => _service.UpdateAppointmentAsync(booked.Id, new UpdateAppointmentDTO
        {
            AppointmentDate = new DateTime(2026, 6, 1, 21, 0, 0),
            OrderNumber = "ORD-1",
            Status = AppointmentStatus.Scheduled
        }, _tenantId);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*must be in the future*");
    }

    [Fact]
    public async Task ChangingTheZone_KeepsTheClockTimeOfExistingAppointments()
    {
        var booked = await BookAsync(new DateTime(2026, 6, 10, 10, 0, 0));

        await SetZoneAsync("Asia/Tokyo");

        var stored = await _service.GetAppointmentByIdAsync(booked.Id, _tenantId);
        stored.AppointmentDate.Should().Be(new DateTime(2026, 6, 10, 10, 0, 0));
    }

    // ---------------- the calendar file ----------------

    private async Task<string> CalendarFileAsync(DateTime when)
    {
        byte[]? ics = null;
        _email.Setup(e => e.SendEmailWithAttachmentAsync(It.IsAny<EmailMessage>(), It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<string>()))
            .Callback<EmailMessage, byte[], string, string>((_, bytes, _, _) => ics = bytes)
            .Returns(Task.CompletedTask);

        await BookAsync(when, notify: true);

        ics.Should().NotBeNull();
        return Encoding.UTF8.GetString(ics!);
    }

    [Theory]
    [InlineData("Europe/Skopje", 2026, 12, 1, 10, "20261201T090000Z", "20261201T093000Z")] // winter, UTC+1
    [InlineData("Europe/Skopje", 2026, 7, 1, 10, "20260701T080000Z", "20260701T083000Z")]  // summer, UTC+2
    [InlineData("Asia/Tokyo", 2026, 7, 1, 8, "20260630T230000Z", "20260630T233000Z")]      // the previous day in UTC
    [InlineData("America/New_York", 2026, 7, 1, 22, "20260702T020000Z", "20260702T023000Z")] // the next day in UTC
    public async Task CalendarFile_IsInUtc(string zone, int y, int m, int d, int h, string start, string end)
    {
        await SetZoneAsync(zone);

        var ics = await CalendarFileAsync(new DateTime(y, m, d, h, 0, 0));

        ics.Should().Contain($"DTSTART:{start}\r\n");
        ics.Should().Contain($"DTEND:{end}\r\n");
    }

    [Fact]
    public async Task CalendarFile_OnATimeSkippedByDaylightSaving_StillSendsTheEmail()
    {
        // 02:30 on 28 March 2027 doesn't exist in Skopje. The booking is fine; the email must not fail on it.
        var ics = await CalendarFileAsync(new DateTime(2027, 3, 28, 2, 30, 0));

        ics.Should().Contain("DTSTART:20270328T013000Z");
    }

    // ---------------- settings ----------------

    private BusinessService NewBusinessService()
    {
        var users = new Mock<UserManager<AppointMeAppUser>>(
            Mock.Of<IUserStore<AppointMeAppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        return new BusinessService(new BusinessRepository(_db), users.Object, Mock.Of<ILogoStorage>(), new FixedClock(UtcNow));
    }

    [Theory]
    [InlineData("Asia/Tokyo", 2026, 6, 2, 5)]
    [InlineData("America/Los_Angeles", 2026, 6, 1, 13)]
    [InlineData("Europe/Skopje", 2026, 6, 1, 22)]
    public async Task CalendarConfig_GivesTheFormsTheBusinessesClock(string zone, int y, int m, int d, int h)
    {
        await SetZoneAsync(zone);

        var config = await NewBusinessService().GetCalendarConfigAsync(_tenantId);

        config.Now.Should().Be(new DateTime(y, m, d, h, 0, 0));
    }
}
