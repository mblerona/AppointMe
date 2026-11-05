namespace AppointMe.Service.Interface
{
    public enum TenantResolutionStatus
    {
        /// <summary>No logged-in user, or the user's account no longer exists.</summary>
        NotAuthenticated,

        /// <summary>The user is logged in but has no tenant, or the tenant's Business is missing.</summary>
        NoTenant,

        /// <summary>The user belongs to an existing Business.</summary>
        Resolved,

        /// <summary>The user has the PlatformAdmin role. Admins have no tenant, so TenantId stays unavailable.</summary>
        PlatformAdmin
    }

    public sealed record TenantResolution(TenantResolutionStatus Status, string? UserId, Guid? TenantId);

    /// <summary>
    /// The tenant of the current request. It is always resolved on the server from the logged-in
    /// user's database record (never from a claim or user input), at most once per request.
    /// </summary>
    public interface ITenantContext
    {
        /// <summary>Resolves the tenant on the first call and returns the cached result afterwards.</summary>
        Task<TenantResolution> ResolveAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// The current tenant id. Only valid after <see cref="ResolveAsync"/> returned
        /// <see cref="TenantResolutionStatus.Resolved"/>; throws otherwise.
        /// </summary>
        Guid TenantId { get; }
    }
}
