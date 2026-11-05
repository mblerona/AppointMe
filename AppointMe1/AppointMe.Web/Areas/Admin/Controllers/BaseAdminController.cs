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
        /// <summary>The logged-in admin, for the action log. The user name is the email.</summary>
        protected string? AdminEmail => User.Identity?.Name;
    }
}
