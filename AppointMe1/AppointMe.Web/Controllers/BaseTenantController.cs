using AppointMe.Service.Interface;
using AppointMe.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Controllers
{
    /// <summary>
    /// Base for controllers that work on tenant-owned data. <see cref="RequireTenantFilter"/> resolves the
    /// tenant before every action, so actions can read <see cref="TenantId"/> directly.
    /// </summary>
    [Authorize]
    [ServiceFilter(typeof(RequireTenantFilter))]
    public abstract class BaseTenantController : Controller
    {
        private readonly ITenantContext _tenantContext;

        protected BaseTenantController(ITenantContext tenantContext)
        {
            _tenantContext = tenantContext;
        }

        /// <summary>The logged-in user's tenant, resolved on the server.</summary>
        protected Guid TenantId => _tenantContext.TenantId;
    }
}
