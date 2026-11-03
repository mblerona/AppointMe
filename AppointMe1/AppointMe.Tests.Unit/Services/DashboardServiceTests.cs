using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Interface;
using AppointMe.Service.Implementation;
using FluentAssertions;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

public class DashboardServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    // Wednesday 2026-11-18, 14:30 local time.
    private static readonly DateTime Now = new(2026, 11, 18, 14, 30, 0);

    private readonly Mock<IBusinessRepository> _businesses = new();
    private readonly Mock<IAppointmentRepository> _appointments = new();
    private readonly Mock<ICustomerRepository> _customers = new();
    private readonly DashboardService _sut;

    public DashboardServiceTests()
    {
        _businesses.Setup(r => r.GetByIdReadOnlyAsync(Tenant))
            .ReturnsAsync(new Business { Id = Tenant, Name = "Bella", LogoUrl = "/logo.png" });
        _appointments.Setup(r => r.CountByStatusAsync(Tenant))
            .ReturnsAsync(new Dictionary<AppointmentStatus, int>
            {
                [AppointmentStatus.Scheduled] = 3,
                [AppointmentStatus.Completed] = 2
            });
        _appointments.Setup(r => r.GetForDashboardAsync(Tenant, It.IsAny<DateTime?>(), It.IsAny<DateTime?>(),
                It.IsAny<AppointmentStatus?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<int>()))
            .ReturnsAsync(new List<Appointment>());
        _customers.Setup(r => r.CountByTenantAsync(Tenant)).ReturnsAsync(7);

        _sut = new DashboardService(_businesses.Object, _appointments.Object, _customers.Object, new FixedTimeProvider(Now));
    }

    private void VerifyQuery(DateTime? start, DateTime? end, AppointmentStatus? status, string? search) =>
        _appointments.Verify(r => r.GetForDashboardAsync(Tenant, start, end, status, search, null, DashboardService.MaxRows), Times.Once);

    [Fact]
    public async Task ReturnsBusinessAndCounts()
    {
        var d = await _sut.GetDashboardAsync(Tenant, "all", "all", null);

        d.BusinessName.Should().Be("Bella");
        d.LogoUrl.Should().Be("/logo.png");
        d.TotalAppointments.Should().Be(5);
        d.ScheduledAppointments.Should().Be(3);
        d.CompletedAppointments.Should().Be(2);
        d.CancelledAppointments.Should().Be(0, "statuses without appointments count as 0");
        d.CustomersCount.Should().Be(7);
    }

    [Theory]
    [InlineData("all")]
    [InlineData(null)]
    [InlineData("yesterday")]
    public async Task UnknownRange_MeansNoDateFilter(string? range)
    {
        await _sut.GetDashboardAsync(Tenant, range, "all", null);

        VerifyQuery(null, null, null, null);
    }

    [Fact]
    public async Task Today_IsTodayFromMidnight()
    {
        await _sut.GetDashboardAsync(Tenant, "today", "all", null);

        VerifyQuery(new DateTime(2026, 11, 18), new DateTime(2026, 11, 19), null, null);
    }

    [Fact]
    public async Task Week_IsTheNextSevenDaysStartingToday()
    {
        await _sut.GetDashboardAsync(Tenant, "week", "all", null);

        VerifyQuery(new DateTime(2026, 11, 18), new DateTime(2026, 11, 25), null, null);
    }

    [Fact]
    public async Task Month_IsTheCurrentCalendarMonth()
    {
        await _sut.GetDashboardAsync(Tenant, "month", "all", null);

        VerifyQuery(new DateTime(2026, 11, 1), new DateTime(2026, 12, 1), null, null);
    }

    [Theory]
    [InlineData("completed", AppointmentStatus.Completed)]
    [InlineData("Cancelled", AppointmentStatus.Cancelled)]
    public async Task Status_IsParsedCaseInsensitively(string status, AppointmentStatus expected)
    {
        await _sut.GetDashboardAsync(Tenant, "all", status, "ana");

        VerifyQuery(null, null, expected, "ana");
    }

    [Theory]
    [InlineData("all")]
    [InlineData("")]
    [InlineData("not-a-status")]
    public async Task UnknownStatus_MeansNoStatusFilter(string status)
    {
        await _sut.GetDashboardAsync(Tenant, "all", status, null);

        VerifyQuery(null, null, null, null);
    }

    [Fact]
    public async Task PhoneSearch_AlsoPassesTheNumberInE164()
    {
        await _sut.GetDashboardAsync(Tenant, "all", "all", "070 123 456");

        _appointments.Verify(r => r.GetForDashboardAsync(
            Tenant, null, null, null, "070 123 456", "+38970123456", DashboardService.MaxRows), Times.Once);
    }

    [Fact]
    public async Task MapsRows_IncludingMissingCustomer()
    {
        var withCustomer = new Appointment
        {
            Id = Guid.NewGuid(),
            OrderNumber = "ORD-1",
            Description = "Cut",
            AppointmentDate = Now,
            Status = AppointmentStatus.Scheduled,
            Customer = new Customer
            {
                FirstName = "Ana", LastName = "Doe", Email = "ana@test.com",
                PhoneNumber = "070111222", SecondPhoneNumber = "02123456", State = "MK"
            }
        };
        var withoutCustomer = new Appointment { Id = Guid.NewGuid(), OrderNumber = "ORD-2", Status = AppointmentStatus.Completed };
        _appointments.Setup(r => r.GetForDashboardAsync(Tenant, null, null, null, null, null, DashboardService.MaxRows))
            .ReturnsAsync(new List<Appointment> { withCustomer, withoutCustomer });

        var rows = (await _sut.GetDashboardAsync(Tenant, "all", "all", null)).Rows;

        rows.Should().HaveCount(2);
        rows[0].CustomerName.Should().Be("Ana Doe");
        rows[0].Email.Should().Be("ana@test.com");
        rows[0].Phone1.Should().Be("070111222");
        rows[0].Phone2.Should().Be("02123456");
        rows[0].Location.Should().Be("MK");
        rows[0].Status.Should().Be("Scheduled");
        rows[1].CustomerName.Should().Be("—");
        rows[1].Email.Should().BeEmpty();
        rows[1].Status.Should().Be("Completed");
    }

    [Fact]
    public async Task UnknownTenant_ThrowsKeyNotFound()
    {
        var act = () => _sut.GetDashboardAsync(Guid.NewGuid(), "all", "all", null);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    /// <summary>A clock fixed at <paramref name="localNow"/>; the local time zone is UTC to keep it simple.</summary>
    private sealed class FixedTimeProvider(DateTime localNow) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(localNow, DateTimeKind.Utc));
    }
}
