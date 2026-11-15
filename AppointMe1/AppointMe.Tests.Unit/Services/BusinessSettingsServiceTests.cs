using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Repository.Interface;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using AwesomeAssertions;
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

        _sut = new BusinessService(new BusinessRepository(_db), userManager.Object, _logoStorage.Object, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<BusinessService>.Instance);

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
    public async Task UpdateSettings_SavesASupportedTimeZone()
    {
        var s = ValidSettings();
        s.TimeZoneId = "Asia/Tokyo";

        await _sut.UpdateSettingsAsync(Tenant, s, null);

        (await StoredAsync()).TimeZoneId.Should().Be("Asia/Tokyo");
        (await _sut.GetSettingsAsync(Tenant)).TimeZoneId.Should().Be("Asia/Tokyo");
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("europe/skopje")]
    [InlineData("Central European Standard Time")]
    public async Task UpdateSettings_WithAnUnsupportedTimeZone_Throws_AndSavesNothing(string zone)
    {
        var s = ValidSettings();
        s.TimeZoneId = zone;

        var act = () => _sut.UpdateSettingsAsync(Tenant, s, null);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be(nameof(BusinessSettingsDTO.TimeZoneId));
        var stored = await StoredAsync();
        stored.TimeZoneId.Should().Be("Europe/Skopje");
        stored.Name.Should().Be("Old name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task UpdateSettings_WithoutATimeZone_KeepsTheCurrentOne(string? zone)
    {
        var s = ValidSettings();
        s.TimeZoneId = "Asia/Tokyo";
        await _sut.UpdateSettingsAsync(Tenant, s, null);

        s.TimeZoneId = zone;
        await _sut.UpdateSettingsAsync(Tenant, s, null);

        (await StoredAsync()).TimeZoneId.Should().Be("Asia/Tokyo");
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
    public async Task GetSettings_ReturnsTheStoredInvoiceChoice_EvenWithServicesOff()
    {
        // The form keeps this choice while services are off; it isn't in effect (see GetBranding).
        var b = await _db.Businesses.SingleAsync();
        b.EnableServices = false;
        await _db.SaveChangesAsync();

        var s = await _sut.GetSettingsAsync(Tenant);

        s.EnableServices.Should().BeFalse();
        s.EnableInvoices.Should().BeTrue();
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
        b.Phone.Should().Be("+38970123456", "phone numbers are stored in E.164");
        b.Address.Should().Be("Skopje");
        b.PrimaryColor.Should().Be("#abc");
        b.SecondaryColor.Should().Be("#11aa22");
        b.DefaultSlotMinutes.Should().Be(45);
        b.WorkDayStart.Should().Be(new TimeSpan(8, 0, 0));
        b.OpenSat.Should().BeTrue();
        b.LogoUrl.Should().Be("/uploads/logos/old.png", "the posted LogoUrl is ignored and no logo was uploaded");
    }

    [Theory]
    [InlineData(nameof(BusinessSettingsDTO.PrimaryColor), "zzz")]
    [InlineData(nameof(BusinessSettingsDTO.PrimaryColor), "#12345")]
    [InlineData(nameof(BusinessSettingsDTO.PrimaryColor), "#ggg")]
    [InlineData(nameof(BusinessSettingsDTO.SecondaryColor), "red")]
    public async Task UpdateSettings_InvalidColor_ThrowsForTheField_AndSavesNothing(string field, string color)
    {
        var s = ValidSettings();
        if (field == nameof(BusinessSettingsDTO.PrimaryColor)) s.PrimaryColor = color;
        else s.SecondaryColor = color;

        var act = () => _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be(field);
        (await StoredAsync()).Name.Should().Be("Old name");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateSettings_EmptyColor_IsStoredAsNull_SoTheDefaultIsUsed(string? color)
    {
        var s = ValidSettings();
        s.PrimaryColor = color;

        await _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await StoredAsync()).PrimaryColor.Should().BeNull();
    }

    [Fact]
    public async Task UpdateSettings_WithServicesOff_StoresTheInvoiceChoice_ButInvoicesAreNotInEffect()
    {
        var s = ValidSettings();
        s.EnableServices = false;
        s.EnableInvoices = true;

        await _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        var stored = await StoredAsync();
        stored.EnableInvoices.Should().BeTrue("the choice is kept for when services come back");
        stored.InvoicesEnabled().Should().BeFalse("invoices require services");
        (await _sut.GetBrandingAsync(Tenant))!.EnableInvoices.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateSettings_TurningServicesOffAndOnAgain_RestoresTheInvoiceChoice()
    {
        // As the form does it: with services off the invoice switch is hidden and the stored choice is posted back.
        var off = ValidSettings();
        off.EnableServices = false;
        off.EnableInvoices = (await _sut.GetSettingsAsync(Tenant)).EnableInvoices;
        await _sut.UpdateSettingsAsync(Tenant, off, logo: null);
        (await _sut.GetBrandingAsync(Tenant))!.EnableInvoices.Should().BeFalse();

        var on = ValidSettings();
        on.EnableServices = true;
        on.EnableInvoices = (await _sut.GetSettingsAsync(Tenant)).EnableInvoices;
        await _sut.UpdateSettingsAsync(Tenant, on, logo: null);

        (await _sut.GetBrandingAsync(Tenant))!.EnableInvoices.Should().BeTrue();
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@")]
    [InlineData("@b.com")]
    public async Task UpdateSettings_InvalidEmail_ThrowsForTheField_AndSavesNothing(string email)
    {
        var s = ValidSettings();
        s.Email = email;

        var act = () => _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.PropertyName.Should().Be(nameof(BusinessSettingsDTO.Email));
        (await StoredAsync()).Name.Should().Be("Old name");
    }

    [Fact]
    public async Task UpdateSettings_EmailTooLong_ThrowsForTheField()
    {
        var s = ValidSettings();
        s.Email = new string('a', BusinessService.MaxEmailLength) + "@test.com";

        var act = () => _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.PropertyName.Should().Be(nameof(BusinessSettingsDTO.Email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateSettings_EmptyEmail_IsStoredAsNull(string? email)
    {
        var s = ValidSettings();
        s.Email = email;

        await _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await StoredAsync()).Email.Should().BeNull();
    }

    [Fact]
    public async Task UpdateSettings_NoOpenDay_Throws_AndSavesNothing()
    {
        var s = ValidSettings();
        s.OpenMon = s.OpenTue = s.OpenWed = s.OpenThu = s.OpenFri = s.OpenSat = s.OpenSun = false;

        var act = () => _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .WithMessage("Choose at least one open day.")
            .Which.PropertyName.Should().BeNull("there is no single field, so it goes to the summary");
        (await StoredAsync()).Name.Should().Be("Old name");
    }

    [Fact]
    public async Task UpdateSettings_OnlySundayOpen_IsAllowed()
    {
        var s = ValidSettings();
        s.OpenMon = s.OpenTue = s.OpenWed = s.OpenThu = s.OpenFri = s.OpenSat = false;
        s.OpenSun = true;

        await _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await StoredAsync()).OpenSun.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateSettings_SlotLongerThanTheOpeningHours_ThrowsForTheField()
    {
        var s = ValidSettings();
        s.WorkDayStart = new TimeSpan(9, 0, 0);
        s.WorkDayEnd = new TimeSpan(10, 0, 0);
        s.DefaultSlotMinutes = 61;

        var act = () => _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.PropertyName.Should().Be(nameof(BusinessSettingsDTO.DefaultSlotMinutes));
        (await StoredAsync()).Name.Should().Be("Old name");
    }

    [Fact]
    public async Task UpdateSettings_SlotExactlyAsLongAsTheOpeningHours_IsAllowed()
    {
        var s = ValidSettings();
        s.WorkDayStart = new TimeSpan(9, 0, 0);
        s.WorkDayEnd = new TimeSpan(10, 0, 0);
        s.DefaultSlotMinutes = 60;

        await _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await StoredAsync()).DefaultSlotMinutes.Should().Be(60);
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
    public async Task UpdateSettings_InvalidPhone_ThrowsForTheField_AndSavesNothing()
    {
        var s = ValidSettings();
        s.Phone = "123";

        var act = () => _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.PropertyName.Should().Be(nameof(BusinessSettingsDTO.Phone));
        (await StoredAsync()).Name.Should().Be("Old name");
    }

    [Fact]
    public async Task UpdateSettings_EmptyPhone_IsStoredAsNull()
    {
        var s = ValidSettings();
        s.Phone = "  ";

        await _sut.UpdateSettingsAsync(Tenant, s, logo: null);

        (await StoredAsync()).Phone.Should().BeNull();
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

    // ---------------- logo uploads (S5) ----------------

    private const string OldLogo = "/uploads/logos/old.png";

    // The first bytes of each supported format, followed by some data.
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3];
    private static readonly byte[] JpegBytes = [0xFF, 0xD8, 0xFF, 0xE0, 0, 16, 1, 2, 3];
    private static readonly byte[] WebpBytes = [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WEBPVP8 "u8, 1, 2, 3];

    private static LogoUpload Upload(byte[] bytes, string fileName) => new(new MemoryStream(bytes), fileName, bytes.Length);

    /// <summary>Records what SaveAsync received and answers with a new, distinct URL each time.</summary>
    private List<(string Extension, byte[] Bytes)> CaptureSaves(string url = "/uploads/logos/new.png")
    {
        var saves = new List<(string, byte[])>();
        _logoStorage.Setup(s => s.SaveAsync(Tenant, It.IsAny<string>(), It.IsAny<Stream>()))
            .Callback<Guid, string, Stream>((_, ext, stream) =>
            {
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                saves.Add((ext, copy.ToArray()));
            })
            .ReturnsAsync(url);
        return saves;
    }

    private async Task AddInvoiceShowingAsync(Guid tenantId, string logoUrl)
    {
        if (!await _db.Businesses.AnyAsync(b => b.Id == tenantId))
            _db.Businesses.Add(new Business { Id = tenantId, Name = "Other business" });

        _db.Invoices.Add(new Invoice
        {
            Id = Guid.NewGuid(), TenantId = tenantId, AppointmentId = Guid.NewGuid(), CustomerId = Guid.NewGuid(),
            InvoiceNumber = $"INV-2026-{Guid.NewGuid():N}", InvoiceYear = 2026, InvoiceSequence = 1,
            BusinessNameSnapshot = "Old name", CustomerNameSnapshot = "Ana Doe", BusinessLogoSnapshot = logoUrl
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Theory]
    [InlineData("png", "Logo.PNG", ".png")]
    [InlineData("jpeg", "logo.jpg", ".jpg")]
    [InlineData("jpeg", "logo.jpeg", ".jpg")]
    [InlineData("webp", "logo.webp", ".webp")]
    public async Task UpdateSettings_WithARealImage_StoresItsBytes_AndSavesTheUrl(string format, string fileName, string storedAs)
    {
        var bytes = format switch { "png" => PngBytes, "jpeg" => JpegBytes, _ => WebpBytes };
        var saves = CaptureSaves();
        _logoStorage.Setup(s => s.DeleteAsync(OldLogo)).Returns(Task.CompletedTask);

        await _sut.UpdateSettingsAsync(Tenant, ValidSettings(), Upload(bytes, fileName));

        saves.Should().ContainSingle();
        saves[0].Extension.Should().Be(storedAs);
        saves[0].Bytes.Should().Equal(bytes);
        (await StoredAsync()).LogoUrl.Should().Be("/uploads/logos/new.png");
    }

    [Fact]
    public async Task UpdateSettings_StoredExtension_ComesFromTheContent_NotTheName()
    {
        var saves = CaptureSaves();
        _logoStorage.Setup(s => s.DeleteAsync(OldLogo)).Returns(Task.CompletedTask);

        await _sut.UpdateSettingsAsync(Tenant, ValidSettings(), Upload(JpegBytes, "logo.png"));

        saves.Single().Extension.Should().Be(".jpg");
    }

    public static TheoryData<string, byte[]> NotAnImage => new()
    {
        { "page.png", "<html><script>alert(1)</script></html>"u8.ToArray() },
        { "logo.png", "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray() },
        { "logo.jpg", "GIF89a\x01\x00\x01\x00"u8.ToArray() },               // a GIF renamed
        { "logo.webp", [.. "RIFF"u8, 0x24, 0, 0, 0, .. "WAVEfmt "u8] },      // RIFF, but audio
        { "logo.png", "MZ\x90\x00"u8.ToArray() },                            // an executable
        { "logo.png", [0x89, 0x50, 0x4E, 0x47] },                            // a truncated PNG signature
    };

    [Theory]
    [MemberData(nameof(NotAnImage))]
    public async Task UpdateSettings_FileWhoseContentIsNotAnImage_Throws_AndStoresNothing(string fileName, byte[] bytes)
    {
        var act = () => _sut.UpdateSettingsAsync(Tenant, ValidSettings(), Upload(bytes, fileName));

        await act.Should().ThrowExactlyAsync<BusinessRuleException>().WithMessage(BusinessService.InvalidLogoContentMessage);
        var stored = await StoredAsync();
        stored.Name.Should().Be("Old name");
        stored.LogoUrl.Should().Be(OldLogo);
        _logoStorage.Verify(s => s.SaveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>()), Times.Never);
    }

    [Fact]
    public async Task UpdateSettings_ContentLargerThanTheStatedLength_IsStillRefusedAsTooLarge()
    {
        // The stated length can't be trusted: the check counts what is actually read.
        var bytes = new byte[BusinessService.MaxLogoBytes + 1];
        PngBytes.CopyTo(bytes, 0);
        var logo = new LogoUpload(new MemoryStream(bytes), "logo.png", Length: 100);

        var act = () => _sut.UpdateSettingsAsync(Tenant, ValidSettings(), logo);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*too large*");
        _logoStorage.Verify(s => s.SaveAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Stream>()), Times.Never);
    }

    [Fact]
    public async Task Reupload_WhenAnInvoiceShowsThePreviousLogo_KeepsIt()
    {
        await AddInvoiceShowingAsync(Tenant, OldLogo);
        CaptureSaves();

        await _sut.UpdateSettingsAsync(Tenant, ValidSettings(), Upload(PngBytes, "logo.png"));

        (await StoredAsync()).LogoUrl.Should().Be("/uploads/logos/new.png");
        // Explicit: the service swallows delete failures, so a strict mock alone would not catch a call.
        _logoStorage.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Reupload_WhenNoInvoiceShowsThePreviousLogo_DeletesIt()
    {
        CaptureSaves();
        _logoStorage.Setup(s => s.DeleteAsync(OldLogo)).Returns(Task.CompletedTask);

        await _sut.UpdateSettingsAsync(Tenant, ValidSettings(), Upload(PngBytes, "logo.png"));

        _logoStorage.Verify(s => s.DeleteAsync(OldLogo), Times.Once);
        _logoStorage.Verify(s => s.DeleteAsync("/uploads/logos/new.png"), Times.Never);
    }

    [Fact]
    public async Task Reupload_AnotherTenantsInvoiceWithTheSameUrl_DoesNotKeepThisTenantsLogo()
    {
        await AddInvoiceShowingAsync(Guid.NewGuid(), OldLogo);
        CaptureSaves();
        _logoStorage.Setup(s => s.DeleteAsync(OldLogo)).Returns(Task.CompletedTask);

        await _sut.UpdateSettingsAsync(Tenant, ValidSettings(), Upload(PngBytes, "logo.png"));

        _logoStorage.Verify(s => s.DeleteAsync(OldLogo), Times.Once);
    }

    [Fact]
    public async Task Reupload_WhenDeletingThePreviousLogoFails_TheSettingsAreStillSaved()
    {
        CaptureSaves();
        _logoStorage.Setup(s => s.DeleteAsync(OldLogo)).ThrowsAsync(new IOException("The file is in use."));

        await _sut.UpdateSettingsAsync(Tenant, ValidSettings(), Upload(PngBytes, "logo.png"));

        var stored = await StoredAsync();
        stored.Name.Should().Be("New name");
        stored.LogoUrl.Should().Be("/uploads/logos/new.png");
    }

    [Fact]
    public async Task SavingWithoutANewLogo_DeletesNothing()
    {
        await _sut.UpdateSettingsAsync(Tenant, ValidSettings(), logo: null);

        _logoStorage.Verify(s => s.DeleteAsync(It.IsAny<string>()), Times.Never);
        (await StoredAsync()).LogoUrl.Should().Be(OldLogo);
    }

    [Fact]
    public async Task WhenTheSettingsCantBeSaved_TheNewlyStoredLogoIsRemoved_AndTheOldOneKept()
    {
        var business = new Business
        {
            Id = Tenant, Name = "Old name", LogoUrl = OldLogo, DefaultSlotMinutes = 30,
            WorkDayStart = new TimeSpan(9, 0, 0), WorkDayEnd = new TimeSpan(17, 0, 0), OpenMon = true
        };
        var repository = new Mock<IBusinessRepository>();
        repository.Setup(r => r.GetByIdAsync(Tenant)).ReturnsAsync(business);
        repository.Setup(r => r.SaveChangesAsync()).ThrowsAsync(new InvalidOperationException("database down"));
        CaptureSaves("/uploads/logos/unsaved.png");
        _logoStorage.Setup(s => s.DeleteAsync("/uploads/logos/unsaved.png")).Returns(Task.CompletedTask);

        var sut = new BusinessService(repository.Object, null!, _logoStorage.Object, TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BusinessService>.Instance);
        var act = () => sut.UpdateSettingsAsync(Tenant, ValidSettings(), Upload(PngBytes, "logo.png"));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("database down");
        _logoStorage.Verify(s => s.DeleteAsync("/uploads/logos/unsaved.png"), Times.Once);
        _logoStorage.Verify(s => s.DeleteAsync(OldLogo), Times.Never);
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
    public async Task GetBranding_ReturnsNameLogoColorsAndSwitches()
    {
        var b = await _db.Businesses.SingleAsync();
        b.PrimaryColor = "#112233";
        b.SecondaryColor = "#ffffff";
        await _db.SaveChangesAsync();

        var branding = await _sut.GetBrandingAsync(Tenant);

        branding!.Name.Should().Be("Old name");
        branding.LogoUrl.Should().Be("/uploads/logos/old.png");
        branding.PrimaryColor.Should().Be("#112233");
        branding.SecondaryColor.Should().Be("#ffffff");
        branding.EnableServices.Should().BeTrue();
        branding.EnableInvoices.Should().BeTrue();
    }

    [Fact]
    public async Task GetBranding_InvoicesAreOff_WhenServicesAreOff()
    {
        var b = await _db.Businesses.SingleAsync();
        b.EnableServices = false;
        await _db.SaveChangesAsync();

        var branding = await _sut.GetBrandingAsync(Tenant);

        branding!.EnableServices.Should().BeFalse();
        branding.EnableInvoices.Should().BeFalse();
    }

    [Fact]
    public async Task GetBranding_ForUnknownTenant_ReturnsNull()
    {
        (await _sut.GetBrandingAsync(Guid.NewGuid())).Should().BeNull("the layout must not fail");
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
