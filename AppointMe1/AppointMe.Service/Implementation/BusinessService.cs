using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.TimeZones;
using AppointMe.Domain.Identity;
using AppointMe.Domain.Validation;
using AppointMe.Repository.Interface;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace AppointMe.Service.Implementation
{
    public class BusinessService : IBusinessService
    {
        public const int MaxBusinessNameLength = 200;
        public const int MinSlotMinutes = 5;
        public const int MaxSlotMinutes = 240;
        public const long MaxLogoBytes = 2 * 1024 * 1024;
        public const int MaxEmailLength = 256;

        private const int FallbackSlotMinutes = 30;
        private static readonly HashSet<string> AllowedLogoExtensions = new() { ".png", ".jpg", ".jpeg", ".webp" };

        private readonly IBusinessRepository _businessRepository;
        private readonly UserManager<AppointMeAppUser> _userManager;
        private readonly ILogoStorage _logoStorage;
        private readonly TimeProvider _timeProvider;

        public BusinessService(
            IBusinessRepository businessRepository,
            UserManager<AppointMeAppUser> userManager,
            ILogoStorage logoStorage,
            TimeProvider timeProvider)
        {
            _businessRepository = businessRepository;
            _userManager = userManager;
            _logoStorage = logoStorage;
            _timeProvider = timeProvider;
        }

        // ---------------- registration ----------------

        public async Task<IdentityResult> RegisterOwnerAsync(AppointMeAppUser user, string password, string businessName)
        {
            var name = ValidateName(businessName);
            if (user.TenantId != null)
                throw new InvalidOperationException("A new owner must not belong to a tenant yet.");

            var now = DateTime.UtcNow;
            var business = new Business
            {
                Id = Guid.NewGuid(),
                Name = name,
                EnableServices = true,
                EnableInvoices = false,
                EnableStaffing = true,
                EnablePayments = true,
                DefaultSlotMinutes = FallbackSlotMinutes,
                CreatedAt = now,
                UpdatedAt = now
            };

            user.TenantId = business.Id;
            user.CreatedAt = now;
            user.UpdatedAt = now;

            await using var transaction = await _businessRepository.BeginTransactionAsync();
            var committed = false;
            try
            {
                // UserManager's store uses the same scoped DbContext as the repository, so the
                // SaveChanges inside CreateAsync stores the Business and the user together.
                _businessRepository.Add(business);

                var result = await _userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                    return result;

                await transaction.CommitAsync();
                committed = true;
                return result;
            }
            finally
            {
                if (!committed)
                {
                    // Validation failures return before anything is saved; make sure the Business
                    // isn't saved later by another SaveChanges on this context.
                    _businessRepository.Detach(business);
                    user.TenantId = null;
                }
            }
        }

        // ---------------- settings ----------------

        public async Task<BusinessSettingsDTO> GetSettingsAsync(Guid tenantId)
        {
            var b = await _businessRepository.GetByIdReadOnlyAsync(tenantId)
                ?? throw new KeyNotFoundException("Business not found.");

            return new BusinessSettingsDTO
            {
                Name = b.Name,
                Email = b.Email,
                Phone = b.Phone,
                Address = b.Address,
                LogoUrl = b.LogoUrl,
                PrimaryColor = b.PrimaryColor,
                SecondaryColor = b.SecondaryColor,
                DefaultSlotMinutes = b.DefaultSlotMinutes,
                EnableServices = b.EnableServices,
                // The stored choice, so the form can keep it while services are off;
                // whether invoicing is in effect is BusinessFeatures.InvoicesEnabled.
                EnableInvoices = b.EnableInvoices,
                WorkDayStart = b.WorkDayStart,
                WorkDayEnd = b.WorkDayEnd,
                TimeZoneId = b.TimeZoneId,
                OpenMon = b.OpenMon,
                OpenTue = b.OpenTue,
                OpenWed = b.OpenWed,
                OpenThu = b.OpenThu,
                OpenFri = b.OpenFri,
                OpenSat = b.OpenSat,
                OpenSun = b.OpenSun,
                DeletionRequestedAt = b.DeletionRequestedAt is { } requestedAt
                    ? BusinessTimeZones.FromUtc(requestedAt, b.TimeZoneId)
                    : null
            };
        }

        public async Task UpdateSettingsAsync(Guid tenantId, BusinessSettingsDTO settings, LogoUpload? logo)
        {
            var business = await _businessRepository.GetByIdAsync(tenantId)
                ?? throw new KeyNotFoundException("Business not found.");

            // Validate everything before changing anything.
            var name = ValidateName(settings.Name, nameof(BusinessSettingsDTO.Name));

            if (settings.WorkDayEnd <= settings.WorkDayStart)
                throw new BusinessRuleException("Work day end must be after start.", nameof(BusinessSettingsDTO.WorkDayEnd));

            if (settings.DefaultSlotMinutes < MinSlotMinutes || settings.DefaultSlotMinutes > MaxSlotMinutes)
                throw new BusinessRuleException(
                    $"Time slot interval must be between {MinSlotMinutes} and {MaxSlotMinutes} minutes.",
                    nameof(BusinessSettingsDTO.DefaultSlotMinutes));

            if (settings.DefaultSlotMinutes > (settings.WorkDayEnd - settings.WorkDayStart).TotalMinutes)
                throw new BusinessRuleException(
                    "Time slot interval can't be longer than the opening hours.",
                    nameof(BusinessSettingsDTO.DefaultSlotMinutes));

            if (!HasAnOpenDay(settings))
                throw new BusinessRuleException("Choose at least one open day.");

            if (!string.IsNullOrWhiteSpace(settings.TimeZoneId) && !BusinessTimeZones.IsSupported(settings.TimeZoneId))
                throw new BusinessRuleException("Please choose a time zone from the list.", nameof(BusinessSettingsDTO.TimeZoneId));

            var email = ValidateEmail(settings.Email);
            var phone = PhoneNumberNormalizer.NormalizeOptional(settings.Phone, nameof(BusinessSettingsDTO.Phone));
            var primaryColor = ValidateColor(settings.PrimaryColor, nameof(BusinessSettingsDTO.PrimaryColor));
            var secondaryColor = ValidateColor(settings.SecondaryColor, nameof(BusinessSettingsDTO.SecondaryColor));
            var logoExtension = logo is { Length: > 0 } ? ValidateLogo(logo) : null;

            business.Name = name;
            business.Email = email;
            business.Phone = phone;
            business.Address = settings.Address?.Trim();
            business.PrimaryColor = primaryColor;
            business.SecondaryColor = secondaryColor;
            business.DefaultSlotMinutes = settings.DefaultSlotMinutes;
            business.WorkDayStart = settings.WorkDayStart;
            business.WorkDayEnd = settings.WorkDayEnd;
            if (!string.IsNullOrWhiteSpace(settings.TimeZoneId))
                business.TimeZoneId = settings.TimeZoneId;
            business.OpenMon = settings.OpenMon;
            business.OpenTue = settings.OpenTue;
            business.OpenWed = settings.OpenWed;
            business.OpenThu = settings.OpenThu;
            business.OpenFri = settings.OpenFri;
            business.OpenSat = settings.OpenSat;
            business.OpenSun = settings.OpenSun;
            business.EnableServices = settings.EnableServices;
            // Stored as chosen, even with services off: "invoices require services" is applied when
            // reading (BusinessFeatures), so turning services back on restores this choice.
            business.EnableInvoices = settings.EnableInvoices;
            business.UpdatedAt = DateTime.UtcNow;

            if (logoExtension != null)
                business.LogoUrl = await _logoStorage.SaveAsync(business.Id, logoExtension, logo!.Content);

            await _businessRepository.SaveChangesAsync();
        }

        public async Task<BusinessCalendarConfigDTO> GetCalendarConfigAsync(Guid tenantId)
        {
            var b = await _businessRepository.GetByIdReadOnlyAsync(tenantId)
                ?? throw new KeyNotFoundException("Business not found.");

            var hidden = new List<int>();
            if (!b.OpenSun) hidden.Add(0);
            if (!b.OpenMon) hidden.Add(1);
            if (!b.OpenTue) hidden.Add(2);
            if (!b.OpenWed) hidden.Add(3);
            if (!b.OpenThu) hidden.Add(4);
            if (!b.OpenFri) hidden.Add(5);
            if (!b.OpenSat) hidden.Add(6);

            return new BusinessCalendarConfigDTO
            {
                WorkDayStart = b.WorkDayStart,
                WorkDayEnd = b.WorkDayEnd,
                Now = BusinessTimeZones.Now(_timeProvider, b.TimeZoneId),
                SlotMinutes = b.DefaultSlotMinutes <= 0 ? FallbackSlotMinutes : b.DefaultSlotMinutes,
                HiddenDays = hidden
            };
        }

        public async Task<BusinessBrandingDTO?> GetBrandingAsync(Guid tenantId)
        {
            var b = await _businessRepository.GetByIdReadOnlyAsync(tenantId);
            if (b == null) return null;

            return new BusinessBrandingDTO
            {
                Name = b.Name,
                LogoUrl = b.LogoUrl,
                PrimaryColor = b.PrimaryColor,
                SecondaryColor = b.SecondaryColor,
                EnableServices = b.ServicesEnabled(),
                EnableInvoices = b.InvoicesEnabled()
            };
        }

        public async Task<SuspensionInfoDTO?> GetSuspensionInfoAsync(Guid tenantId)
        {
            var b = await _businessRepository.GetByIdReadOnlyAsync(tenantId);
            if (b is not { Status: BusinessStatus.Suspended })
                return null;

            return new SuspensionInfoDTO
            {
                BusinessName = b.Name,
                Reason = b.SuspensionReason?.DisplayText() ?? "",
                Note = b.SuspensionNote,
                SuspendedAt = b.SuspendedAt is { } at ? BusinessTimeZones.FromUtc(at, b.TimeZoneId) : null
            };
        }

        // ---------------- helpers ----------------

        internal static string ValidateName(string? input, string? propertyName = null)
        {
            var name = input?.Trim();
            if (string.IsNullOrEmpty(name))
                throw new BusinessRuleException("Business name is required.", propertyName);
            if (name.Length > MaxBusinessNameLength)
                throw new BusinessRuleException(
                    $"Business name can be at most {MaxBusinessNameLength} characters.", propertyName);
            return name;
        }

        /// <returns>The lower-case extension. Only the extension is checked (audit item S5 is still open).</returns>
        private static string ValidateLogo(LogoUpload logo)
        {
            if (logo.Length > MaxLogoBytes)
                throw new BusinessRuleException("Logo file is too large (max 2MB).");

            var extension = Path.GetExtension(logo.FileName).ToLowerInvariant();
            if (!AllowedLogoExtensions.Contains(extension))
                throw new BusinessRuleException("Only PNG, JPG, JPEG, or WEBP files are allowed.");

            return extension;
        }

        /// <summary>
        /// "abc" / "#AABBCC" -> "#abc" / "#aabbcc". Empty means "use the default color" (null);
        /// anything else is an error for the field, never silently dropped.
        /// </summary>
        private static string? ValidateColor(string? input, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var s = input.Trim();
            if (!s.StartsWith('#')) s = "#" + s;

            if ((s.Length != 4 && s.Length != 7) || !s.Skip(1).All(Uri.IsHexDigit))
                throw new BusinessRuleException("Please choose a valid color, such as #3d4f5c.", propertyName);

            return s.ToLowerInvariant();
        }

        /// <returns>The trimmed address, or null when empty.</returns>
        private static string? ValidateEmail(string? input)
        {
            var email = input?.Trim();
            if (string.IsNullOrEmpty(email))
                return null;

            if (email.Length > MaxEmailLength)
                throw new BusinessRuleException(
                    $"Email can be at most {MaxEmailLength} characters.", nameof(BusinessSettingsDTO.Email));
            // The same check as the form ([EmailAddress]) and the admin's tenant edit.
            if (!new EmailAddressAttribute().IsValid(email))
                throw new BusinessRuleException("Please enter a valid email address.", nameof(BusinessSettingsDTO.Email));

            return email;
        }

        private static bool HasAnOpenDay(BusinessSettingsDTO s) =>
            s.OpenMon || s.OpenTue || s.OpenWed || s.OpenThu || s.OpenFri || s.OpenSat || s.OpenSun;
    }
}
