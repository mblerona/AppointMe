using AppointMe.Domain;
using AppointMe.Service.Interface;
using AppointMe.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace AppointMe.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly ITenantContext _tenantContext;

        public HomeController(ILogger<HomeController> logger, ITenantContext tenantContext)
        {
            _logger = logger;
            _tenantContext = tenantContext;
        }

        /// <summary>The start page: sends visitors to log in and users to their home.</summary>
        [AllowAnonymous]
        public async Task<IActionResult> Index()
        {
            if (User?.Identity?.IsAuthenticated != true)
                return Redirect("/Identity/Account/Login");

            var tenant = await _tenantContext.ResolveAsync();
            return tenant.Status == TenantResolutionStatus.PlatformAdmin
                ? RedirectToAction("Index", "Home", new { area = "Admin" })
                : RedirectToAction("Index", "Dashboard");
        }

        [AllowAnonymous]
        public IActionResult Privacy()
        {
            return View();
        }

        [AllowAnonymous]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        /// <summary>
        /// The friendly page for a response that has a status code but no body (404, 403, 429, ...),
        /// rendered by UseStatusCodePagesWithReExecute. The original status code is kept, and nothing
        /// technical is shown. Visited directly, it is just a 404.
        /// Any method, and no antiforgery check: re-execution keeps the original request's method, so a
        /// POST that ended in 404 arrives here as a POST. The page only shows fixed text.
        /// </summary>
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Status(int id)
        {
            if (HttpContext.Features.Get<IStatusCodeReExecuteFeature>() == null)
                return NotFound();

            return View(StatusPageVm.For(id));
        }
    }
}
