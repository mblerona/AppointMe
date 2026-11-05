using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Areas.Admin.Controllers
{
    public class HomeController : BaseAdminController
    {
        private readonly IPlatformAdminService _adminService;

        public HomeController(IPlatformAdminService adminService)
        {
            _adminService = adminService;
        }

        public async Task<IActionResult> Index() => View(await _adminService.GetTotalsAsync());
    }
}
