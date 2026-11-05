using AppointMe.Domain.DTO;
using AppointMe.Domain.Identity;
using AppointMe.Service.Interface;
using AppointMe.Web.Infrastructure;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace AppointMe.Tests.Web.Infrastructure;

/// <summary>Unit tests for <see cref="CurrentBusiness"/> with hand-written fakes.</summary>
public class CurrentBusinessTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private sealed class FakeTenantContext(TenantResolution resolution) : ITenantContext
    {
        public Task<TenantResolution> ResolveAsync(CancellationToken cancellationToken = default) => Task.FromResult(resolution);
        public Guid TenantId => resolution.TenantId ?? throw new InvalidOperationException();
    }

    private sealed class CountingBusinessService : IBusinessService
    {
        public int BrandingCalls { get; private set; }
        public Guid? LastTenant { get; private set; }

        public Task<BusinessBrandingDTO?> GetBrandingAsync(Guid tenantId)
        {
            BrandingCalls++;
            LastTenant = tenantId;
            return Task.FromResult<BusinessBrandingDTO?>(new BusinessBrandingDTO { Name = "Bella", EnableServices = true });
        }

        public Task<IdentityResult> RegisterOwnerAsync(AppointMeAppUser user, string password, string businessName) => throw new NotSupportedException();
        public Task<BusinessSettingsDTO> GetSettingsAsync(Guid tenantId) => throw new NotSupportedException();
        public Task UpdateSettingsAsync(Guid tenantId, BusinessSettingsDTO settings, LogoUpload? logo) => throw new NotSupportedException();
        public Task<BusinessCalendarConfigDTO> GetCalendarConfigAsync(Guid tenantId) => throw new NotSupportedException();
        public Task<SuspensionInfoDTO?> GetSuspensionInfoAsync(Guid tenantId) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Resolved_LoadsTheTenantsBusinessOnce()
    {
        var service = new CountingBusinessService();
        var sut = new CurrentBusiness(
            new FakeTenantContext(new TenantResolution(TenantResolutionStatus.Resolved, "user-1", Tenant)), service);

        var first = await sut.GetAsync();
        var second = await sut.GetAsync();

        first!.Name.Should().Be("Bella");
        second.Should().BeSameAs(first);
        service.BrandingCalls.Should().Be(1, "the layout and the page share one query per request");
        service.LastTenant.Should().Be(Tenant);
    }

    [Theory]
    [InlineData(TenantResolutionStatus.NotAuthenticated)]
    [InlineData(TenantResolutionStatus.NoTenant)]
    [InlineData(TenantResolutionStatus.PlatformAdmin)]
    [InlineData(TenantResolutionStatus.Suspended)]
    public async Task WithoutTenant_ReturnsNull_WithoutQuerying(TenantResolutionStatus status)
    {
        var service = new CountingBusinessService();
        var sut = new CurrentBusiness(new FakeTenantContext(new TenantResolution(status, null, null)), service);

        (await sut.GetAsync()).Should().BeNull();
        (await sut.GetAsync()).Should().BeNull();
        service.BrandingCalls.Should().Be(0);
    }
}
