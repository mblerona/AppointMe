using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;

namespace AppointMe.Repository.Interface
{
    /// <summary>
    /// The only reads across tenants in the application. Only PlatformAdminService may use this
    /// (PlatformAdminBoundaryTests checks that). Tenant code keeps using its filtered repositories.
    /// </summary>
    public interface IPlatformAdminRepository
    {
        /// <param name="search">Matches the business name, the business email or a user's email; ignores case.</param>
        /// <param name="status">Null means every status.</param>
        /// <returns>Newest first.</returns>
        Task<IReadOnlyList<TenantSummaryDTO>> GetTenantsAsync(string? search, BusinessStatus? status);

        Task<TenantDetailsDTO?> GetTenantAsync(Guid tenantId);

        Task<PlatformTotalsDTO> GetTotalsAsync(DateTime newSinceUtc);

        /// <summary>The login emails of a tenant's users.</summary>
        Task<IReadOnlyList<string>> GetUserEmailsAsync(Guid tenantId);

        /// <summary>The tenant's login accounts, oldest first, without any account that has the PlatformAdmin role.</summary>
        Task<IReadOnlyList<TenantAccountDTO>> GetTenantAccountsAsync(Guid tenantId);

        /// <summary>Saved by the next SaveChanges on this request's context, together with the action it records.</summary>
        void AddActionLog(AdminActionLog entry);

        /// <param name="businessId">Null means every tenant.</param>
        /// <returns>Newest first.</returns>
        Task<IReadOnlyList<AdminActionLogDTO>> GetActionLogAsync(Guid? businessId, int limit);

        /// <returns>Oldest first, so the longest-waiting request is on top.</returns>
        Task<IReadOnlyList<DeletionRequestDTO>> GetPendingDeletionRequestsAsync();

        /// <summary>
        /// True when another tenant's rows point at this tenant's. It should never happen, but the database
        /// cascades from a customer to its appointments, so deleting could take another tenant's data with it.
        /// </summary>
        Task<bool> HasCrossTenantLinksAsync(Guid tenantId);

        /// <summary>
        /// Permanently deletes the tenant in one transaction. Its action log rows stay, reduced to the minimum,
        /// and <paramref name="deletionEntry"/> is added in the same transaction. An account with the
        /// PlatformAdmin role is never deleted; it only loses the TenantId.
        /// </summary>
        Task DeleteTenantAsync(Guid tenantId, AdminActionLog deletionEntry);
    }
}
