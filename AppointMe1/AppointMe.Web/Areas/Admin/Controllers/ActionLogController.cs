using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Areas.Admin.Controllers
{
    public class ActionLogController : BaseAdminController
    {
        /// <summary>There is no paging; the page shows this many of the newest actions.</summary>
        public const int PageLimit = 200;

        private readonly IPlatformAdminService _adminService;

        public ActionLogController(IPlatformAdminService adminService)
        {
            _adminService = adminService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            ViewData["Limit"] = PageLimit;
            return View(await _adminService.GetActionLogAsync(PageLimit));
        }
    }
}
