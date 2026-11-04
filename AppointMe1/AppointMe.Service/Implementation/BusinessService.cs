using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.TimeZones;
using AppointMe.Domain.Identity;
using AppointMe.Domain.Validation;
using AppointMe.Repository.Interface;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Identity;

namespace AppointMe.Service.Implementation
{
    public class BusinessService : IBusinessService
    {
        public const int MaxBusinessNameLength = 200;
        public const int MinSlotMinutes = 5;
        public const int MaxSlotMinutes = 240;
        public const long MaxLogoBytes = 2 * 1024 * 1024;

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
                // Invoices are built from services, so they can't be on without them.
                EnableInvoices = b.EnableServices && b.EnableInvoices,
                WorkDayStart = b.WorkDayStart,
                WorkDayEnd = b.WorkDayEnd,
                TimeZoneId = b.TimeZoneId,
                OpenMon = b.OpenMon,
                OpenTue = b.OpenTue,
                OpenWed = b.OpenWed,
                OpenThu = b.OpenThu,
                OpenFri = b.OpenFri,
                OpenSat = b.OpenSat,
                OpenSun = b.OpenSun
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

            if (!string.IsNullOrWhiteSpace(settings.TimeZoneId) && !BusinessTimeZones.IsSupported(settings.TimeZoneId))
                throw new BusinessRuleException("Please choose a time zone from the list.", nameof(BusinessSettingsDTO.TimeZoneId));

            var phone = PhoneNumberNormalizer.NormalizeOptional(settings.Phone, nameof(BusinessSettingsDTO.Phone));
            var logoExtension = logo is { Length: > 0 } ? ValidateLogo(logo) : null;

            business.Name = name;
            business.Email = settings.Email?.Trim();
            business.Phone = phone;
            business.Address = settings.Address?.Trim();
            business.PrimaryColor = NormalizeHex(settings.PrimaryColor);
            business.SecondaryColor = NormalizeHex(settings.SecondaryColor);
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
            business.EnableInvoices = settings.EnableServices && settings.EnableInvoices;
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
                EnableServices = b.EnableServices,
                EnableInvoices = b.EnableServices && b.EnableInvoices
            };
        }

        // ---------------- helpers ----------------

        private static string ValidateName(string? input, string? propertyName = null)
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

        /// <summary>"abc" / "#AABBCC" -> "#abc" / "#aabbcc"; anything else -> null.</summary>
        private static string? NormalizeHex(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var s = input.Trim();
            if (!s.StartsWith('#')) s = "#" + s;

            if (s.Length != 4 && s.Length != 7) return null;

            var hex = s.Substring(1);
            if (!hex.All(Uri.IsHexDigit)) return null;

            return s.ToLowerInvariant();
        }
    }
}
