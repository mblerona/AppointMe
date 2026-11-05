using AppointMe.Domain;
using AppointMe.Service.Interface;
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

        public async Task<IActionResult> Index()
        {
            if (User?.Identity?.IsAuthenticated != true)
                return Redirect("/Identity/Account/Login");

            var tenant = await _tenantContext.ResolveAsync();
            return tenant.Status == TenantResolutionStatus.PlatformAdmin
                ? RedirectToAction("Index", "Home", new { area = "Admin" })
                : RedirectToAction("Index", "Dashboard");
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
