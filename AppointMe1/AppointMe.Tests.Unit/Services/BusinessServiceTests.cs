using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using AwesomeAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

/// <summary>
/// BusinessService.RegisterOwnerAsync with a real UserManager/UserStore sharing one in-memory DbContext,
/// like in the app (both are scoped).
/// </summary>
public class BusinessServiceTests : IDisposable
{
    private const string ValidPassword = "Test123!Aa";

    private readonly ApplicationDbContext _db;
    private readonly UserManager<AppointMeAppUser> _userManager;
    private readonly BusinessService _sut;
    private readonly Mock<ILogoStorage> _logoStorage = new(MockBehavior.Strict);

    public BusinessServiceTests()
    {
        _db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"business-service-{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options);

        var options = new IdentityOptions();
        options.Password.RequiredLength = 8;

        _userManager = new UserManager<AppointMeAppUser>(
            new UserStore<AppointMeAppUser>(_db),
            Options.Create(options),
            new PasswordHasher<AppointMeAppUser>(),
            new[] { new UserValidator<AppointMeAppUser>() },
            new[] { new PasswordValidator<AppointMeAppUser>() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            null!,
            NullLogger<UserManager<AppointMeAppUser>>.Instance);

        _sut = new BusinessService(new BusinessRepository(_db), _userManager, _logoStorage.Object, TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<BusinessService>.Instance);
    }

    public void Dispose()
    {
        _userManager.Dispose();
        _db.Dispose();
    }

    private static AppointMeAppUser NewUser(string email) => new()
    {
        UserName = email,
        Email = email,
        FirstName = "Ana",
        LastName = "Test"
    };

    private async Task<int> SavedBusinessCountAsync()
    {
        // Count what is stored, not what is tracked.
        _db.ChangeTracker.Clear();
        return await _db.Businesses.CountAsync();
    }

    [Fact]
    public async Task RegisterOwner_CreatesBusiness_AndLinksTheUser()
    {
        var user = NewUser("owner@test.com");

        var result = await _sut.RegisterOwnerAsync(user, ValidPassword, "  Bella Salon  ");

        result.Created.Should().BeTrue();
        result.ExistingAccount.Should().BeNull();
        user.TenantId.Should().NotBeNull();
        user.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));

        _db.ChangeTracker.Clear();
        var business = await _db.Businesses.SingleAsync();
        business.Id.Should().Be(user.TenantId!.Value);
        business.Name.Should().Be("Bella Salon");
        business.EnableInvoices.Should().BeFalse("same defaults as before");
        business.DefaultSlotMinutes.Should().Be(30);
        business.TimeZoneId.Should().Be("Europe/Skopje");

        var stored = await _db.Users.SingleAsync(u => u.Email == "owner@test.com");
        stored.TenantId.Should().Be(business.Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RegisterOwner_WithoutBusinessName_Throws_AndSavesNothing(string name)
    {
        var act = () => _sut.RegisterOwnerAsync(NewUser("noname@test.com"), ValidPassword, name);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Business name is required.");
        (await SavedBusinessCountAsync()).Should().Be(0);
        (await _db.Users.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RegisterOwner_WithTooLongBusinessName_Throws()
    {
        var act = () => _sut.RegisterOwnerAsync(NewUser("long@test.com"), ValidPassword, new string('x', 201));

        await act.Should().ThrowAsync<BusinessRuleException>();
        (await SavedBusinessCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RegisterOwner_WithWeakPassword_ReturnsErrors_AndLeavesNoBusiness()
    {
        var user = NewUser("weak@test.com");

        var result = await _sut.RegisterOwnerAsync(user, "short", "Biz");

        result.Failed.Should().BeTrue();
        result.Identity.Errors.Should().NotBeEmpty();
        user.TenantId.Should().BeNull("a failed registration doesn't keep the tenant link");
        _db.ChangeTracker.Entries<Business>().Should().BeEmpty("the Business must not be saved by a later SaveChanges");
        (await SavedBusinessCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RegisterOwner_WithAnExistingEmail_ReportsTheExistingAccount_NotAnError_AndCreatesNothing()
    {
        var first = NewUser("dup@test.com");
        (await _sut.RegisterOwnerAsync(first, ValidPassword, "First")).Created.Should().BeTrue();

        var second = NewUser("dup@test.com");
        var result = await _sut.RegisterOwnerAsync(second, "Other123!Aa", "Second");

        result.Failed.Should().BeFalse("the visitor must see the same result as for a new account");
        result.Created.Should().BeFalse();
        result.EmailAlreadyRegistered.Should().BeTrue();
        result.ExistingAccount!.Id.Should().Be(first.Id);
        second.TenantId.Should().BeNull();
        (await SavedBusinessCountAsync()).Should().Be(1);
        (await _db.Businesses.SingleAsync()).Name.Should().Be("First");
        (await _db.Users.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RegisterOwner_WithAnExistingEmail_LeavesTheExistingAccountUnchanged()
    {
        var first = NewUser("keep@test.com");
        await _sut.RegisterOwnerAsync(first, ValidPassword, "First");

        await _sut.RegisterOwnerAsync(NewUser("keep@test.com"), "Other123!Aa", "Second");

        _db.ChangeTracker.Clear();
        var stored = await _userManager.FindByEmailAsync("keep@test.com");
        (await _userManager.CheckPasswordAsync(stored!, ValidPassword)).Should().BeTrue("the old password still works");
        (await _userManager.CheckPasswordAsync(stored!, "Other123!Aa")).Should().BeFalse();
    }

    [Fact]
    public async Task RegisterOwner_ExistingEmail_IsFoundRegardlessOfCase()
    {
        await _sut.RegisterOwnerAsync(NewUser("case@test.com"), ValidPassword, "First");

        var result = await _sut.RegisterOwnerAsync(NewUser("CASE@Test.com"), ValidPassword, "Second");

        result.EmailAlreadyRegistered.Should().BeTrue();
        (await SavedBusinessCountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task RegisterOwner_WeakPassword_GivesTheSameErrors_WhetherOrNotTheEmailExists()
    {
        // Otherwise a weak password would tell a visitor which emails have accounts.
        await _sut.RegisterOwnerAsync(NewUser("taken@test.com"), ValidPassword, "First");

        var forExisting = await _sut.RegisterOwnerAsync(NewUser("taken@test.com"), "short", "Biz");
        var forNew = await _sut.RegisterOwnerAsync(NewUser("free@test.com"), "short", "Biz");

        forExisting.Failed.Should().BeTrue();
        forExisting.EmailAlreadyRegistered.Should().BeFalse();
        forExisting.Identity.Errors.Select(e => e.Code).Should().Equal(forNew.Identity.Errors.Select(e => e.Code));
    }

    [Fact]
    public async Task RegisterOwner_BusinessNameError_ComesFirst_EvenForAnExistingEmail()
    {
        await _sut.RegisterOwnerAsync(NewUser("named@test.com"), ValidPassword, "First");

        var act = () => _sut.RegisterOwnerAsync(NewUser("named@test.com"), ValidPassword, "   ");

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("Business name is required.");
    }

    [Fact]
    public async Task RegisterOwner_ForUserThatAlreadyHasATenant_Throws()
    {
        var user = NewUser("has-tenant@test.com");
        user.TenantId = Guid.NewGuid();

        var act = () => _sut.RegisterOwnerAsync(user, ValidPassword, "Biz");

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await SavedBusinessCountAsync()).Should().Be(0);
    }
}
