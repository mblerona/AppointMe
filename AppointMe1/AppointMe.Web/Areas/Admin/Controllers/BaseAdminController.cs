using AppointMe.Domain.Identity;
using AppointMe.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Areas.Admin.Controllers
{
    /// <summary>Base for every controller in the /Admin area. Tenants get the default AccessDenied redirect.</summary>
    [Area("Admin")]
    [Authorize(Roles = PlatformRoles.Admin)]
    [ServiceFilter(typeof(RequirePlatformAdminFilter))]
    public abstract class BaseAdminController : Controller
    {
    }
}
