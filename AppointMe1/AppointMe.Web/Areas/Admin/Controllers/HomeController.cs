using AppointMe.Service.Interface;
using AppointMe.Web.ViewModels.Admin;
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

        public async Task<IActionResult> Index() => View(new AdminHomeVm
        {
            Totals = await _adminService.GetTotalsAsync(),
            PendingDeletionRequests = await _adminService.GetPendingDeletionRequestsAsync()
        });
    }
}
