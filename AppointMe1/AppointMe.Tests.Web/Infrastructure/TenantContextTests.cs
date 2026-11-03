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
