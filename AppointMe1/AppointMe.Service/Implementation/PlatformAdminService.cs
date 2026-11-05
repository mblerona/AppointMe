using System.ComponentModel.DataAnnotations;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.TimeZones;
using AppointMe.Domain.Validation;
using AppointMe.Repository.Interface;
using AppointMe.Service.Interface;

namespace AppointMe.Service.Implementation
{
    public class PlatformAdminService : IPlatformAdminService
    {
        public const int MaxEmailLength = 256;
        public const int MaxAddressLength = 500;
        public const int NewTenantDays = 30;

        private readonly IPlatformAdminRepository _adminRepository;
        private readonly IBusinessRepository _businessRepository;
        private readonly TimeProvider _timeProvider;

        public PlatformAdminService(
            IPlatformAdminRepository adminRepository,
            IBusinessRepository businessRepository,
            TimeProvider timeProvider)
        {
            _adminRepository = adminRepository;
            _businessRepository = businessRepository;
            _timeProvider = timeProvider;
        }

        public Task<PlatformTotalsDTO> GetTotalsAsync() =>
            _adminRepository.GetTotalsAsync(_timeProvider.GetUtcNow().UtcDateTime.AddDays(-NewTenantDays));

        public async Task<IReadOnlyList<TenantSummaryDTO>> GetTenantsAsync(string? search, BusinessStatus? status)
        {
            var tenants = await _adminRepository.GetTenantsAsync(search, status);
            foreach (var tenant in tenants)
                ToDisplayDates(tenant);
            return tenants;
        }

        public async Task<TenantDetailsDTO> GetTenantAsync(Guid tenantId)
        {
            var tenant = await _adminRepository.GetTenantAsync(tenantId)
                ?? throw new KeyNotFoundException("Tenant not found.");
            ToDisplayDates(tenant);
            return tenant;
        }

        public async Task UpdateTenantAsync(Guid tenantId, UpdateTenantDTO update)
        {
            var business = await _businessRepository.GetByIdAsync(tenantId)
                ?? throw new KeyNotFoundException("Tenant not found.");

            // Validate everything before changing anything.
            var name = BusinessService.ValidateName(update.Name, nameof(UpdateTenantDTO.Name));
            var email = ValidateEmail(update.Email);
            var phone = PhoneNumberNormalizer.NormalizeOptional(update.Phone, nameof(UpdateTenantDTO.Phone));
            var address = ValidateAddress(update.Address);

            business.Name = name;
            business.Email = email;
            business.Phone = phone;
            business.Address = address;
            business.UpdatedAt = _timeProvider.GetUtcNow().UtcDateTime;

            await _businessRepository.SaveChangesAsync();
        }

        private static void ToDisplayDates(TenantSummaryDTO tenant) =>
            tenant.CreatedAt = BusinessTimeZones.FromUtc(tenant.CreatedAt, BusinessTimeZones.Default);

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
