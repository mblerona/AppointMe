using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using AppointMe.Service.Pdf;
using AppointMe.Web.Infrastructure;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AppointMe.Tests.Web.Infrastructure;

/// <summary>
/// S5, end to end with the real services and the real file storage (in a temporary folder, never wwwroot):
/// a new upload gets a new file, invoices keep the logo they were issued with, and only a logo
/// no invoice shows is deleted.
/// </summary>
public sealed class LogoReuploadTests : IDisposable
{
    private sealed class TempEnvironment(string webRoot) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = webRoot;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Test";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = webRoot;
        public string EnvironmentName { get; set; } = "Testing";
    }

    /// <summary>Records the logo bytes each PDF was given.</summary>
    private sealed class CapturingPdfGenerator : IInvoicePdfGenerator
    {
        public byte[]? LastLogo { get; private set; }

        public byte[] Generate(InvoiceDTO invoice, DateTime generatedAt, byte[]? logo)
        {
            LastLogo = logo;
            return [1];
        }
    }

    // Three different "images": a PNG signature followed by a marker byte.
    private static byte[] Png(byte marker) => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, marker];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"logo-reupload-{Guid.NewGuid():N}");
    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"logo-reupload-{Guid.NewGuid()}")
            // See TenantContextTests: this class can run after more than 20 web factories have started.
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options);

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _serviceId = Guid.NewGuid();
    private readonly WebRootLogoStorage _storage;
    private readonly CapturingPdfGenerator _pdf = new();
    private readonly BusinessService _businesses;
    private readonly InvoiceService _invoices;

    public LogoReuploadTests()
    {
        _storage = new WebRootLogoStorage(new TempEnvironment(_root));
        _businesses = new BusinessService(new BusinessRepository(_db), null!, _storage, TimeProvider.System,
            NullLogger<BusinessService>.Instance);
        _invoices = new InvoiceService(new InvoiceRepository(_db), new AppointmentRepository(_db), new BusinessRepository(_db),
            TimeProvider.System, _storage, _pdf);

        _db.Businesses.Add(new Business
        {
            Id = _tenant, Name = "Salon", EnableServices = true, EnableInvoices = true, DefaultSlotMinutes = 30,
            WorkDayStart = new TimeSpan(9, 0, 0), WorkDayEnd = new TimeSpan(17, 0, 0),
            OpenMon = true, OpenTue = true, OpenWed = true, OpenThu = true, OpenFri = true
        });
        _db.Customers.Add(new Customer
        {
            Id = _customerId, TenantId = _tenant, CustomerNumber = 1, FirstName = "Ana", LastName = "Doe",
            Email = "ana@test.com", PhoneNumber = "+38970123456", State = "MK", City = "Skopje"
        });
        _db.ServiceOfferings.Add(new ServiceOffering { Id = _serviceId, BusinessId = _tenant, Name = "Haircut", Price = 100, IsActive = true });
        _db.SaveChanges();
    }

    private string LogosDir => Path.Combine(_root, "uploads", "logos");

    private string[] StoredFiles() =>
        Directory.Exists(LogosDir) ? Directory.GetFiles(LogosDir).Select(f => Path.GetFileName(f)).ToArray() : [];

    private async Task<string> UploadAsync(byte[] bytes)
    {
        var settings = await _businesses.GetSettingsAsync(_tenant);
        await _businesses.UpdateSettingsAsync(_tenant, settings, new LogoUpload(new MemoryStream(bytes), "logo.png", bytes.Length));
        _db.ChangeTracker.Clear();
        return (await _businesses.GetSettingsAsync(_tenant)).LogoUrl!;
    }

    /// <summary>An appointment with a service, invoiced through the real InvoiceService (which takes the logo snapshot).</summary>
    private async Task<Guid> InvoiceAnAppointmentAsync(string orderNumber)
    {
        var appointment = new Appointment
        {
            Id = Guid.NewGuid(), TenantId = _tenant, CustomerId = _customerId, OrderNumber = orderNumber,
            Description = "desc", DurationMinutes = 30, AppointmentDate = new DateTime(2026, 12, 1, 10, 0, 0)
        };
        appointment.AppointmentServices.Add(new AppointmentServiceModel
        {
            AppointmentId = appointment.Id, ServiceOfferingId = _serviceId, PriceAtBooking = 100
        });
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        return (await _invoices.CreateOrGetForAppointmentAsync(appointment.Id, _tenant)).Id;
    }

    private async Task<byte[]?> PdfLogoOfAsync(Guid invoiceId)
    {
        await _invoices.GeneratePdfAsync(invoiceId, _tenant);
        return _pdf.LastLogo;
    }

    [Fact]
    public async Task Reupload_OldInvoicesKeepTheirLogo_NewInvoicesGetTheNewOne()
    {
        var first = await UploadAsync(Png(1));
        var oldInvoice = await InvoiceAnAppointmentAsync("ORD-1");

        var second = await UploadAsync(Png(2));
        var newInvoice = await InvoiceAnAppointmentAsync("ORD-2");

        second.Should().NotBe(first, "every upload gets a new file name");
        (await PdfLogoOfAsync(oldInvoice)).Should().Equal(Png(1));
        (await PdfLogoOfAsync(newInvoice)).Should().Equal(Png(2));
        StoredFiles().Should().HaveCount(2);
    }

    [Fact]
    public async Task Reupload_DeletesThePreviousLogo_OnlyWhenNoInvoiceShowsIt()
    {
        var first = await UploadAsync(Png(1));    // never invoiced
        var second = await UploadAsync(Png(2));   // replaces the first, which is deleted
        StoredFiles().Should().ContainSingle();
        (await _storage.ReadAsync(first)).Should().BeNull();

        await InvoiceAnAppointmentAsync("ORD-1"); // now an invoice shows the second
        var third = await UploadAsync(Png(3));

        (await _storage.ReadAsync(second)).Should().Equal(Png(2), "an invoice still shows it");
        (await _storage.ReadAsync(third)).Should().Equal(Png(3));
        StoredFiles().Should().HaveCount(2);
    }

    [Fact]
    public async Task UploadThatIsNotAnImage_ChangesNothing_AndStoresNoFile()
    {
        var first = await UploadAsync(Png(1));

        var act = () => UploadAsync("<html></html>"u8.ToArray());

        await act.Should().ThrowAsync<AppointMe.Domain.Exceptions.BusinessRuleException>();
        _db.ChangeTracker.Clear();
        (await _businesses.GetSettingsAsync(_tenant)).LogoUrl.Should().Be(first);
        StoredFiles().Should().ContainSingle();
    }

    [Fact]
    public async Task TenantDeletion_RemovesEveryLogoFile_IncludingOnesInvoicesShowed()
    {
        await UploadAsync(Png(1));
        await InvoiceAnAppointmentAsync("ORD-1");
        await UploadAsync(Png(2));
        StoredFiles().Should().HaveCount(2);

        // What PlatformAdminService calls after the tenant's rows are deleted.
        await _storage.DeleteAllAsync(_tenant);

        StoredFiles().Should().BeEmpty();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
