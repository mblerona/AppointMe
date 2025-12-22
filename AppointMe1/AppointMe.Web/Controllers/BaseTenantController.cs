using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppointMe.Web.Controllers
{
    [Authorize]
    public abstract class BaseTenantController : Controller
    {
        protected readonly UserManager<AppointMeAppUser> _userManager;
        protected readonly ApplicationDbContext _db;

        protected BaseTenantController(UserManager<AppointMeAppUser> userManager, ApplicationDbContext db)
        {
            _userManager = userManager;
            _db = db;
        }

       
        /// If tenant is missing (or Business missing), redirect user to Settings/Business.
        
        protected async Task<IActionResult?> EnsureTenantAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null)
                return Challenge();

            if (user.TenantId == null || user.TenantId == Guid.Empty)
                return RedirectToAction("Business", "Settings");

           
            var exists = await _db.Businesses.AnyAsync(b => b.Id == user.TenantId.Value);
            if (!exists)
                return RedirectToAction("Business", "Settings");

            return null;
        }

        protected async Task<Guid> GetTenantIdAsync()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user?.TenantId == null || user.TenantId == Guid.Empty)
                throw new InvalidOperationException("Tenant is not assigned.");

            return user.TenantId.Value;
        }
    }
}
