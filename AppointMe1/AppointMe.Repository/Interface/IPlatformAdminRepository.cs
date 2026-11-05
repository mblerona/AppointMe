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

        /// <summary>Saved by the next SaveChanges on this request's context, together with the action it records.</summary>
        void AddActionLog(AdminActionLog entry);

        /// <param name="businessId">Null means every tenant.</param>
        /// <returns>Newest first.</returns>
        Task<IReadOnlyList<AdminActionLogDTO>> GetActionLogAsync(Guid? businessId, int limit);
    }
}
