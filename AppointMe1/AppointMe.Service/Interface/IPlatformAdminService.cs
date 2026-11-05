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

        /// <summary>Sends the same email as registration to an unconfirmed account of this tenant.</summary>
        /// <returns><c>true</c> if the email went out. The action is logged either way.</returns>
        /// <exception cref="KeyNotFoundException">No such tenant, or the account isn't one of this tenant's.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">The email is already confirmed.</exception>
        Task<bool> ResendConfirmationAsync(Guid tenantId, string? accountId, string? adminEmail);

        /// <summary>Clears the lockout and resets the failed-attempt count of an account of this tenant.</summary>
        /// <exception cref="KeyNotFoundException">No such tenant, or the account isn't one of this tenant's.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">The account isn't locked out.</exception>
        Task UnlockAccountAsync(Guid tenantId, string? accountId, string? adminEmail);

        /// <summary>Sends the same email as Forgot password to a confirmed account of this tenant. The admin never sees or sets a password.</summary>
        /// <returns><c>true</c> if the email went out. The action is logged either way.</returns>
        /// <exception cref="KeyNotFoundException">No such tenant, or the account isn't one of this tenant's.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">The email isn't confirmed, as on the Forgot password page.</exception>
        Task<bool> SendPasswordResetAsync(Guid tenantId, string? accountId, string? adminEmail);

        /// <returns>Oldest first.</returns>
        Task<IReadOnlyList<DeletionRequestDTO>> GetPendingDeletionRequestsAsync();

        /// <param name="note">Optional and only for the action log; the tenant doesn't see it.</param>
        /// <exception cref="KeyNotFoundException">No such tenant.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">No pending request, or the note is too long.</exception>
        Task<TenantEmailOutcome> DismissDeletionRequestAsync(Guid tenantId, string? note, string? adminEmail);

        /// <summary>Permanently deletes a tenant that asked for it.</summary>
        /// <param name="typedName">Must match the business name exactly.</param>
        /// <param name="adminEmail">The admin doing this, for the action log.</param>
        /// <exception cref="KeyNotFoundException">No such tenant.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">No pending request, the wrong name, or it could not be deleted; nothing is changed.</exception>
        Task<TenantDeletionResult> ApproveDeletionRequestAsync(Guid tenantId, string? typedName, string? adminEmail);

        /// <summary>Permanently deletes a tenant without a request.</summary>
        /// <param name="typedName">Must match the business name exactly.</param>
        /// <param name="reason">Optional. It stays in the log after the tenant is gone.</param>
        /// <param name="adminEmail">The admin doing this, for the action log.</param>
        /// <exception cref="KeyNotFoundException">No such tenant.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">The wrong name, a reason that is too long, or it could not be deleted; nothing is changed.</exception>
        Task<TenantDeletionResult> DeleteTenantAsync(Guid tenantId, string? typedName, string? reason, string? adminEmail);
    }
}
