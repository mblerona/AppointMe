using System.ComponentModel.DataAnnotations;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Identity;
using AppointMe.Domain.TimeZones;
using AppointMe.Domain.Validation;
using AppointMe.Repository.Interface;
using AppointMe.Service.Admin;
using AppointMe.Service.Email;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AppointMe.Service.Implementation
{
    public class PlatformAdminService : IPlatformAdminService
    {
        public const int MaxEmailLength = 256;
        public const int MaxAddressLength = 500;
        public const int NewTenantDays = 30;

        /// <summary>How many actions the tenant details page shows.</summary>
        public const int TenantActionLogLimit = 50;

        /// <summary>The log's reason for an approved request. The tenant's own text isn't kept after deletion.</summary>
        public const string ApprovedRequestReason = "Requested by the tenant";

        private readonly IPlatformAdminRepository _adminRepository;
        private readonly IBusinessRepository _businessRepository;
        private readonly IEmailService _emailService;
        private readonly ILogoStorage _logoStorage;
        private readonly UserManager<AppointMeAppUser> _userManager;
        private readonly IAccountEmails _accountEmails;
        private readonly SupportOptions _support;
        private readonly TimeProvider _timeProvider;
        private readonly ILogger<PlatformAdminService> _logger;

        public PlatformAdminService(
            IPlatformAdminRepository adminRepository,
            IBusinessRepository businessRepository,
            IEmailService emailService,
            ILogoStorage logoStorage,
            UserManager<AppointMeAppUser> userManager,
            IAccountEmails accountEmails,
            IOptions<SupportOptions> support,
            TimeProvider timeProvider,
            ILogger<PlatformAdminService> logger)
        {
            _adminRepository = adminRepository;
            _businessRepository = businessRepository;
            _emailService = emailService;
            _logoStorage = logoStorage;
            _userManager = userManager;
            _accountEmails = accountEmails;
            _support = support.Value;
            _timeProvider = timeProvider;
            _logger = logger;
        }

        private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

        // ---------------- reading ----------------

        public Task<PlatformTotalsDTO> GetTotalsAsync() =>
            _adminRepository.GetTotalsAsync(UtcNow.AddDays(-NewTenantDays));

        public async Task<IReadOnlyList<TenantSummaryDTO>> GetTenantsAsync(string? search, BusinessStatus? status)
        {
            var tenants = await _adminRepository.GetTenantsAsync(search, status);
            foreach (var tenant in tenants)
                tenant.CreatedAt = ToDisplay(tenant.CreatedAt);
            return tenants;
        }

        public async Task<TenantDetailsDTO> GetTenantAsync(Guid tenantId)
        {
            var tenant = await _adminRepository.GetTenantAsync(tenantId)
                ?? throw new KeyNotFoundException("Tenant not found.");

            tenant.CreatedAt = ToDisplay(tenant.CreatedAt);
            if (tenant.SuspendedAt is { } suspendedAt)
                tenant.SuspendedAt = ToDisplay(suspendedAt);
            if (tenant.DeletionRequestedAt is { } requestedAt)
                tenant.DeletionRequestedAt = ToDisplay(requestedAt);
            tenant.ActionLog = ToDisplay(await _adminRepository.GetActionLogAsync(tenantId, TenantActionLogLimit));

            tenant.Accounts = await _adminRepository.GetTenantAccountsAsync(tenantId);
            var now = _timeProvider.GetUtcNow();
            foreach (var account in tenant.Accounts)
            {
                if (account.LockoutEnd is { } end && end > now)
                    account.LockedOutUntil = ToDisplay(end.UtcDateTime);
            }

            return tenant;
        }

        public async Task<IReadOnlyList<AdminActionLogDTO>> GetActionLogAsync(int limit) =>
            ToDisplay(await _adminRepository.GetActionLogAsync(null, limit));

        // ---------------- editing ----------------

        public async Task UpdateTenantAsync(Guid tenantId, UpdateTenantDTO update, string? adminEmail)
        {
            var business = await _businessRepository.GetByIdAsync(tenantId)
                ?? throw new KeyNotFoundException("Tenant not found.");

            // Validate everything before changing anything.
            var name = BusinessService.ValidateName(update.Name, nameof(UpdateTenantDTO.Name));
            var email = ValidateEmail(update.Email);
            var phone = PhoneNumberNormalizer.NormalizeOptional(update.Phone, nameof(UpdateTenantDTO.Phone));
            var address = ValidateAddress(update.Address);

            var changed = new List<string>();
            if (business.Name != name) changed.Add("Name");
            if (business.Email != email) changed.Add("Email");
            if (business.Phone != phone) changed.Add("Phone");
            if (business.Address != address) changed.Add("Address");

            // Saving an unchanged form is not an action.
            if (changed.Count == 0)
                return;

            business.Name = name;
            business.Email = email;
            business.Phone = phone;
            business.Address = address;
            business.UpdatedAt = UtcNow;

            AddLog(business, AdminActionType.TenantEdited, adminEmail, changedFields: string.Join(", ", changed));

            await _businessRepository.SaveChangesAsync();
        }

        // ---------------- suspension ----------------

        public void ValidateSuspension(SuspensionReason? reason, string? note) =>
            ValidatedSuspension(reason, note);

        public async Task<TenantEmailOutcome> SuspendTenantAsync(
            Guid tenantId, SuspensionReason? reason, string? note, string? adminEmail)
        {
            var business = await _businessRepository.GetByIdAsync(tenantId)
                ?? throw new KeyNotFoundException("Tenant not found.");

            var (validReason, validNote) = ValidatedSuspension(reason, note);

            if (business.Status == BusinessStatus.Suspended)
                throw new BusinessRuleException("This business is already suspended.");

            var now = UtcNow;
            business.Status = BusinessStatus.Suspended;
            business.SuspendedAt = now;
            business.SuspensionReason = validReason;
            business.SuspensionNote = validNote;
            business.UpdatedAt = now;

            AddLog(business, AdminActionType.TenantSuspended, adminEmail, validReason.DisplayText(), validNote);

            await _businessRepository.SaveChangesAsync();

            return await EmailTenantAsync(business, "Your AppointMe account has been suspended", SuspendedEmail(business));
        }

        public async Task<TenantEmailOutcome> ReactivateTenantAsync(Guid tenantId, string? note, string? adminEmail)
        {
            var business = await _businessRepository.GetByIdAsync(tenantId)
                ?? throw new KeyNotFoundException("Tenant not found.");

            var validNote = ValidateNote(note);

            if (business.Status != BusinessStatus.Suspended)
                throw new BusinessRuleException("This business is not suspended.");

            business.Status = BusinessStatus.Active;
            business.SuspendedAt = null;
            business.SuspensionReason = null;
            business.SuspensionNote = null;
            business.UpdatedAt = UtcNow;

            AddLog(business, AdminActionType.TenantReactivated, adminEmail, note: validNote);

            await _businessRepository.SaveChangesAsync();

            return await EmailTenantAsync(business, "Your AppointMe account has been reactivated", ReactivatedEmail(business));
        }

        // ---------------- login accounts ----------------

        public async Task<bool> ResendConfirmationAsync(Guid tenantId, string? accountId, string? adminEmail)
        {
            var (business, account) = await FindAccountAsync(tenantId, accountId);

            if (account.EmailConfirmed)
                throw new BusinessRuleException("This account's email is already confirmed.");

            await LogAccountActionAsync(business, account, AdminActionType.AccountConfirmationResent, adminEmail);

            return await _accountEmails.TrySendConfirmationAsync(account);
        }

        public async Task UnlockAccountAsync(Guid tenantId, string? accountId, string? adminEmail)
        {
            var (business, account) = await FindAccountAsync(tenantId, accountId);

            if (!await _userManager.IsLockedOutAsync(account))
                throw new BusinessRuleException("This account isn't locked out.");

            // Identity saves after each call, so the transaction keeps the unlock and its log entry together.
            await using var transaction = await _businessRepository.BeginTransactionAsync();

            EnsureSucceeded(await _userManager.SetLockoutEndDateAsync(account, null));
            EnsureSucceeded(await _userManager.ResetAccessFailedCountAsync(account));
            await LogAccountActionAsync(business, account, AdminActionType.AccountUnlocked, adminEmail);

            await transaction.CommitAsync();
        }

        public async Task<bool> SendPasswordResetAsync(Guid tenantId, string? accountId, string? adminEmail)
        {
            var (business, account) = await FindAccountAsync(tenantId, accountId);

            if (!account.EmailConfirmed)
                throw new BusinessRuleException("This account's email isn't confirmed yet, so resend the confirmation email instead.");

            await LogAccountActionAsync(business, account, AdminActionType.AccountPasswordResetSent, adminEmail);

            return await _accountEmails.TrySendPasswordResetAsync(account);
        }

        /// <summary>Anything but an account of this tenant counts as missing, so a tampered id gets a 404.</summary>
        private async Task<(Business Business, AppointMeAppUser Account)> FindAccountAsync(Guid tenantId, string? accountId)
        {
            var business = await _businessRepository.GetByIdAsync(tenantId)
                ?? throw new KeyNotFoundException("Tenant not found.");

            var account = string.IsNullOrEmpty(accountId) ? null : await _userManager.FindByIdAsync(accountId);

            if (account == null || account.TenantId != tenantId || await _userManager.IsInRoleAsync(account, PlatformRoles.Admin))
                throw new KeyNotFoundException("Account not found.");

            return (business, account);
        }

        /// <summary>Only ever names the account. The token and the link stay inside the email.</summary>
        private async Task LogAccountActionAsync(
            Business business, AppointMeAppUser account, AdminActionType action, string? adminEmail)
        {
            AddLog(business, action, adminEmail, note: $"Account: {account.Email}");
            await _businessRepository.SaveChangesAsync();
        }

        private static void EnsureSucceeded(IdentityResult result)
        {
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    "Could not unlock the account: " + string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        // ---------------- deletion ----------------

        public async Task<IReadOnlyList<DeletionRequestDTO>> GetPendingDeletionRequestsAsync()
        {
            var requests = await _adminRepository.GetPendingDeletionRequestsAsync();
            foreach (var request in requests)
                request.RequestedAt = ToDisplay(request.RequestedAt);
            return requests;
        }

        public async Task<TenantEmailOutcome> DismissDeletionRequestAsync(Guid tenantId, string? note, string? adminEmail)
        {
            var business = await _businessRepository.GetByIdAsync(tenantId)
                ?? throw new KeyNotFoundException("Tenant not found.");

            var validNote = ValidateNote(note);

            if (business.DeletionRequestedAt == null)
                throw new BusinessRuleException("This business has no pending deletion request.");

            business.DeletionRequestedAt = null;
            business.DeletionRequestReason = null;
            business.UpdatedAt = UtcNow;

            AddLog(business, AdminActionType.DeletionRequestDismissed, adminEmail, note: validNote);

            await _businessRepository.SaveChangesAsync();

            return await EmailTenantAsync(business, "Your AppointMe deletion request was not approved", DismissedEmail(business));
        }

        public async Task<TenantDeletionResult> ApproveDeletionRequestAsync(Guid tenantId, string? typedName, string? adminEmail)
        {
            var business = await _businessRepository.GetByIdAsync(tenantId)
                ?? throw new KeyNotFoundException("Tenant not found.");

            EnsureNameMatches(business, typedName);

            // The tenant may have cancelled while the admin had this page open.
            if (business.DeletionRequestedAt == null)
                throw new BusinessRuleException("This business no longer has a pending deletion request, so nothing was deleted.");

            return await DeleteAsync(business, AdminActionType.DeletionRequestApproved, ApprovedRequestReason, adminEmail);
        }

        public async Task<TenantDeletionResult> DeleteTenantAsync(Guid tenantId, string? typedName, string? reason, string? adminEmail)
        {
            var business = await _businessRepository.GetByIdAsync(tenantId)
                ?? throw new KeyNotFoundException("Tenant not found.");

            EnsureNameMatches(business, typedName);

            var validReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            if (validReason?.Length > AdminActionLog.MaxReasonLength)
                throw new BusinessRuleException($"The reason can be at most {AdminActionLog.MaxReasonLength} characters.", "Reason");

            return await DeleteAsync(business, AdminActionType.TenantDeleted, validReason, adminEmail);
        }

        private async Task<TenantDeletionResult> DeleteAsync(Business business, AdminActionType action, string? reason, string? adminEmail)
        {
            if (await _adminRepository.HasCrossTenantLinksAsync(business.Id))
            {
                _logger.LogError("Business {BusinessId} was not deleted: another tenant's rows point at its rows.", business.Id);
                throw new BusinessRuleException(
                    "This business has data linked to another business, so nothing was deleted. This needs fixing in the database first.");
            }

            // Everything needed afterwards, because the rows are about to go.
            var (id, name) = (business.Id, business.Name);
            var recipients = await RecipientsAsync(business);

            try
            {
                await _adminRepository.DeleteTenantAsync(id, new AdminActionLog
                {
                    Id = Guid.NewGuid(),
                    At = UtcNow,
                    Action = action,
                    BusinessId = id,
                    BusinessName = name,
                    AdminEmail = adminEmail,
                    Reason = reason
                });
            }
            catch (DbUpdateException ex)
            {
                // For example the tenant saved a new appointment during the delete. The transaction rolled back.
                _logger.LogError(ex, "Business {BusinessId} could not be deleted.", id);
                throw new BusinessRuleException("The business could not be deleted, and nothing was changed. Please try again.");
            }

            // The tenant is gone, so a failure in either of these must not look like the delete failed.
            var logoRemoved = true;
            try
            {
                await _logoStorage.DeleteAllAsync(id);
            }
            catch (Exception ex)
            {
                logoRemoved = false;
                _logger.LogError(ex, "The logo files of deleted business {BusinessId} could not be removed.", id);
            }

            var email = await EmailAsync(id, recipients, "Your AppointMe account has been deleted", DeletedEmail(name));
            return new TenantDeletionResult(email, logoRemoved);
        }

        private static void EnsureNameMatches(Business business, string? typedName)
        {
            if (!string.Equals(typedName?.Trim(), business.Name.Trim(), StringComparison.Ordinal))
                throw new BusinessRuleException("The name you typed doesn't match the business name.", "ConfirmName");
        }

        // ---------------- helpers ----------------

        private void AddLog(
            Business business, AdminActionType action, string? adminEmail,
            string? reason = null, string? note = null, string? changedFields = null) =>
            _adminRepository.AddActionLog(new AdminActionLog
            {
                Id = Guid.NewGuid(),
                At = UtcNow,
                Action = action,
                BusinessId = business.Id,
                BusinessName = business.Name,
                AdminEmail = adminEmail,
                Reason = reason,
                Note = note,
                ChangedFields = changedFields
            });

        /// <summary>The tenant's login emails, or the business email when it has no users.</summary>
        private async Task<IReadOnlyList<string>> RecipientsAsync(Business business)
        {
            var emails = await _adminRepository.GetUserEmailsAsync(business.Id);
            if (emails.Count > 0)
                return emails;

            return string.IsNullOrWhiteSpace(business.Email) ? Array.Empty<string>() : new[] { business.Email };
        }

        /// <summary>The action is already saved, so a failed email only changes the outcome the admin is told.</summary>
        private async Task<TenantEmailOutcome> EmailTenantAsync(Business business, string subject, string content) =>
            await EmailAsync(business.Id, await RecipientsAsync(business), subject, content);

        private async Task<TenantEmailOutcome> EmailAsync(
            Guid businessId, IReadOnlyList<string> recipients, string subject, string content)
        {
            var outcome = TenantEmailOutcome.NoRecipient;

            try
            {
                foreach (var recipient in recipients)
                {
                    await _emailService.SendEmailAsync(new EmailMessage { MailTo = recipient, Subject = subject, Content = content });
                    outcome = TenantEmailOutcome.Sent;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not send the email \"{Subject}\" for business {BusinessId}.", subject, businessId);
                return TenantEmailOutcome.Failed;
            }

            return outcome;
        }

        private string SupportSentence() => string.IsNullOrWhiteSpace(_support.Email)
            ? "Please contact support if you have any questions."
            : $"If you have any questions, please contact support at {_support.Email}.";

        private string DismissedEmail(Business business) => string.Join(Environment.NewLine,
            $"Your request to delete the AppointMe account for {business.Name} was not approved, so the account stays as it is.",
            "",
            SupportSentence());

        private string DeletedEmail(string businessName) => string.Join(Environment.NewLine,
            $"The AppointMe account for {businessName} has been permanently deleted, together with its customers, appointments, services and invoices.",
            "",
            SupportSentence());

        private string SuspendedEmail(Business business)
        {
            var lines = new List<string>
            {
                $"The AppointMe account for {business.Name} has been suspended, so it can't be used at the moment.",
                "",
                $"Reason: {business.SuspensionReason!.Value.DisplayText()}"
            };

            if (!string.IsNullOrEmpty(business.SuspensionNote))
                lines.Add($"Note: {business.SuspensionNote}");

            lines.Add("");
            lines.Add(SupportSentence());

            return string.Join(Environment.NewLine, lines);
        }

        private static string ReactivatedEmail(Business business) =>
            $"The AppointMe account for {business.Name} has been reactivated. You can log in and use it as before.";

        private static (SuspensionReason Reason, string? Note) ValidatedSuspension(SuspensionReason? reason, string? note)
        {
            if (reason is not { } value || !Enum.IsDefined(value))
                throw new BusinessRuleException("Please choose a reason from the list.", "Reason");

            var validNote = ValidateNote(note);

            if (value == SuspensionReason.Other && validNote == null)
                throw new BusinessRuleException("Please add a note explaining the reason.", "Note");

            return (value, validNote);
        }

        private static string? ValidateNote(string? input)
        {
            var note = input?.Trim();
            if (string.IsNullOrEmpty(note))
                return null;

            if (note.Length > AdminActionLog.MaxNoteLength)
                throw new BusinessRuleException($"The note can be at most {AdminActionLog.MaxNoteLength} characters.", "Note");

            return note;
        }

        private static DateTime ToDisplay(DateTime utc) => BusinessTimeZones.FromUtc(utc, BusinessTimeZones.Default);

        private static IReadOnlyList<AdminActionLogDTO> ToDisplay(IReadOnlyList<AdminActionLogDTO> entries)
        {
            foreach (var entry in entries)
                entry.At = ToDisplay(entry.At);
            return entries;
        }

        private static string? ValidateEmail(string? input)
        {
            var email = input?.Trim();
            if (string.IsNullOrEmpty(email))
                return null;

            if (email.Length > MaxEmailLength)
                throw new BusinessRuleException(
                    $"Email can be at most {MaxEmailLength} characters.", nameof(UpdateTenantDTO.Email));
            if (!new EmailAddressAttribute().IsValid(email))
                throw new BusinessRuleException("Please enter a valid email address.", nameof(UpdateTenantDTO.Email));

            return email;
        }

        private static string? ValidateAddress(string? input)
        {
            var address = input?.Trim();
            if (string.IsNullOrEmpty(address))
                return null;

            if (address.Length > MaxAddressLength)
                throw new BusinessRuleException(
                    $"Address can be at most {MaxAddressLength} characters.", nameof(UpdateTenantDTO.Address));

            return address;
        }
    }
}
