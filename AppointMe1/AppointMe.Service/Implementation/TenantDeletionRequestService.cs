using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Exceptions;
using AppointMe.Repository.Interface;
using AppointMe.Service.Admin;
using AppointMe.Service.Email;
using AppointMe.Service.Interface;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppointMe.Service.Implementation
{
    public class TenantDeletionRequestService : ITenantDeletionRequestService
    {
        private readonly IBusinessRepository _businessRepository;
        private readonly IEmailService _emailService;
        private readonly PlatformAdminOptions _admin;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<TenantDeletionRequestService> _logger;

        public TenantDeletionRequestService(
            IBusinessRepository businessRepository,
            IEmailService emailService,
            IOptions<PlatformAdminOptions> admin,
            TimeProvider timeProvider,
            ILogger<TenantDeletionRequestService> logger)
        {
            _businessRepository = businessRepository;
            _emailService = emailService;
            _admin = admin.Value;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        public async Task RequestAsync(Guid tenantId, string? reason)
        {
            var business = await _businessRepository.GetByIdAsync(tenantId)
                ?? throw new KeyNotFoundException("Business not found.");

            var validReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (validReason?.Length > Business.MaxDeletionRequestReasonLength)
                throw new BusinessRuleException(
                    $"The reason can be at most {Business.MaxDeletionRequestReasonLength} characters.", "Reason");

            if (business.DeletionRequestedAt != null)
                throw new BusinessRuleException("You have already requested the deletion of this account.");

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            business.DeletionRequestedAt = now;
            business.DeletionRequestReason = validReason;
            business.UpdatedAt = now;

            await _businessRepository.SaveChangesAsync();

            await NotifyAdminAsync(business);
        }

        public async Task CancelAsync(Guid tenantId)
        {
            var business = await _businessRepository.GetByIdAsync(tenantId)
                ?? throw new KeyNotFoundException("Business not found.");

            if (business.DeletionRequestedAt == null)
                throw new BusinessRuleException("There is no pending deletion request to cancel.");

            business.DeletionRequestedAt = null;
            business.DeletionRequestReason = null;
            business.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

            await _businessRepository.SaveChangesAsync();
        }

        /// <summary>The request is saved and shows on the admin home page, so a failed email is only logged.</summary>
        private async Task NotifyAdminAsync(Business business)
        {
            var adminEmail = _admin.Email?.Trim();
            if (string.IsNullOrEmpty(adminEmail))
            {
                _logger.LogWarning(
                    "No admin email for the deletion request of business {BusinessId}: {Section}:Email is not set.",
                    business.Id, PlatformAdminOptions.SectionName);
                return;
            }

            var reason = business.DeletionRequestReason ?? "(no reason given)";
            try
            {
                await _emailService.SendEmailAsync(new EmailMessage
                {
                    MailTo = adminEmail,
                    Subject = "New AppointMe account deletion request",
                    Content = string.Join(Environment.NewLine,
                        $"{business.Name} has asked for its AppointMe account to be deleted.",
                        "",
                        $"Reason: {reason}",
                        "",
                        "Open the admin home page to approve or dismiss the request.")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not email the admin about the deletion request of business {BusinessId}.", business.Id);
            }
        }
    }
}
