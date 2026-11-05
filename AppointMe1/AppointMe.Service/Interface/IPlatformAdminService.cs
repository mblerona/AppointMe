using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;

namespace AppointMe.Service.Interface
{
    /// <summary>
    /// Everything the platform admin can see and do across tenants. Only the /Admin area may use this.
    /// Dates in its results are in <see cref="Domain.TimeZones.BusinessTimeZones.Default"/>, because the
    /// admin has no Business and so no time zone of their own. Every change is recorded in the action log,
    /// in the same save as the change itself.
    /// </summary>
    public interface IPlatformAdminService
    {
        Task<PlatformTotalsDTO> GetTotalsAsync();

        Task<IReadOnlyList<TenantSummaryDTO>> GetTenantsAsync(string? search, BusinessStatus? status);

        /// <exception cref="KeyNotFoundException">No such tenant.</exception>
        Task<TenantDetailsDTO> GetTenantAsync(Guid tenantId);

        /// <param name="adminEmail">The admin doing this, for the action log.</param>
        /// <exception cref="KeyNotFoundException">No such tenant.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">An invalid value; nothing is saved.</exception>
        Task UpdateTenantAsync(Guid tenantId, UpdateTenantDTO update, string? adminEmail);

        /// <summary>Checks the reason and note without changing anything, for the step before the confirmation.</summary>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">An invalid reason or note.</exception>
        void ValidateSuspension(SuspensionReason? reason, string? note);

        /// <exception cref="KeyNotFoundException">No such tenant.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">An invalid reason or note, or already suspended.</exception>
        Task<TenantEmailOutcome> SuspendTenantAsync(Guid tenantId, SuspensionReason? reason, string? note, string? adminEmail);

        /// <param name="note">Optional and only for the action log; the tenant doesn't see it.</param>
        /// <exception cref="KeyNotFoundException">No such tenant.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">Not suspended, or the note is too long.</exception>
        Task<TenantEmailOutcome> ReactivateTenantAsync(Guid tenantId, string? note, string? adminEmail);

        /// <param name="limit">At most this many, newest first.</param>
        Task<IReadOnlyList<AdminActionLogDTO>> GetActionLogAsync(int limit);
    }
}
