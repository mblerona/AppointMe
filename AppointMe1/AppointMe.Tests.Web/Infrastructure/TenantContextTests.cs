using System.Security.Claims;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Service.Interface;
using AppointMe.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace AppointMe.Tests.Web.Infrastructure;

/// <summary>Unit tests for <see cref="TenantContext"/> against an in-memory database.</summary>
public class TenantContextTests
{
    private static readonly string UserIdClaimType = new IdentityOptions().ClaimsIdentity.UserIdClaimType;

    private readonly ApplicationDbContext _db = new(
        new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase($"tenant-context-{Guid.NewGuid()}")
            // Same as the web factories: this class can run after more than 20 of them have started, and
            // then EF throws this warning here. Without this, every test in the class fails, but only for
            // some test orders, so it looks random.
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options);

    private TenantContext CreateContext(ClaimsPrincipal principal) =>
        new(new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } },
            _db,
            Options.Create(new IdentityOptions()));

    private static ClaimsPrincipal Authenticated(string userId, params Claim[] extra) =>
        new(new ClaimsIdentity(new[] { new Claim(UserIdClaimType, userId) }.Concat(extra), "TestAuth"));

    private async Task<AppointMeAppUser> AddUserAsync(Guid? tenantId, bool withBusiness)
    {
        if (withBusiness && tenantId is { } id)
            _db.Businesses.Add(new Business { Id = id, Name = "Biz" });

        var user = new AppointMeAppUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = $"{Guid.NewGuid():N}@test.com",
            FirstName = "T",
            LastName = "U",
            TenantId = tenantId
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Anonymous_IsNotAuthenticated_AndTenantIdThrows()
    {
        var ctx = CreateContext(new ClaimsPrincipal(new ClaimsIdentity()));

        var result = await ctx.ResolveAsync();

        result.Status.Should().Be(TenantResolutionStatus.NotAuthenticated);
        var act = () => ctx.TenantId;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task UserMissingFromDatabase_IsNotAuthenticated()
    {
        var ctx = CreateContext(Authenticated(Guid.NewGuid().ToString()));

        (await ctx.ResolveAsync()).Status.Should().Be(TenantResolutionStatus.NotAuthenticated);
    }

    [Fact]
    public async Task UserWithoutTenant_IsNoTenant()
    {
        var user = await AddUserAsync(tenantId: null, withBusiness: false);
        var ctx = CreateContext(Authenticated(user.Id));

        var result = await ctx.ResolveAsync();

        result.Status.Should().Be(TenantResolutionStatus.NoTenant);
        result.UserId.Should().Be(user.Id);
        var act = () => ctx.TenantId;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task UserWhoseBusinessIsMissing_IsNoTenant()
    {
        var user = await AddUserAsync(Guid.NewGuid(), withBusiness: false);
        var ctx = CreateContext(Authenticated(user.Id));

        (await ctx.ResolveAsync()).Status.Should().Be(TenantResolutionStatus.NoTenant);
    }

    [Fact]
    public async Task UserWithBusiness_IsResolved()
    {
        var tenantId = Guid.NewGuid();
        var user = await AddUserAsync(tenantId, withBusiness: true);
        var ctx = CreateContext(Authenticated(user.Id));

        var result = await ctx.ResolveAsync();

        result.Status.Should().Be(TenantResolutionStatus.Resolved);
        result.TenantId.Should().Be(tenantId);
        ctx.TenantId.Should().Be(tenantId);
    }

    private async Task AddToRoleAsync(AppointMeAppUser user, string roleName)
    {
        var role = new IdentityRole(roleName);
        _db.Roles.Add(role);
        _db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = role.Id });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task PlatformAdmin_HasNoTenant_AndTenantIdThrows()
    {
        var user = await AddUserAsync(tenantId: null, withBusiness: false);
        await AddToRoleAsync(user, PlatformRoles.Admin);
        var ctx = CreateContext(Authenticated(user.Id));

        var result = await ctx.ResolveAsync();

        result.Status.Should().Be(TenantResolutionStatus.PlatformAdmin);
        result.UserId.Should().Be(user.Id);
        result.TenantId.Should().BeNull();
        var act = () => ctx.TenantId;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task PlatformAdmin_WithATenantIdSetByHand_StillNeverActsAsThatTenant()
    {
        var tenantId = Guid.NewGuid();
        var user = await AddUserAsync(tenantId, withBusiness: true);
        await AddToRoleAsync(user, PlatformRoles.Admin);
        var ctx = CreateContext(Authenticated(user.Id));

        var result = await ctx.ResolveAsync();

        result.Status.Should().Be(TenantResolutionStatus.PlatformAdmin);
        result.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task UserOfASuspendedBusiness_IsSuspended_AndTenantIdThrows()
    {
        var tenantId = Guid.NewGuid();
        var user = await AddUserAsync(tenantId, withBusiness: true);
        (await _db.Businesses.SingleAsync(b => b.Id == tenantId)).Status = BusinessStatus.Suspended;
        await _db.SaveChangesAsync();
        var ctx = CreateContext(Authenticated(user.Id));

        var result = await ctx.ResolveAsync();

        result.Status.Should().Be(TenantResolutionStatus.Suspended);
        result.TenantId.Should().Be(tenantId, "the suspended page needs to know which business");
        var act = () => ctx.TenantId;
        act.Should().Throw<InvalidOperationException>("tenant code must not run");
    }

    [Fact]
    public async Task SuspendingAndReactivating_AppliesOnTheNextRequest()
    {
        var tenantId = Guid.NewGuid();
        var user = await AddUserAsync(tenantId, withBusiness: true);
        var principal = Authenticated(user.Id);
        var business = await _db.Businesses.SingleAsync(b => b.Id == tenantId);

        (await CreateContext(principal).ResolveAsync()).Status.Should().Be(TenantResolutionStatus.Resolved);

        business.Status = BusinessStatus.Suspended;
        await _db.SaveChangesAsync();
        (await CreateContext(principal).ResolveAsync()).Status.Should().Be(TenantResolutionStatus.Suspended);

        business.Status = BusinessStatus.Active;
        await _db.SaveChangesAsync();
        (await CreateContext(principal).ResolveAsync()).Status.Should().Be(TenantResolutionStatus.Resolved);
    }

    [Fact]
    public async Task AnotherRole_DoesNotMakeAUserAPlatformAdmin()
    {
        var tenantId = Guid.NewGuid();
        var user = await AddUserAsync(tenantId, withBusiness: true);
        await AddToRoleAsync(user, "SomethingElse");
        var ctx = CreateContext(Authenticated(user.Id));

        (await ctx.ResolveAsync()).Status.Should().Be(TenantResolutionStatus.Resolved);
    }

    [Fact]
    public async Task AdminRoleClaim_IsIgnored_RoleAlwaysComesFromDatabase()
    {
        var tenantId = Guid.NewGuid();
        var user = await AddUserAsync(tenantId, withBusiness: true);
        var ctx = CreateContext(Authenticated(user.Id, new Claim(ClaimTypes.Role, PlatformRoles.Admin)));

        (await ctx.ResolveAsync()).Status.Should().Be(TenantResolutionStatus.Resolved);
    }

    [Fact]
    public async Task TenantClaim_IsIgnored_TenantAlwaysComesFromDatabase()
    {
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();
        _db.Businesses.Add(new Business { Id = otherTenant, Name = "Other" });
        var user = await AddUserAsync(tenantId, withBusiness: true);

        var ctx = CreateContext(Authenticated(user.Id, new Claim("TenantId", otherTenant.ToString())));

        await ctx.ResolveAsync();

        ctx.TenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task ResolvesOncePerRequest_ButANewRequestSeesDatabaseChanges()
    {
        var tenantId = Guid.NewGuid();
        var user = await AddUserAsync(tenantId, withBusiness: true);
        var principal = Authenticated(user.Id);

        var firstRequest = CreateContext(principal);
        (await firstRequest.ResolveAsync()).Status.Should().Be(TenantResolutionStatus.Resolved);

        // The tenant changes in the database during the request.
        var tracked = await _db.Users.SingleAsync(u => u.Id == user.Id);
        tracked.TenantId = null;
        await _db.SaveChangesAsync();

        // Same request: cached result, no second query.
        (await firstRequest.ResolveAsync()).Status.Should().Be(TenantResolutionStatus.Resolved);
        firstRequest.TenantId.Should().Be(tenantId);

        // Next request (new scoped instance): the change takes effect immediately.
        var nextRequest = CreateContext(principal);
        (await nextRequest.ResolveAsync()).Status.Should().Be(TenantResolutionStatus.NoTenant);
    }
}
