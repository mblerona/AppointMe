using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Admin;
using AppointMe.Service.Email;
using AppointMe.Service.Implementation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
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

    private readonly Mock<IEmailService> _email = new();

    private PlatformAdminService Service(string? supportEmail = "support@test.com") => new(
        new PlatformAdminRepository(_db), new BusinessRepository(_db), _email.Object,
        Mock.Of<AppointMe.Service.Interface.ILogoStorage>(),
        Options.Create(new SupportOptions { Email = supportEmail }), new FixedClock(Now),
        NullLogger<PlatformAdminService>.Instance);

    private List<EmailMessage> CapturedEmails()
    {
        var sent = new List<EmailMessage>();
        _email.Setup(e => e.SendEmailAsync(It.IsAny<EmailMessage>()))
            .Callback<EmailMessage>(sent.Add)
            .Returns(Task.CompletedTask);
        return sent;
    }

    private async Task<Business> SuspendedBusinessAsync(string name = "Biz", string? email = null)
    {
        var business = await AddBusinessAsync(name, Now.AddDays(-1), email);
        business.Status = BusinessStatus.Suspended;
        business.SuspendedAt = Now.AddHours(-1);
        business.SuspensionReason = SuspensionReason.SecurityConcern;
        business.SuspensionNote = "Old note";
        await _db.SaveChangesAsync();
        return business;
    }

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
    public async Task StatusFilter_FindsOnlyThatStatus()
    {
        var active = await AddBusinessAsync("Active one");
        var suspended = await SuspendedBusinessAsync("Suspended one");

        var service = Service();
        (await service.GetTenantsAsync(null, BusinessStatus.Active)).Should().ContainSingle()
            .Which.Id.Should().Be(active.Id);
        var found = (await service.GetTenantsAsync(null, BusinessStatus.Suspended)).Should().ContainSingle().Which;
        found.Id.Should().Be(suspended.Id);
        found.Status.Should().Be(BusinessStatus.Suspended);
        (await service.GetTenantsAsync(null, null)).Should().HaveCount(2);
    }

    [Fact]
    public async Task StatusFilter_AndSearch_WorkTogether()
    {
        await SuspendedBusinessAsync("Bella");
        await AddBusinessAsync("Bella active");
        await SuspendedBusinessAsync("Other");

        var found = await Service().GetTenantsAsync("bella", BusinessStatus.Suspended);

        found.Should().ContainSingle().Which.Name.Should().Be("Bella");
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
    public async Task Totals_SeparateActiveFromSuspended()
    {
        await AddBusinessAsync("Active");
        await SuspendedBusinessAsync("Suspended");

        var totals = await Service().GetTotalsAsync();

        totals.TotalTenants.Should().Be(2);
        totals.ActiveTenants.Should().Be(1);
        totals.SuspendedTenants.Should().Be(1);
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
        }, "admin@test.com");

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

        await Service().UpdateTenantAsync(business.Id, new UpdateTenantDTO { Name = "New name" }, "admin@test.com");

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
        }, "admin@test.com");

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

        var act = () => Service().UpdateTenantAsync(business.Id, update, "admin@test.com");

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be(field);
        var saved = await _db.Businesses.AsNoTracking().SingleAsync(b => b.Id == business.Id);
        saved.Name.Should().Be("Original");
        saved.Email.Should().Be("original@test.com");
        (await _db.AdminActionLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Update_OfAnUnknownTenant_Throws()
    {
        var act = () => Service().UpdateTenantAsync(Guid.NewGuid(), new UpdateTenantDTO { Name = "New" }, "admin@test.com");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Update_LogsTheChangedFieldNames_WithoutValues()
    {
        var business = await AddBusinessAsync("Old name", email: "old@test.com");

        await Service().UpdateTenantAsync(business.Id, new UpdateTenantDTO
        {
            Name = "New name", Email = "old@test.com", Address = "New address"
        }, "admin@test.com");

        var entry = await _db.AdminActionLogs.SingleAsync();
        entry.Action.Should().Be(AdminActionType.TenantEdited);
        entry.ChangedFields.Should().Be("Name, Address");
        entry.BusinessId.Should().Be(business.Id);
        entry.BusinessName.Should().Be("New name");
        entry.AdminEmail.Should().Be("admin@test.com");
        entry.At.Should().Be(Now);
        entry.Note.Should().BeNull();
    }

    [Fact]
    public async Task Update_WithNoChange_LogsNothing_AndKeepsUpdatedAt()
    {
        var business = await AddBusinessAsync("Same", email: "same@test.com");
        business.UpdatedAt = Now.AddDays(-5);
        await _db.SaveChangesAsync();

        await Service().UpdateTenantAsync(business.Id, new UpdateTenantDTO { Name = " Same ", Email = "same@test.com" }, "admin@test.com");

        (await _db.AdminActionLogs.CountAsync()).Should().Be(0);
        (await _db.Businesses.AsNoTracking().SingleAsync()).UpdatedAt.Should().Be(Now.AddDays(-5));
    }

    // ---------------- suspend ----------------

    [Fact]
    public async Task Suspend_SetsTheFields_AndLogsInTheSameSave()
    {
        var business = await AddBusinessAsync();
        await AddUserAsync(business.Id, "owner@test.com");
        CapturedEmails();

        var outcome = await Service().SuspendTenantAsync(business.Id, SuspensionReason.Other, "  A note  ", "admin@test.com");

        outcome.Should().Be(TenantEmailOutcome.Sent);
        var saved = await _db.Businesses.AsNoTracking().SingleAsync();
        saved.Status.Should().Be(BusinessStatus.Suspended);
        saved.SuspendedAt.Should().Be(Now);
        saved.SuspensionReason.Should().Be(SuspensionReason.Other);
        saved.SuspensionNote.Should().Be("A note");
        saved.UpdatedAt.Should().Be(Now);

        var entry = await _db.AdminActionLogs.AsNoTracking().SingleAsync();
        entry.Should().BeEquivalentTo(new
        {
            Action = AdminActionType.TenantSuspended, BusinessId = business.Id, BusinessName = "Biz",
            AdminEmail = "admin@test.com", Reason = "Other", Note = "A note", At = Now
        });
    }

    [Fact]
    public async Task Suspend_DoesNotTouchOtherTenants()
    {
        var target = await AddBusinessAsync("Target");
        var other = await AddBusinessAsync("Other");
        CapturedEmails();

        await Service().SuspendTenantAsync(target.Id, SuspensionReason.SecurityConcern, null, "admin@test.com");

        (await _db.Businesses.AsNoTracking().SingleAsync(b => b.Id == other.Id)).Status.Should().Be(BusinessStatus.Active);
    }

    public static TheoryData<SuspensionReason?, string?> InvalidSuspensions => new()
    {
        { null, null },
        { (SuspensionReason)99, "note" },
        { SuspensionReason.Other, null },
        { SuspensionReason.Other, "" },
        { SuspensionReason.Other, "   " },
        { SuspensionReason.SecurityConcern, new string('x', AdminActionLog.MaxNoteLength + 1) }
    };

    [Theory]
    [MemberData(nameof(InvalidSuspensions))]
    public async Task Suspend_WithAnInvalidReasonOrNote_ChangesNothing(SuspensionReason? reason, string? note)
    {
        var business = await AddBusinessAsync();

        var validate = () => Service().ValidateSuspension(reason, note);
        validate.Should().Throw<BusinessRuleException>();

        var act = () => Service().SuspendTenantAsync(business.Id, reason, note, "admin@test.com");
        await act.Should().ThrowAsync<BusinessRuleException>();

        (await _db.Businesses.AsNoTracking().SingleAsync()).Status.Should().Be(BusinessStatus.Active);
        (await _db.AdminActionLogs.CountAsync()).Should().Be(0);
        _email.Verify(e => e.SendEmailAsync(It.IsAny<EmailMessage>()), Times.Never);
    }

    [Theory]
    [InlineData(SuspensionReason.AbuseOfEmailNotifications, "Abuse of email notifications")]
    [InlineData(SuspensionReason.FraudulentOrFakeBusiness, "Fraudulent or fake business")]
    [InlineData(SuspensionReason.ViolationOfTermsOfUse, "Violation of the terms of use")]
    [InlineData(SuspensionReason.SecurityConcern, "Security concern")]
    public async Task Suspend_WithAListedReason_NeedsNoNote_AndUsesItsText(SuspensionReason reason, string text)
    {
        var business = await AddBusinessAsync();
        await AddUserAsync(business.Id, "owner@test.com");
        var sent = CapturedEmails();

        await Service().SuspendTenantAsync(business.Id, reason, null, "admin@test.com");

        sent.Single().Content.Should().Contain(text);
        (await _db.AdminActionLogs.SingleAsync()).Reason.Should().Be(text);
    }

    [Fact]
    public async Task Suspend_WhenAlreadySuspended_IsRefused_AndKeepsTheOriginalReason()
    {
        var business = await SuspendedBusinessAsync();

        var act = () => Service().SuspendTenantAsync(business.Id, SuspensionReason.Other, "New", "admin@test.com");

        await act.Should().ThrowAsync<BusinessRuleException>();
        var saved = await _db.Businesses.AsNoTracking().SingleAsync();
        saved.SuspensionReason.Should().Be(SuspensionReason.SecurityConcern);
        saved.SuspensionNote.Should().Be("Old note");
        (await _db.AdminActionLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Suspend_OfAnUnknownTenant_Throws()
    {
        var act = () => Service().SuspendTenantAsync(Guid.NewGuid(), SuspensionReason.SecurityConcern, null, "admin@test.com");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ---------------- emails ----------------

    [Fact]
    public async Task SuspensionEmail_GoesToEveryUser_WithReasonNoteAndSupport()
    {
        var business = await AddBusinessAsync("Bella", email: "contact@bella.test");
        await AddUserAsync(business.Id, "owner@bella.test");
        await AddUserAsync(business.Id, "staff@bella.test");
        var sent = CapturedEmails();

        await Service().SuspendTenantAsync(business.Id, SuspensionReason.SecurityConcern, "Call us", "admin@test.com");

        sent.Select(m => m.MailTo).Should().BeEquivalentTo("owner@bella.test", "staff@bella.test");
        var message = sent.First();
        message.Subject.Should().Contain("suspended");
        message.IsHtml.Should().BeFalse();
        message.Content.Should().Contain("Bella").And.Contain("Security concern").And.Contain("Call us").And.Contain("support@test.com");
        message.Content.Should().NotContain("admin@test.com");
    }

    [Fact]
    public async Task SuspensionEmail_WithoutASupportAddress_StillMentionsSupport()
    {
        var business = await AddBusinessAsync();
        await AddUserAsync(business.Id, "owner@test.com");
        var sent = CapturedEmails();

        await Service(supportEmail: null).SuspendTenantAsync(business.Id, SuspensionReason.SecurityConcern, null, "admin@test.com");

        sent.Single().Content.Should().Contain("contact support");
    }

    [Fact]
    public async Task Emails_WithoutUsers_GoToTheBusinessEmail()
    {
        var business = await AddBusinessAsync(email: "contact@test.com");
        var sent = CapturedEmails();

        var outcome = await Service().SuspendTenantAsync(business.Id, SuspensionReason.SecurityConcern, null, "admin@test.com");

        outcome.Should().Be(TenantEmailOutcome.Sent);
        sent.Single().MailTo.Should().Be("contact@test.com");
    }

    [Fact]
    public async Task Emails_WithNoAddressAtAll_ReportThat_AndTheActionStands()
    {
        var business = await AddBusinessAsync();

        var outcome = await Service().SuspendTenantAsync(business.Id, SuspensionReason.SecurityConcern, null, "admin@test.com");

        outcome.Should().Be(TenantEmailOutcome.NoRecipient);
        (await _db.Businesses.AsNoTracking().SingleAsync()).Status.Should().Be(BusinessStatus.Suspended);
    }

    [Fact]
    public async Task AFailedEmail_ReportsThat_AndTheActionAndLogStand()
    {
        var business = await AddBusinessAsync();
        await AddUserAsync(business.Id, "owner@test.com");
        _email.Setup(e => e.SendEmailAsync(It.IsAny<EmailMessage>())).ThrowsAsync(new InvalidOperationException("down"));

        var outcome = await Service().SuspendTenantAsync(business.Id, SuspensionReason.SecurityConcern, null, "admin@test.com");

        outcome.Should().Be(TenantEmailOutcome.Failed);
        (await _db.Businesses.AsNoTracking().SingleAsync()).Status.Should().Be(BusinessStatus.Suspended);
        (await _db.AdminActionLogs.CountAsync()).Should().Be(1);
    }

    // ---------------- reactivate ----------------

    [Fact]
    public async Task Reactivate_ClearsTheSuspension_LogsTheNote_AndEmailsWithoutIt()
    {
        var business = await SuspendedBusinessAsync();
        await AddUserAsync(business.Id, "owner@test.com");
        var sent = CapturedEmails();

        var outcome = await Service().ReactivateTenantAsync(business.Id, "  Resolved  ", "admin@test.com");

        outcome.Should().Be(TenantEmailOutcome.Sent);
        var saved = await _db.Businesses.AsNoTracking().SingleAsync();
        saved.Status.Should().Be(BusinessStatus.Active);
        saved.SuspendedAt.Should().BeNull();
        saved.SuspensionReason.Should().BeNull();
        saved.SuspensionNote.Should().BeNull();

        var entry = await _db.AdminActionLogs.AsNoTracking().SingleAsync();
        entry.Action.Should().Be(AdminActionType.TenantReactivated);
        entry.Note.Should().Be("Resolved");
        entry.Reason.Should().BeNull();

        sent.Single().Subject.Should().Contain("reactivated");
        sent.Single().Content.Should().NotContain("Resolved");
    }

    [Fact]
    public async Task Reactivate_WhenActive_IsRefused()
    {
        var business = await AddBusinessAsync();

        var act = () => Service().ReactivateTenantAsync(business.Id, null, "admin@test.com");

        await act.Should().ThrowAsync<BusinessRuleException>();
        (await _db.AdminActionLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Reactivate_WithANoteThatIsTooLong_ChangesNothing()
    {
        var business = await SuspendedBusinessAsync();

        var act = () => Service().ReactivateTenantAsync(business.Id, new string('x', AdminActionLog.MaxNoteLength + 1), "admin@test.com");

        await act.Should().ThrowAsync<BusinessRuleException>();
        (await _db.Businesses.AsNoTracking().SingleAsync()).Status.Should().Be(BusinessStatus.Suspended);
    }

    [Fact]
    public async Task Reactivate_OfAnUnknownTenant_Throws()
    {
        var act = () => Service().ReactivateTenantAsync(Guid.NewGuid(), null, "admin@test.com");

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ---------------- reading the suspension and the log ----------------

    [Fact]
    public async Task Details_IncludeTheSuspensionAndThatTenantsLog_InSkopjeTime()
    {
        var business = await AddBusinessAsync("Target");
        var other = await AddBusinessAsync("Other");
        CapturedEmails();
        await Service().SuspendTenantAsync(business.Id, SuspensionReason.SecurityConcern, "Note", "admin@test.com");
        await Service().SuspendTenantAsync(other.Id, SuspensionReason.SecurityConcern, null, "admin@test.com");

        var tenant = await Service().GetTenantAsync(business.Id);

        // June: Skopje is UTC+2.
        var skopje = Now.AddHours(2);
        tenant.SuspendedAt.Should().Be(DateTime.SpecifyKind(skopje, DateTimeKind.Unspecified));
        tenant.SuspensionReason.Should().Be(SuspensionReason.SecurityConcern);
        tenant.SuspensionNote.Should().Be("Note");
        tenant.ActionLog.Should().ContainSingle().Which.At.Should().Be(DateTime.SpecifyKind(skopje, DateTimeKind.Unspecified));
    }

    [Fact]
    public async Task ActionLog_IsNewestFirst_RespectsTheLimit_AndSaysIfTheBusinessExists()
    {
        var existing = await AddBusinessAsync("Existing");
        var deletedId = Guid.NewGuid();
        _db.AdminActionLogs.AddRange(
            new AdminActionLog { Id = Guid.NewGuid(), At = Now.AddDays(-2), BusinessId = existing.Id, BusinessName = "Existing" },
            new AdminActionLog { Id = Guid.NewGuid(), At = Now, BusinessId = deletedId, BusinessName = "Deleted" },
            new AdminActionLog { Id = Guid.NewGuid(), At = Now.AddDays(-1), BusinessId = existing.Id, BusinessName = "Existing" });
        await _db.SaveChangesAsync();

        var log = await Service().GetActionLogAsync(2);

        log.Should().HaveCount(2);
        log[0].BusinessName.Should().Be("Deleted");
        log[0].BusinessExists.Should().BeFalse();
        log[1].At.Should().BeAfter(Now.AddDays(-2));
        log[1].BusinessExists.Should().BeTrue();
    }

    [Fact]
    public async Task SuspensionInfo_ForTheTenant_IsInTheirOwnTimeZone_AndNullWhenNotSuspended()
    {
        var business = await SuspendedBusinessAsync("Bella");
        business.TimeZoneId = "Asia/Tokyo";
        business.SuspensionReason = SuspensionReason.ViolationOfTermsOfUse;
        await _db.SaveChangesAsync();
        var active = await AddBusinessAsync("Active");
        var service = new BusinessService(
            new BusinessRepository(_db), null!, Mock.Of<AppointMe.Service.Interface.ILogoStorage>(), new FixedClock(Now));

        var info = await service.GetSuspensionInfoAsync(business.Id);

        info.Should().NotBeNull();
        info!.BusinessName.Should().Be("Bella");
        info.Reason.Should().Be("Violation of the terms of use");
        info.Note.Should().Be("Old note");
        info.SuspendedAt.Should().Be(new DateTime(2026, 6, 15, 20, 0, 0));
        (await service.GetSuspensionInfoAsync(active.Id)).Should().BeNull();
        (await service.GetSuspensionInfoAsync(Guid.NewGuid())).Should().BeNull();
    }
}
