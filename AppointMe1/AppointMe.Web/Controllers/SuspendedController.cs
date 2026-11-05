using AppointMe.Service.Admin;
using AppointMe.Service.Interface;
using AppointMe.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AppointMe.Web.Controllers
{
    /// <summary>Where a suspended tenant ends up (see TenantStatusMiddleware). It shows only their own suspension.</summary>
    [Authorize]
    public class SuspendedController : Controller
    {
        private readonly ITenantContext _tenantContext;
        private readonly IBusinessService _businessService;
        private readonly SupportOptions _support;

        public SuspendedController(
            ITenantContext tenantContext,
            IBusinessService businessService,
            IOptions<SupportOptions> support)
        {
            _tenantContext = tenantContext;
            _businessService = businessService;
            _support = support.Value;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var tenant = await _tenantContext.ResolveAsync();
            if (tenant.Status == TenantResolutionStatus.NotAuthenticated)
                return Challenge();

            // Anyone else, including a tenant who has just been reactivated, goes back to the app.
            var info = tenant.Status == TenantResolutionStatus.Suspended
                ? await _businessService.GetSuspensionInfoAsync(tenant.TenantId!.Value)
                : null;
            if (info == null)
                return RedirectToAction("Index", "Home");

            return View(new SuspendedVm { Suspension = info, SupportEmail = _support.Email?.Trim() });
        }
    }
}
