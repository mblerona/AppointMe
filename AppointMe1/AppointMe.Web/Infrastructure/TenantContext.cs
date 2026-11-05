using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// Scoped (one instance per request). Takes the user id from the authentication cookie and reads
    /// the TenantId from the database in a single query, so changes to the user or Business
    /// (for example a suspension) take effect on the next request.
    /// </summary>
    public sealed class TenantContext : ITenantContext
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ApplicationDbContext _db;
        private readonly string _userIdClaimType;

        private TenantResolution? _resolution;

        public TenantContext(
            IHttpContextAccessor httpContextAccessor,
            ApplicationDbContext db,
            IOptions<IdentityOptions> identityOptions)
        {
            _httpContextAccessor = httpContextAccessor;
            _db = db;
            _userIdClaimType = identityOptions.Value.ClaimsIdentity.UserIdClaimType;
        }

        public async Task<TenantResolution> ResolveAsync(CancellationToken cancellationToken = default)
        {
            if (_resolution != null)
                return _resolution;

            _resolution = await LoadAsync(cancellationToken);
            return _resolution;
        }

        public Guid TenantId =>
            _resolution is { Status: TenantResolutionStatus.Resolved, TenantId: { } id }
                ? id
                : throw new InvalidOperationException(
                    "The tenant has not been resolved for this request. Call ResolveAsync first.");

        private async Task<TenantResolution> LoadAsync(CancellationToken cancellationToken)
        {
            var principal = _httpContextAccessor.HttpContext?.User;
            var userId = principal?.Identity?.IsAuthenticated == true
                ? principal.FindFirst(_userIdClaimType)?.Value
                : null;

            if (string.IsNullOrEmpty(userId))
                return new TenantResolution(TenantResolutionStatus.NotAuthenticated, null, null);

            var row = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new
                {
                    u.TenantId,
                    BusinessStatus = _db.Businesses
                        .Where(b => b.Id == u.TenantId)
                        .Select(b => (BusinessStatus?)b.Status)
                        .FirstOrDefault(),
                    IsPlatformAdmin = _db.UserRoles.Any(ur => ur.UserId == u.Id
                        && _db.Roles.Any(r => r.Id == ur.RoleId && r.Name == PlatformRoles.Admin))
                })
                .FirstOrDefaultAsync(cancellationToken);

            if (row == null)
                return new TenantResolution(TenantResolutionStatus.NotAuthenticated, null, null);

            // Checked first: an admin never acts as a tenant, even if a TenantId was set on the account by hand.
            if (row.IsPlatformAdmin)
                return new TenantResolution(TenantResolutionStatus.PlatformAdmin, userId, null);

            if (row.TenantId is not { } tenantId || tenantId == Guid.Empty || row.BusinessStatus == null)
                return new TenantResolution(TenantResolutionStatus.NoTenant, userId, null);

            if (row.BusinessStatus == BusinessStatus.Suspended)
                return new TenantResolution(TenantResolutionStatus.Suspended, userId, tenantId);

            return new TenantResolution(TenantResolutionStatus.Resolved, userId, tenantId);
        }
    }
}
