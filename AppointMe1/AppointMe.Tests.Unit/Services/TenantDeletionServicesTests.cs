using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Repository.Interface;
using AppointMe.Service.Admin;
using AppointMe.Service.Email;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

/// <summary>The rules around deletion. The deletion of real rows is tested on SQL Server (TenantDeletionTests).</summary>
public class TenantDeletionServicesTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 22, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow);
    }

    private readonly ApplicationDbContext _db = new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseInMemoryDatabase($"tenant-deletion-{Guid.NewGuid()}")
        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
        .Options);

    private readonly Mock<IEmailService> _email = new();
    private readonly Mock<ILogoStorage> _logos = new();
    private readonly List<EmailMessage> _sent = new();

    public TenantDeletionServicesTests()
    {
        _email.Setup(e => e.SendEmailAsync(It.IsAny<EmailMessage>()))
            .Callback<EmailMessage>(_sent.Add)
            .Returns(Task.CompletedTask);
    }

    private PlatformAdminService Admin(IPlatformAdminRepository? repository = null) => new(
        repository ?? new PlatformAdminRepository(_db), new BusinessRepository(_db), _email.Object, _logos.Object,
        Options.Create(new SupportOptions { Email = "support@test.com" }), new FixedClock(Now),
        NullLogger<PlatformAdminService>.Instance);

    private TenantDeletionRequestService Requests(string? adminEmail = "admin@test.com") => new(
        new BusinessRepository(_db), _email.Object, Options.Create(new PlatformAdminOptions { Email = adminEmail }),
        new FixedClock(Now), NullLogger<TenantDeletionRequestService>.Instance);

    private async Task<Business> AddBusinessAsync(string name = "Bella Salon", bool requested = false, string? ownerEmail = "owner@test.com")
    {
        var business = new Business
        {
            Id = Guid.NewGuid(), Name = name, CreatedAt = Now.AddDays(-30),
            DeletionRequestedAt = requested ? Now.AddDays(-1) : null,
            DeletionRequestReason = requested ? "Closing down" : null
        };
        _db.Businesses.Add(business);
        if (ownerEmail != null)
        {
            _db.Users.Add(new AppointMeAppUser
            {
                Id = Guid.NewGuid().ToString(), UserName = $"{Guid.NewGuid():N}", Email = ownerEmail,
                FirstName = "T", LastName = "U", TenantId = business.Id
            });
        }
        await _db.SaveChangesAsync();
        return business;
    }

    private Task<Business?> ReloadAsync(Guid id) => _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);

    // ---------------- the tenant's request ----------------

    [Fact]
    public async Task Request_SavesIt_AndEmailsTheAdmin()
    {
        var business = await AddBusinessAsync();

        await Requests().RequestAsync(business.Id, "  Closing down  ");

        var saved = await ReloadAsync(business.Id);
        saved!.DeletionRequestedAt.Should().Be(Now);
        saved.DeletionRequestReason.Should().Be("Closing down");

        var message = _sent.Should().ContainSingle().Which;
        message.MailTo.Should().Be("admin@test.com");
        message.Content.Should().Contain("Bella Salon").And.Contain("Closing down");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Request_NeedsNoReason(string? reason)
    {
        var business = await AddBusinessAsync();

        await Requests().RequestAsync(business.Id, reason);

        var saved = await ReloadAsync(business.Id);
        saved!.DeletionRequestedAt.Should().NotBeNull();
        saved.DeletionRequestReason.Should().BeNull();
        _sent.Single().Content.Should().Contain("no reason given");
    }

    [Fact]
    public async Task Request_WithAReasonThatIsTooLong_IsRefused()
    {
        var business = await AddBusinessAsync();

        var act = () => Requests().RequestAsync(business.Id, new string('x', Business.MaxDeletionRequestReasonLength + 1));

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be("Reason");
        (await ReloadAsync(business.Id))!.DeletionRequestedAt.Should().BeNull();
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_Twice_IsRefused_AndKeepsTheFirst()
    {
        var business = await AddBusinessAsync(requested: true);

        var act = () => Requests().RequestAsync(business.Id, "Second");

        await act.Should().ThrowAsync<BusinessRuleException>();
        (await ReloadAsync(business.Id))!.DeletionRequestReason.Should().Be("Closing down");
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Request_WhenTheAdminEmailFails_IsStillSaved()
    {
        var business = await AddBusinessAsync();
        _email.Setup(e => e.SendEmailAsync(It.IsAny<EmailMessage>())).ThrowsAsync(new InvalidOperationException("down"));

        await Requests().RequestAsync(business.Id, null);

        (await ReloadAsync(business.Id))!.DeletionRequestedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Request_WithoutAnAdminEmailSetting_IsStillSaved()
    {
        var business = await AddBusinessAsync();

        await Requests(adminEmail: null).RequestAsync(business.Id, null);

        (await ReloadAsync(business.Id))!.DeletionRequestedAt.Should().NotBeNull();
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancel_ClearsTheRequest_AndLeavesOtherTenantsAlone()
    {
        var business = await AddBusinessAsync(requested: true);
        var other = await AddBusinessAsync("Other", requested: true, ownerEmail: null);

        await Requests().CancelAsync(business.Id);

        var saved = await ReloadAsync(business.Id);
        saved!.DeletionRequestedAt.Should().BeNull();
        saved.DeletionRequestReason.Should().BeNull();
        (await ReloadAsync(other.Id))!.DeletionRequestedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Cancel_WithoutARequest_IsRefused()
    {
        var business = await AddBusinessAsync();

        var act = () => Requests().CancelAsync(business.Id);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Fact]
    public async Task RequestAndCancel_OfAnUnknownTenant_Throw()
    {
        await FluentActions.Awaiting(() => Requests().RequestAsync(Guid.NewGuid(), null)).Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Awaiting(() => Requests().CancelAsync(Guid.NewGuid())).Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Settings_ShowTheRequestDate_InTheBusinessesOwnTimeZone()
    {
        var business = await AddBusinessAsync();
        business.TimeZoneId = "Asia/Tokyo";
        business.DeletionRequestedAt = Now;
        await _db.SaveChangesAsync();
        var service = new BusinessService(new BusinessRepository(_db), null!, _logos.Object, new FixedClock(Now));

        var settings = await service.GetSettingsAsync(business.Id);

        // 22:00 UTC on 15 June is 07:00 on 16 June in Tokyo.
        settings.DeletionRequestedAt.Should().Be(new DateTime(2026, 6, 16, 7, 0, 0));
    }

    // ---------------- the admin's list and dismissal ----------------

    [Fact]
    public async Task PendingRequests_AreOldestFirst_WithSkopjeDates_AndOnlyThosePending()
    {
        var newer = await AddBusinessAsync("Newer", requested: true, ownerEmail: "newer@test.com");
        newer.DeletionRequestedAt = Now.AddHours(-1);
        var older = await AddBusinessAsync("Older", requested: true, ownerEmail: "older@test.com");
        older.DeletionRequestedAt = Now.AddDays(-3);
        await AddBusinessAsync("No request", ownerEmail: null);
        await _db.SaveChangesAsync();

        var requests = await Admin().GetPendingDeletionRequestsAsync();

        requests.Select(r => r.BusinessName).Should().Equal("Older", "Newer");
        requests[0].OwnerEmail.Should().Be("older@test.com");
        requests[0].Reason.Should().Be("Closing down");
        requests[0].RequestedAt.Should().Be(new DateTime(2026, 6, 13, 0, 0, 0), "22:00 UTC is midnight in Skopje in June");
    }

    [Fact]
    public async Task Dismiss_ClearsTheRequest_Logs_AndEmailsTheTenant()
    {
        var business = await AddBusinessAsync(requested: true);

        var outcome = await Admin().DismissDeletionRequestAsync(business.Id, "  Called them  ", "admin@test.com");

        outcome.Should().Be(TenantEmailOutcome.Sent);
        var saved = await ReloadAsync(business.Id);
        saved!.DeletionRequestedAt.Should().BeNull();
        saved.DeletionRequestReason.Should().BeNull();

        var entry = await _db.AdminActionLogs.AsNoTracking().SingleAsync();
        entry.Action.Should().Be(AdminActionType.DeletionRequestDismissed);
        entry.Note.Should().Be("Called them");
        entry.AdminEmail.Should().Be("admin@test.com");

        var message = _sent.Should().ContainSingle().Which;
        message.MailTo.Should().Be("owner@test.com");
        message.Subject.Should().Contain("not approved");
        message.Content.Should().NotContain("Called them").And.Contain("support@test.com");
    }

    [Fact]
    public async Task Dismiss_WithoutARequest_IsRefused()
    {
        var business = await AddBusinessAsync();

        var act = () => Admin().DismissDeletionRequestAsync(business.Id, null, "admin@test.com");

        await act.Should().ThrowAsync<BusinessRuleException>();
        (await _db.AdminActionLogs.CountAsync()).Should().Be(0);
        _sent.Should().BeEmpty();
    }

    // ---------------- the name confirmation ----------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bella")]
    [InlineData("bella salon")]
    [InlineData("Bella Salon X")]
    public async Task WrongName_DeletesNothing(string? typed)
    {
        var business = await AddBusinessAsync(requested: true);

        foreach (var act in new Func<Task>[]
        {
            () => Admin().ApproveDeletionRequestAsync(business.Id, typed, "admin@test.com"),
            () => Admin().DeleteTenantAsync(business.Id, typed, null, "admin@test.com")
        })
        {
            (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be("ConfirmName");
        }

        (await ReloadAsync(business.Id)).Should().NotBeNull();
        _logos.Verify(l => l.DeleteAllAsync(It.IsAny<Guid>()), Times.Never);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Name_MayHaveSurroundingSpaces()
    {
        var business = await AddBusinessAsync();

        await Admin().DeleteTenantAsync(business.Id, "  Bella Salon ", null, "admin@test.com");

        (await ReloadAsync(business.Id)).Should().BeNull();
    }

    // ---------------- deleting ----------------

    [Fact]
    public async Task Approve_WithoutAPendingRequest_DeletesNothing()
    {
        var business = await AddBusinessAsync();

        var act = () => Admin().ApproveDeletionRequestAsync(business.Id, "Bella Salon", "admin@test.com");

        await act.Should().ThrowAsync<BusinessRuleException>();
        (await ReloadAsync(business.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Approve_Deletes_LogsWhoDidIt_WithoutTheTenantsText_AndSendsTheFinalEmail()
    {
        var business = await AddBusinessAsync(requested: true);

        var result = await Admin().ApproveDeletionRequestAsync(business.Id, "Bella Salon", "admin@test.com");

        result.Should().Be(new TenantDeletionResult(TenantEmailOutcome.Sent, LogoRemoved: true));
        (await ReloadAsync(business.Id)).Should().BeNull();

        var entry = await _db.AdminActionLogs.AsNoTracking().SingleAsync();
        entry.Should().BeEquivalentTo(new
        {
            Action = AdminActionType.DeletionRequestApproved, BusinessId = business.Id, BusinessName = "Bella Salon",
            Reason = PlatformAdminService.ApprovedRequestReason, At = Now,
            AdminEmail = "admin@test.com", Note = (string?)null, ChangedFields = (string?)null
        });

        _logos.Verify(l => l.DeleteAllAsync(business.Id), Times.Once);
        var message = _sent.Should().ContainSingle().Which;
        message.MailTo.Should().Be("owner@test.com");
        message.Subject.Should().Contain("deleted");
        message.Content.Should().Contain("Bella Salon");
    }

    [Fact]
    public async Task Delete_WorksWithoutARequest_AndKeepsTheAdminsReason()
    {
        var business = await AddBusinessAsync();

        await Admin().DeleteTenantAsync(business.Id, "Bella Salon", "  Fake business  ", "admin@test.com");

        (await ReloadAsync(business.Id)).Should().BeNull();
        var entry = await _db.AdminActionLogs.AsNoTracking().SingleAsync();
        entry.Action.Should().Be(AdminActionType.TenantDeleted);
        entry.Reason.Should().Be("Fake business");
        entry.AdminEmail.Should().Be("admin@test.com");
    }

    [Fact]
    public async Task Delete_WithAReasonThatIsTooLong_DeletesNothing()
    {
        var business = await AddBusinessAsync();

        var act = () => Admin().DeleteTenantAsync(business.Id, "Bella Salon", new string('x', AdminActionLog.MaxReasonLength + 1), "admin@test.com");

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be("Reason");
        (await ReloadAsync(business.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task Delete_RemovesTheTenantsDataFromEarlierLogRows_ButKeepsWhoDidIt()
    {
        var business = await AddBusinessAsync();
        var other = await AddBusinessAsync("Other", ownerEmail: null);
        await Admin().SuspendTenantAsync(business.Id, SuspensionReason.Other, "A private note", "admin@test.com");
        await Admin().UpdateTenantAsync(business.Id, new UpdateTenantDTO { Name = "Bella Salon", Address = "New" }, "admin@test.com");
        await Admin().SuspendTenantAsync(other.Id, SuspensionReason.Other, "Other's note", "admin@test.com");

        await Admin().DeleteTenantAsync(business.Id, "Bella Salon", null, "admin@test.com");

        var log = await _db.AdminActionLogs.AsNoTracking().Where(l => l.BusinessId == business.Id).ToListAsync();
        log.Should().HaveCount(3);
        log.Should().OnlyContain(l => l.AdminEmail == "admin@test.com" && l.Note == null && l.ChangedFields == null);
        log.Should().Contain(l => l.Action == AdminActionType.TenantSuspended && l.Reason == "Other" && l.BusinessName == "Bella Salon");

        var otherEntry = await _db.AdminActionLogs.AsNoTracking().SingleAsync(l => l.BusinessId == other.Id);
        otherEntry.AdminEmail.Should().Be("admin@test.com", "another tenant's log stays whole");
        otherEntry.Note.Should().Be("Other's note");
    }

    [Fact]
    public async Task AnUnknownTenant_Throws()
    {
        var id = Guid.NewGuid();
        await FluentActions.Awaiting(() => Admin().ApproveDeletionRequestAsync(id, "x", "admin@test.com")).Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Awaiting(() => Admin().DeleteTenantAsync(id, "x", null, "admin@test.com")).Should().ThrowAsync<KeyNotFoundException>();
        await FluentActions.Awaiting(() => Admin().DismissDeletionRequestAsync(id, null, "a")).Should().ThrowAsync<KeyNotFoundException>();
    }

    // ---------------- when something goes wrong ----------------

    [Fact]
    public async Task WhenTheDatabaseRefuses_ItIsAFriendlyError_WithNoLogoRemovalOrEmail()
    {
        var business = await AddBusinessAsync();
        var repository = new Mock<IPlatformAdminRepository>();
        repository.Setup(r => r.GetUserEmailsAsync(business.Id)).ReturnsAsync(new[] { "owner@test.com" });
        repository.Setup(r => r.DeleteTenantAsync(business.Id, It.IsAny<AdminActionLog>())).ThrowsAsync(new DbUpdateException("FK"));

        var act = () => Admin(repository.Object).DeleteTenantAsync(business.Id, "Bella Salon", null, "admin@test.com");

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Message.Should().Contain("nothing was changed");
        _logos.Verify(l => l.DeleteAllAsync(It.IsAny<Guid>()), Times.Never);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task LinksFromAnotherTenant_StopTheDelete_BeforeAnythingHappens()
    {
        var business = await AddBusinessAsync();
        var repository = new Mock<IPlatformAdminRepository>();
        repository.Setup(r => r.HasCrossTenantLinksAsync(business.Id)).ReturnsAsync(true);

        var act = () => Admin(repository.Object).DeleteTenantAsync(business.Id, "Bella Salon", null, "admin@test.com");

        await act.Should().ThrowAsync<BusinessRuleException>();
        repository.Verify(r => r.DeleteTenantAsync(It.IsAny<Guid>(), It.IsAny<AdminActionLog>()), Times.Never);
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task WhenTheLogoCantBeRemoved_TheTenantIsStillDeleted_AndTheEmailStillGoes()
    {
        var business = await AddBusinessAsync();
        _logos.Setup(l => l.DeleteAllAsync(business.Id)).ThrowsAsync(new IOException("in use"));

        var result = await Admin().DeleteTenantAsync(business.Id, "Bella Salon", null, "admin@test.com");

        result.Should().Be(new TenantDeletionResult(TenantEmailOutcome.Sent, LogoRemoved: false));
        (await ReloadAsync(business.Id)).Should().BeNull();
        _sent.Should().ContainSingle();
    }

    [Fact]
    public async Task WhenTheFinalEmailFails_TheTenantIsStillDeleted()
    {
        var business = await AddBusinessAsync();
        _email.Setup(e => e.SendEmailAsync(It.IsAny<EmailMessage>())).ThrowsAsync(new InvalidOperationException("down"));

        var result = await Admin().DeleteTenantAsync(business.Id, "Bella Salon", null, "admin@test.com");

        result.Email.Should().Be(TenantEmailOutcome.Failed);
        (await ReloadAsync(business.Id)).Should().BeNull();
    }

    [Fact]
    public async Task ATenantWithNoAddress_IsDeleted_AndThatIsReported()
    {
        var business = await AddBusinessAsync(ownerEmail: null);

        var result = await Admin().DeleteTenantAsync(business.Id, "Bella Salon", null, "admin@test.com");

        result.Email.Should().Be(TenantEmailOutcome.NoRecipient);
    }
}
