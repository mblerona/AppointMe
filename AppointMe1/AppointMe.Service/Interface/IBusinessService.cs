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
    }
}
