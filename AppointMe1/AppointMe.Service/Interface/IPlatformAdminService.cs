using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;

namespace AppointMe.Service.Interface
{
    /// <summary>
    /// Everything the platform admin can see and do across tenants. Only the /Admin area may use this.
    /// Dates in its results are in <see cref="Domain.TimeZones.BusinessTimeZones.Default"/>, because the
    /// admin has no Business and so no time zone of their own.
    /// </summary>
    public interface IPlatformAdminService
    {
        Task<PlatformTotalsDTO> GetTotalsAsync();

        Task<IReadOnlyList<TenantSummaryDTO>> GetTenantsAsync(string? search, BusinessStatus? status);

        /// <exception cref="KeyNotFoundException">No such tenant.</exception>
        Task<TenantDetailsDTO> GetTenantAsync(Guid tenantId);

        /// <exception cref="KeyNotFoundException">No such tenant.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">An invalid value; nothing is saved.</exception>
        Task UpdateTenantAsync(Guid tenantId, UpdateTenantDTO update);
    }
}
