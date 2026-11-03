using AppointMe.Domain.DTO;
using AppointMe.Service.Interface;

namespace AppointMe.Web.Infrastructure
{
    /// <summary>
    /// The current tenant's Business for views (layout branding, feature switches such as EnableServices).
    /// Scoped and cached, so the layout and the page share one query per request. Injected into every
    /// view through Views/_ViewImports.cshtml; views must not use ApplicationDbContext.
    /// </summary>
    public sealed class CurrentBusiness
    {
        private readonly ITenantContext _tenantContext;
        private readonly IBusinessService _businessService;

        private bool _loaded;
        private BusinessBrandingDTO? _business;

        public CurrentBusiness(ITenantContext tenantContext, IBusinessService businessService)
        {
            _tenantContext = tenantContext;
            _businessService = businessService;
        }

        /// <returns>The Business, or null for anonymous users and users without a tenant.</returns>
        public async Task<BusinessBrandingDTO?> GetAsync()
        {
            if (_loaded)
                return _business;

            var tenant = await _tenantContext.ResolveAsync();
            _business = tenant.Status == TenantResolutionStatus.Resolved
                ? await _businessService.GetBrandingAsync(tenant.TenantId!.Value)
                : null;
            _loaded = true;
            return _business;
        }
    }
}
