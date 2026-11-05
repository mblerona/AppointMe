using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AppointMe.Web.Areas.Identity.Pages.Account.Manage
{
    /// <summary>Replaces the default UI page, which links to the removed DeletePersonalData page.</summary>
    public class PersonalDataModel : PageModel
    {
        private readonly ITenantContext _tenantContext;

        public PersonalDataModel(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        /// <summary>An admin has no Business, so the deletion request in Business Settings doesn't apply to them.</summary>
        public bool IsPlatformAdmin { get; private set; }

        public async Task OnGetAsync()
        {
            var tenant = await _tenantContext.ResolveAsync(HttpContext.RequestAborted);
            IsPlatformAdmin = tenant.Status == TenantResolutionStatus.PlatformAdmin;
        }
    }
}
