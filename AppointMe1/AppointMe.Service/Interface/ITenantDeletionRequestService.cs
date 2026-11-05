namespace AppointMe.Service.Interface
{
    /// <summary>
    /// The tenant's side of a deletion request: asking and cancelling, always for the tenant's own Business.
    /// Approving or dismissing is only in <see cref="IPlatformAdminService"/>.
    /// </summary>
    public interface ITenantDeletionRequestService
    {
        /// <param name="reason">Optional.</param>
        /// <exception cref="KeyNotFoundException">No such tenant.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">Already requested, or the reason is too long.</exception>
        Task RequestAsync(Guid tenantId, string? reason);

        /// <exception cref="KeyNotFoundException">No such tenant.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">No pending request.</exception>
        Task CancelAsync(Guid tenantId);
    }
}
