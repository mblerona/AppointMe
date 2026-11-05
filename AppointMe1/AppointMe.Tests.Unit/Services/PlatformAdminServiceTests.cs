using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Implementation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

public class PlatformAdminServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"platform-admin-{Guid.NewGuid()}")
        .Options);

    private PlatformAdminService Service() =>
        new(new PlatformAdminRepository(_db), new BusinessRepository(_db), new FixedClock(Now));

    private async Task<Business> AddBusinessAsync(string name = "Biz", DateTime? createdAt = null, string? email = null)
    {
        var business = new Business { Id = Guid.NewGuid(), Name = name, CreatedAt = createdAt ?? Now, Email = email };
        _db.Businesses.Add(business);
        await _db.SaveChangesAsync();
        return business;
    }

    private async Task AddUserAsync(Guid? tenantId, string email, DateTime? createdAt = null)
    {
        _db.Users.Add(new AppointMeAppUser
        {
            Id = Guid.NewGuid().ToString(), UserName = email, Email = email, FirstName = "T", LastName = "U",
            TenantId = tenantId, CreatedAt = createdAt ?? Now
        });
        await _db.SaveChangesAsync();
    }

    private async Task AddUsageAsync(Guid tenantId, int customers, int appointments, int invoices)
    {
        var customerIds = Enumerable.Range(1, Math.Max(customers, 1)).Select(_ => Guid.NewGuid()).ToList();
        for (var i = 0; i < customers; i++)
        {
            _db.Customers.Add(new Customer
            {
                Id = customerIds[i], TenantId = tenantId, CustomerNumber = i + 1, FirstName = "Ana", LastName = "Doe",
                Email = $"{Guid.NewGuid():N}@test.com", PhoneNumber = "+38970123456", State = "MK", City = "Skopje"
            });
        }
        for (var i = 0; i < appointments; i++)
        {
            _db.Appointments.Add(new Appointment
            {
                Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerIds[0], OrderNumber = $"ORD-{i}",
                Description = "desc", AppointmentDate = Now
            });
        }
        for (var i = 0; i < invoices; i++)
        {
            _db.Invoices.Add(new Invoice
            {
                Id = Guid.NewGuid(), TenantId = tenantId, AppointmentId = Guid.NewGuid(), CustomerId = customerIds[0],
                InvoiceNumber = $"INV-{i}", InvoiceYear = 2026, InvoiceSequence = i + 1,
                CustomerNameSnapshot = "Ana Doe", BusinessNameSnapshot = "Biz"
            });
        }
        await _db.SaveChangesAsync();
    }

    // ---------------- list ----------------

    [Fact]
    public async Task List_ContainsEveryTenant_NewestFirst()
    {
        await AddBusinessAsync("Old", Now.AddDays(-10));
        await AddBusinessAsync("New", Now);
        await AddBusinessAsync("Middle", Now.AddDays(-5));

        var tenants = await Service().GetTenantsAsync(null, null);

        tenants.Select(t => t.Name).Should().ContainInOrder("New", "Middle", "Old");
    }

    [Fact]
    public async Task List_CountsBelongToEachTenantSeparately()
    {
        var first = await AddBusinessAsync("First");
        var second = await AddBusinessAsync("Second");
        var empty = await AddBusinessAsync("Empty");
        await AddUsageAsync(first.Id, customers: 2, appointments: 3, invoices: 1);
        await AddUsageAsync(second.Id, customers: 1, appointments: 0, invoices: 0);

        var tenants = await Service().GetTenantsAsync(null, null);

        tenants.Single(t => t.Id == first.Id).Should().BeEquivalentTo(
            new { CustomerCount = 2, AppointmentCount = 3, InvoiceCount = 1 });
        tenants.Single(t => t.Id == second.Id).Should().BeEquivalentTo(
            new { CustomerCount = 1, AppointmentCount = 0, InvoiceCount = 0 });
        tenants.Single(t => t.Id == empty.Id).Should().BeEquivalentTo(
            new { CustomerCount = 0, AppointmentCount = 0, InvoiceCount = 0 });
    }

    [Fact]
    public async Task OwnerEmail_IsTheOldestUser_AndNullWithoutUsers()
    {
        var withUsers = await AddBusinessAsync("With users");
        var withoutUsers = await AddBusinessAsync("Without users");
        await AddUserAsync(withUsers.Id, "second@test.com", Now);
        await AddUserAsync(withUsers.Id, "owner@test.com", Now.AddDays(-1));

        var tenants = await Service().GetTenantsAsync(null, null);

        tenants.Single(t => t.Id == withUsers.Id).OwnerEmail.Should().Be("owner@test.com");
        tenants.Single(t => t.Id == withoutUsers.Id).OwnerEmail.Should().BeNull();
    }

    [Fact]
    public async Task PlatformAdminWithoutTenant_IsNotListedAsAnOwner()
    {
        var business = await AddBusinessAsync();
        await AddUserAsync(null, "admin@test.com", Now.AddYears(-1));

        var tenant = (await Service().GetTenantsAsync(null, null)).Single(t => t.Id == business.Id);

        tenant.OwnerEmail.Should().BeNull();
    }

    [Theory]
    [InlineData("bella")]
    [InlineData("BELLA")]
    [InlineData("  Bella  ")]
    [InlineData("contact@bella")]
    [InlineData("OWNER@BELLA")]
    [InlineData("staff@bella")]
    public async Task Search_MatchesNameBusinessEmailOrAnyUserEmail_IgnoringCase(string term)
    {
        var bella = await AddBusinessAsync("Bella Salon", email: "contact@bella.test");
        var other = await AddBusinessAsync("Other", email: "contact@other.test");
        await AddUserAsync(bella.Id, "owner@bella.test", Now.AddDays(-1));
        await AddUserAsync(bella.Id, "staff@bella.test");
        await AddUserAsync(other.Id, "owner@other.test");

        var tenants = await Service().GetTenantsAsync(term, null);

        tenants.Should().ContainSingle().Which.Id.Should().Be(bella.Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptySearch_ReturnsEveryTenant(string? term)
    {
        await AddBusinessAsync("One");
        await AddBusinessAsync("Two");

        (await Service().GetTenantsAsync(term, null)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_WithNoMatch_ReturnsNothing()
    {
        await AddBusinessAsync("One");

        (await Service().GetTenantsAsync("zzz", null)).Should().BeEmpty();
    }

    [Fact]
    public async Task StatusFilter_FindsActive_AndNoSuspendedBeforeSuspensionExists()
    {
        await AddBusinessAsync("One");

        var service = Service();
        (await service.GetTenantsAsync(null, BusinessStatus.Active)).Should().ContainSingle()
            .Which.Status.Should().Be(BusinessStatus.Active);
        (await service.GetTenantsAsync(null, BusinessStatus.Suspended)).Should().BeEmpty();
    }

    [Fact]
    public async Task Dates_AreConvertedToSkopjeTime()
    {
        // 23:30 UTC in January is 00:30 the next day in Skopje (UTC+1).
        var business = await AddBusinessAsync(createdAt: new DateTime(2026, 1, 1, 23, 30, 0));

        var service = Service();
        var expected = new DateTime(2026, 1, 2, 0, 30, 0);
        (await service.GetTenantsAsync(null, null)).Single().CreatedAt.Should().Be(expected);
        (await service.GetTenantAsync(business.Id)).CreatedAt.Should().Be(expected);
    }

    // ---------------- details ----------------

    [Fact]
    public async Task Details_IncludeContactInformationAndCounts()
    {
        var business = await AddBusinessAsync("Bella", email: "contact@bella.test");
        business.Phone = "+38970123456";
        business.Address = "Skopje";
        await _db.SaveChangesAsync();
        await AddUserAsync(business.Id, "owner@bella.test");
        await AddUsageAsync(business.Id, customers: 1, appointments: 2, invoices: 0);

        var tenant = await Service().GetTenantAsync(business.Id);

        tenant.Should().BeEquivalentTo(new
        {
            Name = "Bella", OwnerEmail = "owner@bella.test", Email = "contact@bella.test", Phone = "+38970123456",
            Address = "Skopje", CustomerCount = 1, AppointmentCount = 2, InvoiceCount = 0, Status = BusinessStatus.Active
        });
    }

    [Fact]
    public async Task Details_OfAnUnknownTenant_Throw()
    {
        var act = () => Service().GetTenantAsync(Guid.NewGuid());

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ---------------- totals ----------------

    [Fact]
    public async Task Totals_CountAllTenants_AndThoseFromTheLast30Days()
    {
        await AddBusinessAsync("Today", Now);
        await AddBusinessAsync("Exactly 30 days ago", Now.AddDays(-30));
        await AddBusinessAsync("31 days ago", Now.AddDays(-31));

        var totals = await Service().GetTotalsAsync();

        totals.Should().BeEquivalentTo(new PlatformTotalsDTO
        {
            TotalTenants = 3, ActiveTenants = 3, SuspendedTenants = 0, NewTenantsLast30Days = 2
        });
    }

    [Fact]
    public async Task Totals_WithNoTenants_AreZero()
    {
        (await Service().GetTotalsAsync()).Should().BeEquivalentTo(new PlatformTotalsDTO());
    }

    // ---------------- edit ----------------

    [Fact]
    public async Task Update_SavesTheFourFields_NormalizedAndTrimmed_AndSetsUpdatedAt()
    {
        var business = await AddBusinessAsync(createdAt: Now.AddDays(-1));

        await Service().UpdateTenantAsync(business.Id, new UpdateTenantDTO
        {
            Name = "  New name  ", Email = "  new@test.com ", Phone = "070 123 456", Address = "  Street 1  "
        });

        var saved = await _db.Businesses.AsNoTracking().SingleAsync(b => b.Id == business.Id);
        saved.Name.Should().Be("New name");
        saved.Email.Should().Be("new@test.com");
        saved.Phone.Should().Be("+38970123456");
        saved.Address.Should().Be("Street 1");
        saved.UpdatedAt.Should().Be(Now);
    }

    [Fact]
    public async Task Update_LeavesEverythingElseAlone()
    {
        var business = await AddBusinessAsync(createdAt: Now.AddDays(-1));
        business.PrimaryColor = "#112233";
        business.DefaultSlotMinutes = 45;
        business.TimeZoneId = "Asia/Tokyo";
        business.EnableInvoices = false;
        await _db.SaveChangesAsync();

        await Service().UpdateTenantAsync(business.Id, new UpdateTenantDTO { Name = "New name" });

        var saved = await _db.Businesses.AsNoTracking().SingleAsync(b => b.Id == business.Id);
        saved.PrimaryColor.Should().Be("#112233");
        saved.DefaultSlotMinutes.Should().Be(45);
        saved.TimeZoneId.Should().Be("Asia/Tokyo");
        saved.EnableInvoices.Should().BeFalse();
        saved.CreatedAt.Should().Be(Now.AddDays(-1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Update_ClearsOptionalFields(string? blank)
    {
        var business = await AddBusinessAsync(email: "old@test.com");
        business.Phone = "+38970123456";
        business.Address = "Old";
        await _db.SaveChangesAsync();

        await Service().UpdateTenantAsync(business.Id, new UpdateTenantDTO
        {
            Name = "Biz", Email = blank, Phone = blank, Address = blank
        });

        var saved = await _db.Businesses.AsNoTracking().SingleAsync(b => b.Id == business.Id);
        saved.Email.Should().BeNull();
        saved.Phone.Should().BeNull();
        saved.Address.Should().BeNull();
    }

    public static TheoryData<UpdateTenantDTO, string> InvalidUpdates => new()
    {
        { new UpdateTenantDTO { Name = null }, nameof(UpdateTenantDTO.Name) },
        { new UpdateTenantDTO { Name = "   " }, nameof(UpdateTenantDTO.Name) },
        { new UpdateTenantDTO { Name = new string('x', BusinessService.MaxBusinessNameLength + 1) }, nameof(UpdateTenantDTO.Name) },
        { new UpdateTenantDTO { Name = "Ok", Email = "not-an-email" }, nameof(UpdateTenantDTO.Email) },
        { new UpdateTenantDTO { Name = "Ok", Email = new string('x', PlatformAdminService.MaxEmailLength) + "@test.com" }, nameof(UpdateTenantDTO.Email) },
        { new UpdateTenantDTO { Name = "Ok", Phone = "abc" }, nameof(UpdateTenantDTO.Phone) },
        { new UpdateTenantDTO { Name = "Ok", Address = new string('x', PlatformAdminService.MaxAddressLength + 1) }, nameof(UpdateTenantDTO.Address) }
    };

    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public async Task Update_WithAnInvalidValue_NamesTheField_AndSavesNothing(UpdateTenantDTO update, string field)
    {
        var business = await AddBusinessAsync("Original", email: "original@test.com");

        var act = () => Service().UpdateTenantAsync(business.Id, update);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be(field);
        var saved = await _db.Businesses.AsNoTracking().SingleAsync(b => b.Id == business.Id);
        saved.Name.Should().Be("Original");
        saved.Email.Should().Be("original@test.com");
    }

    [Fact]
    public async Task Update_OfAnUnknownTenant_Throws()
    {
        var act = () => Service().UpdateTenantAsync(Guid.NewGuid(), new UpdateTenantDTO { Name = "New" });

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
