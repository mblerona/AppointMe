using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

/// <summary>BusinessService settings and calendar config (moved out of SettingsController/AppointmentsController).</summary>
public class BusinessSettingsServiceTests : IDisposable
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"business-settings-{Guid.NewGuid()}")
        .Options);

    private readonly Mock<ILogoStorage> _logoStorage = new(MockBehavior.Strict);
    private readonly BusinessService _sut;

    public BusinessSettingsServiceTests()
    {
        // UserManager isn't used by the settings methods.
        var userManager = new Mock<UserManager<AppointMeAppUser>>(
            Mock.Of<IUserStore<AppointMeAppUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        _sut = new BusinessService(new BusinessRepository(_db), userManager.Object, _logoStorage.Object);

        _db.Businesses.Add(new Business
        {
            Id = Tenant,
            Name = "Old name",
            LogoUrl = "/uploads/logos/old.png",
            EnableServices = true,
            EnableInvoices = true,
            DefaultSlotMinutes = 30,
            WorkDayStart = new TimeSpan(9, 0, 0),
            WorkDayEnd = new TimeSpan(17, 0, 0),
            OpenMon = true, OpenTue = true, OpenWed = true, OpenThu = true, OpenFri = true,
            OpenSat = false, OpenSun = false
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static BusinessSettingsDTO ValidSettings() => new()
    {
        Name = "  New name  ",
        Email = " biz@test.com ",
        Phone = " 070123456 ",
        Address = " Skopje ",
        PrimaryColor = "ABC",
        SecondaryColor = "#11AA22",
        DefaultSlotMinutes = 45,
        EnableServices = true,
        EnableInvoices = true,
        WorkDayStart = new TimeSpan(8, 0, 0),
        WorkDayEnd = new TimeSpan(16, 0, 0),
        OpenMon = true, OpenTue = true, OpenWed = true, OpenThu = true, OpenFri = true,
        OpenSat = true, OpenSun = false
    };

    private async Task<Business> StoredAsync()
    {
        _db.ChangeTracker.Clear();
        return await _db.Businesses.SingleAsync(b => b.Id == Tenant);
    }

    [Fact]
    public async Task GetSettings_ReturnsTheTenantsBusiness()
    {
        var s = await _sut.GetSettingsAsync(Tenant);

        s.Name.Should().Be("Old name");
        s.LogoUrl.Should().Be("/uploads/logos/old.png");
        s.EnableInvoices.Should().BeTrue();
    }

    [Fact]
    public async Task GetSettings_TurnsInvoicesOff_WhenServicesAreOff()
    {
        var b = await _db.Businesses.SingleAsync();
        b.EnableServices = false;
        await _db.SaveChangesAsync();

        (await _sut.GetSettingsAsync(Tenant)).EnableInvoices.Should().BeFalse();
    }

    [Fact]
    public async Task GetSettings_ForUnknownTenant_ThrowsKeyNotFound()
    {
        var act = () => _sut.GetSettingsAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task UpdateSettings_TrimsNormalizesAndSaves()
    {
        await _sut.UpdateSettingsAsync(Tenant, ValidSettings(), logo: null);

        var b = await StoredAsync();
        b.Name.Should().Be("New name");
        b.Email.Should().Be("biz@test.com");
        b.Phone.Should().Be("070123456");
        b.Address.Should().Be("Skopje");
        b.PrimaryColor.Should().Be("#abc");
        b.SecondaryColor.Should().Be("#11aa22");
        b.DefaultSlotMinutes.Should().Be(45);
        b.WorkDayStart.Should().Be(new TimeSpan(8, 0, 0));
        b.OpenSat.Should().BeTrue();
        b.LogoUrl.Should().Be("/uploads/logos/old.png", "the posted LogoUrl is ignored and no logo was uploaded");
    }

    [Theory]
    [InlineData("zzz")]
    [InlineData("#12345")]
    [InlineData("   ")]
    public async Task UpdateSettings_InvalidColor_IsStoredAsNull(string color)
    {
        var s = ValidSettings();
        s.PrimaryColor = color;

        await _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await StoredAsync()).PrimaryColor.Should().BeNull();
    }

    [Fact]
    public async Task UpdateSettings_InvoicesNeedServices()
    {
        var s = ValidSettings();
        s.EnableServices = false;
        s.EnableInvoices = true;

        await _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await StoredAsync()).EnableInvoices.Should().BeFalse();
    }

    [Theory]
    [InlineData(17, 17)]
    [InlineData(17, 9)]
    public async Task UpdateSettings_WorkDayEndNotAfterStart_Throws_AndSavesNothing(int start, int end)
    {
        var s = ValidSettings();
        s.WorkDayStart = new TimeSpan(start, 0, 0);
        s.WorkDayEnd = new TimeSpan(end, 0, 0);

        var act = () => _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.PropertyName.Should().Be(nameof(BusinessSettingsDTO.WorkDayEnd));
        (await StoredAsync()).Name.Should().Be("Old name");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(241)]
    public async Task UpdateSettings_SlotLengthOutOfRange_Throws(int minutes)
    {
        var s = ValidSettings();
        s.DefaultSlotMinutes = minutes;

        var act = () => _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.PropertyName.Should().Be(nameof(BusinessSettingsDTO.DefaultSlotMinutes));
    }

    [Fact]
    public async Task UpdateSettings_EmptyName_Throws()
    {
        var s = ValidSettings();
        s.Name = "  ";

        var act = () => _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.PropertyName.Should().Be(nameof(BusinessSettingsDTO.Name));
    }

    [Fact]
    public async Task UpdateSettings_WithValidLogo_StoresItAndSavesTheUrl()
    {
        using var content = new MemoryStream(new byte[] { 1, 2, 3 });
        _logoStorage.Setup(s => s.SaveAsync(Tenant, ".png", content)).ReturnsAsync("/uploads/logos/new.png");

        await _sut.UpdateSettingsAsync(Tenant, ValidSettings(), new LogoUpload(content, "Logo.PNG", 3));

        (await StoredAsync()).LogoUrl.Should().Be("/uploads/logos/new.png");
        _logoStorage.VerifyAll();
    }

    [Fact]
    public async Task UpdateSettings_LogoTooLarge_Throws_AndStoresNothing()
    {
        using var content = new MemoryStream();
        var logo = new LogoUpload(content, "logo.png", BusinessService.MaxLogoBytes + 1);

        var act = () => _sut.UpdateSettingsAsync(Tenant, ValidSettings(), logo);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*too large*");
        (await StoredAsync()).Name.Should().Be("Old name");
        // Strict mock: any SaveAsync call would have thrown.
    }

    [Theory]
    [InlineData("logo.gif")]
    [InlineData("logo.svg")]
    [InlineData("logo")]
    public async Task UpdateSettings_LogoWithWrongType_Throws(string fileName)
    {
        using var content = new MemoryStream(new byte[] { 1 });

        var act = () => _sut.UpdateSettingsAsync(Tenant, ValidSettings(), new LogoUpload(content, fileName, 1));

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Only PNG, JPG, JPEG, or WEBP*");
    }

    [Fact]
    public async Task UpdateSettings_EmptyLogoFile_IsIgnored()
    {
        using var content = new MemoryStream();

        await _sut.UpdateSettingsAsync(Tenant, ValidSettings(), new LogoUpload(content, "logo.gif", 0));

        (await StoredAsync()).LogoUrl.Should().Be("/uploads/logos/old.png");
    }

    [Fact]
    public async Task UpdateSettings_ForUnknownTenant_ThrowsKeyNotFound()
    {
        var act = () => _sut.UpdateSettingsAsync(Guid.NewGuid(), ValidSettings(), logo: null);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task GetCalendarConfig_MapsHoursClosedDaysAndSlot()
    {
        var config = await _sut.GetCalendarConfigAsync(Tenant);

        config.WorkDayStart.Should().Be(new TimeSpan(9, 0, 0));
        config.WorkDayEnd.Should().Be(new TimeSpan(17, 0, 0));
        config.SlotMinutes.Should().Be(30);
        config.HiddenDays.Should().Equal(0, 6); // Sunday and Saturday are closed
    }

    [Fact]
    public async Task GetCalendarConfig_FallsBackTo30Minutes_ForInvalidSlot()
    {
        var b = await _db.Businesses.SingleAsync();
        b.DefaultSlotMinutes = 0;
        b.OpenSun = true;
        b.OpenWed = false;
        await _db.SaveChangesAsync();

        var config = await _sut.GetCalendarConfigAsync(Tenant);

        config.SlotMinutes.Should().Be(30);
        config.HiddenDays.Should().Equal(3, 6);
    }
}
