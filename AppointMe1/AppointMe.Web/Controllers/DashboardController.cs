using AppointMe.Domain.DTO;
using AppointMe.Service.Interface;
using AppointMe.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Controllers
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class DashboardController : BaseTenantController
    {
        private readonly IDashboardService _dashboardService;

        public DashboardController(ITenantContext tenantContext, IDashboardService dashboardService)
            : base(tenantContext)
        {
            _dashboardService = dashboardService;
        }

        public async Task<IActionResult> Index(string range = "all", string status = "all", string? search = null)
        {
            var dashboard = await _dashboardService.GetDashboardAsync(TenantId, range, status, search);

            return View(new DashboardVm
            {
                BusinessName = dashboard.BusinessName,
                LogoUrl = dashboard.LogoUrl,
                Today = dashboard.Today,

                TotalAppointments = dashboard.TotalAppointments,
                ScheduledAppointments = dashboard.ScheduledAppointments,
                CompletedAppointments = dashboard.CompletedAppointments,
                CancelledAppointments = dashboard.CancelledAppointments,
                CustomersCount = dashboard.CustomersCount,

                Range = range,
                Status = status,
                Search = search,

                Rows = dashboard.Rows.Select(ToRowVm).ToList()
            });
        }

        private static AppointmentRowVm ToRowVm(DashboardAppointmentRowDTO r) => new()
        {
            Id = r.Id,
            CustomerName = r.CustomerName,
            Description = r.Description,
            OrderNumber = r.OrderNumber,
            AppointmentDate = r.AppointmentDate,
            Email = r.Email,
            Phone1 = r.Phone1,
            Phone2 = r.Phone2,
            Location = r.Location,
            Status = r.Status
        };
    }
}
