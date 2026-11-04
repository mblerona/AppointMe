using AppointMe.Domain.DomainModels;
using AppointMe.Domain.TimeZones;
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
/// Dates stored in UTC reach the pages and the PDF in the business's time zone, and the invoice year is
/// the business's year. Appointment times are already clock times and must not be converted again.
/// </summary>
public class BusinessLocalDatesTests
{
    private static readonly DateTime StoredUtc = new(2026, 6, 1, 20, 0, 0);
    private static readonly DateTime StoredInTokyo = new(2026, 6, 2, 5, 0, 0);
    private static readonly DateTime AppointmentClockTime = new(2026, 7, 1, 10, 0, 0);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }

    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"local-dates-{Guid.NewGuid()}")
        .Options);

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Customer _customer;
    private readonly Appointment _appointment;

    public BusinessLocalDatesTests()
    {
        _db.Businesses.Add(new Business { Id = _tenantId, Name = "Biz", TimeZoneId = "Asia/Tokyo", EnableInvoices = true });
        _customer = new Customer
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, CustomerNumber = 1, FirstName = "Ana", LastName = "Doe", Email = "ana@test.com",
            PhoneNumber = "+38970123456", State = "MK", City = "Skopje", CreatedAt = StoredUtc, UpdatedAt = StoredUtc
        };
        _appointment = new Appointment
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, CustomerId = _customer.Id, OrderNumber = "ORD-1", Description = "desc",
            AppointmentDate = AppointmentClockTime, CreatedAt = StoredUtc, UpdatedAt = StoredUtc
        };
        _db.AddRange(_customer, _appointment);
        _db.SaveChanges();
    }

    private CustomerService Customers() => new(new CustomerRepository(_db), new BusinessRepository(_db));

    private AppointmentService Appointments() => new(
        new AppointmentRepository(_db), new CustomerRepository(_db), new ServiceOfferingRepository(_db),
        Mock.Of<IHolidayService>(), new BusinessRepository(_db), Mock.Of<IEmailService>(),
        NullLogger<AppointmentService>.Instance, TimeProvider.System);

    private InvoiceService Invoices(DateTime utcNow) => new(
        new InvoiceRepository(_db), new AppointmentRepository(_db), new BusinessRepository(_db), new FixedClock(utcNow));

    private async Task SetZoneAsync(string zone)
    {
        var business = await _db.Businesses.FirstAsync(b => b.Id == _tenantId);
        business.TimeZoneId = zone;
        await _db.SaveChangesAsync();
    }

    private async Task<Appointment> AddAppointmentAsync(string orderNumber)
    {
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, CustomerId = _customer.Id, OrderNumber = orderNumber,
            Description = "desc", AppointmentDate = AppointmentClockTime
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment;
    }

    // ---------------- customers ----------------

    [Fact]
    public async Task Customer_DatesAreInTheBusinessesZone()
    {
        var dto = await Customers().GetCustomerByIdAsync(_customer.Id, _tenantId);

        dto.CreatedAt.Should().Be(StoredInTokyo);
        dto.UpdatedAt.Should().Be(StoredInTokyo);
    }

    [Fact]
    public async Task CustomerList_DatesAreInTheBusinessesZone()
    {
        (await Customers().GetAllCustomersAsync(_tenantId)).Single().CreatedAt.Should().Be(StoredInTokyo);
        (await Customers().SearchCustomersAsync("Ana", _tenantId)).Single().UpdatedAt.Should().Be(StoredInTokyo);
    }

    [Fact]
    public async Task CustomerProfile_DatesAreInTheBusinessesZone_AppointmentTimesAreUnchanged()
    {
        var profile = await Customers().GetCustomerProfileAsync(_customer.Id, _tenantId);

        profile.CreatedAt.Should().Be(StoredInTokyo);
        profile.UpdatedAt.Should().Be(StoredInTokyo);

        var appointment = profile.Appointments.Single();
        appointment.CreatedAt.Should().Be(StoredInTokyo);
        appointment.AppointmentDate.Should().Be(AppointmentClockTime, "it already is a clock time");
    }

    [Fact]
    public async Task CustomerProfile_IncludesTheAppointmentsDuration()
    {
        _appointment.DurationMinutes = 90;
        await _db.SaveChangesAsync();

        var profile = await Customers().GetCustomerProfileAsync(_customer.Id, _tenantId);

        profile.Appointments.Single().DurationMinutes.Should().Be(90);
    }

    [Fact]
    public async Task ConvertingForDisplay_DoesNotChangeWhatIsStored()
    {
        await Customers().GetCustomerProfileAsync(_customer.Id, _tenantId);
        await Appointments().GetAppointmentByIdAsync(_appointment.Id, _tenantId);
        await _db.SaveChangesAsync();

        _db.ChangeTracker.Clear();
        (await _db.Customers.FirstAsync(c => c.Id == _customer.Id)).CreatedAt.Should().Be(StoredUtc);
        (await _db.Appointments.FirstAsync(a => a.Id == _appointment.Id)).CreatedAt.Should().Be(StoredUtc);
    }

    // ---------------- appointments ----------------

    [Fact]
    public async Task Appointment_DatesAreInTheBusinessesZone_TheAppointmentTimeIsUnchanged()
    {
        var dto = await Appointments().GetAppointmentByIdAsync(_appointment.Id, _tenantId);

        dto.CreatedAt.Should().Be(StoredInTokyo);
        dto.UpdatedAt.Should().Be(StoredInTokyo);
        dto.AppointmentDate.Should().Be(AppointmentClockTime);
    }

    [Fact]
    public async Task AppointmentLists_DatesAreInTheBusinessesZone()
    {
        (await Appointments().GetAllAppointmentsAsync(_tenantId)).Single().CreatedAt.Should().Be(StoredInTokyo);
        (await Appointments().GetAppointmentsByCustomerAsync(_customer.Id, _tenantId)).Single().CreatedAt.Should().Be(StoredInTokyo);
        (await Appointments().GetAppointmentsByStatusAsync(AppointmentStatus.Scheduled, _tenantId)).Single().CreatedAt.Should().Be(StoredInTokyo);
        (await Appointments().GetAppointmentsByDateRangeAsync(AppointmentClockTime.AddDays(-1), AppointmentClockTime.AddDays(1), _tenantId))
            .Single().UpdatedAt.Should().Be(StoredInTokyo);
    }

    // ---------------- invoices ----------------

    [Fact]
    public async Task Invoice_IssuedAtIsInTheBusinessesZone_TheAppointmentDateIsUnchanged()
    {
        var created = await Invoices(StoredUtc).CreateOrGetForAppointmentAsync(_appointment.Id, _tenantId);

        created.IssuedAt.Should().Be(StoredInTokyo);
        created.AppointmentDate.Should().Be(AppointmentClockTime, "the snapshot of a clock time must not be converted");

        (await Invoices(StoredUtc).GetByIdAsync(created.Id, _tenantId)).IssuedAt.Should().Be(StoredInTokyo);
        (await Invoices(StoredUtc).GetAllAsync(_tenantId)).Single().IssuedAt.Should().Be(StoredInTokyo);
        (await Invoices(StoredUtc).CreateOrGetForAppointmentAsync(_appointment.Id, _tenantId)).IssuedAt
            .Should().Be(StoredInTokyo, "fetching the existing invoice gives the same answer");

        _db.ChangeTracker.Clear();
        (await _db.Invoices.SingleAsync()).IssuedAt.Should().Be(StoredUtc, "it stays UTC in the database");
    }

    [Theory]
    [InlineData("Asia/Tokyo", 2026, 12, 31, 20, 2027)]            // already 2027 there
    [InlineData("Europe/Skopje", 2026, 12, 31, 23, 2027)]         // 00:00 on 1 January there
    [InlineData("Europe/Skopje", 2026, 12, 31, 22, 2026)]         // still 23:00 there
    [InlineData("America/Los_Angeles", 2027, 1, 1, 2, 2026)]      // still 2026 there, 2027 in UTC
    [InlineData("UTC", 2027, 1, 1, 0, 2027)]
    public async Task InvoiceNumberYear_IsTheBusinessesYear(string zone, int y, int m, int d, int h, int expectedYear)
    {
        await SetZoneAsync(zone);

        var created = await Invoices(new DateTime(y, m, d, h, 0, 0)).CreateOrGetForAppointmentAsync(_appointment.Id, _tenantId);

        created.InvoiceNumber.Should().Be($"INV-{expectedYear}-0001");
        var stored = await _db.Invoices.SingleAsync();
        stored.InvoiceYear.Should().Be(expectedYear);
        stored.InvoiceSequence.Should().Be(1);
    }

    [Fact]
    public async Task InvoiceSequence_RestartsWithTheBusinessesNewYear()
    {
        var clock2026 = new DateTime(2026, 12, 31, 14, 0, 0); // 23:00 in Tokyo
        var clock2027 = new DateTime(2026, 12, 31, 16, 0, 0); // 01:00 on 1 January in Tokyo

        (await Invoices(clock2026).CreateOrGetForAppointmentAsync(_appointment.Id, _tenantId)).InvoiceNumber.Should().Be("INV-2026-0001");
        (await Invoices(clock2026).CreateOrGetForAppointmentAsync((await AddAppointmentAsync("ORD-2")).Id, _tenantId)).InvoiceNumber.Should().Be("INV-2026-0002");
        (await Invoices(clock2027).CreateOrGetForAppointmentAsync((await AddAppointmentAsync("ORD-3")).Id, _tenantId)).InvoiceNumber.Should().Be("INV-2027-0001");
    }

    [Fact]
    public async Task InvoiceSequence_ContinuesPast9999()
    {
        _db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, AppointmentId = Guid.NewGuid(), CustomerId = _customer.Id, InvoiceNumber = "INV-2026-9999",
            InvoiceYear = 2026, InvoiceSequence = 9999, CustomerNameSnapshot = "Ana", BusinessNameSnapshot = "Biz"
        });
        await _db.SaveChangesAsync();

        var created = await Invoices(StoredUtc).CreateOrGetForAppointmentAsync(_appointment.Id, _tenantId);

        created.InvoiceNumber.Should().Be("INV-2026-10000");
    }

    // ---------------- the conversion ----------------

    [Theory]
    [InlineData("Asia/Tokyo", 2026, 6, 2, 5)]
    [InlineData("America/Los_Angeles", 2026, 6, 1, 13)]
    [InlineData("Europe/Skopje", 2026, 6, 1, 22)]
    [InlineData("UTC", 2026, 6, 1, 20)]
    public void FromUtc_GivesTheClockTime(string zone, int y, int m, int d, int h)
    {
        var result = BusinessTimeZones.FromUtc(StoredUtc, zone);

        result.Should().Be(new DateTime(y, m, d, h, 0, 0));
        result.Kind.Should().Be(DateTimeKind.Unspecified);
    }

    [Theory]
    [InlineData("Asia/Tokyo")]
    [InlineData("America/Los_Angeles")]
    public void FromUtc_WithADateThatWasNeverSet_DoesNotThrow(string zone)
    {
        // A row can have 0001-01-01. A negative offset would go below the smallest date.
        var act = () => BusinessTimeZones.FromUtc(default, zone);

        act.Should().NotThrow();
    }
}
