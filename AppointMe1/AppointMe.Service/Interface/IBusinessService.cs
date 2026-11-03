using AppointMe.Domain.DTO;
using AppointMe.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace AppointMe.Service.Interface
{
    public interface IBusinessService
    {
        /// <summary>
        /// Registers a new business owner: creates the Business and the user in one transaction and links
        /// them (<c>user.TenantId = Business.Id</c>). If the user can't be created (for example a duplicate
        /// email or a weak password), nothing is saved and the Identity errors are returned.
        /// </summary>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">The business name is empty or too long.</exception>
        Task<IdentityResult> RegisterOwnerAsync(AppointMeAppUser user, string password, string businessName);

        /// <exception cref="KeyNotFoundException">The tenant has no Business.</exception>
        Task<BusinessSettingsDTO> GetSettingsAsync(Guid tenantId);

        /// <summary>
        /// Validates and saves the settings (and the logo, if one is given). <see cref="BusinessSettingsDTO.LogoUrl"/>
        /// from the input is ignored. Nothing is saved when a rule fails.
        /// </summary>
        /// <exception cref="KeyNotFoundException">The tenant has no Business.</exception>
        /// <exception cref="Domain.Exceptions.BusinessRuleException">A rule failed; PropertyName names the field.</exception>
        Task UpdateSettingsAsync(Guid tenantId, BusinessSettingsDTO settings, LogoUpload? logo);

        /// <exception cref="KeyNotFoundException">The tenant has no Business.</exception>
        Task<BusinessCalendarConfigDTO> GetCalendarConfigAsync(Guid tenantId);
    }
}
