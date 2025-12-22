using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppointMe.Web.Controllers
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class DashboardController : BaseTenantController
    {
        public DashboardController(UserManager<AppointMeAppUser> userManager, ApplicationDbContext db)
            : base(userManager, db) { }

        public async Task<IActionResult> Index(string range = "all", string status = "all", string? search = null)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();

            var business = await _db.Businesses.FirstAsync(b => b.Id == tenantId);

          
            DateTime? start = null;
            DateTime? end = null;

            var now = DateTime.Now;
            if (range == "today")
            {
                start = now.Date;
                end = now.Date.AddDays(1);
            }
            else if (range == "week")
            {
                // last 7 days including today
                start = now.Date;
                end = now.Date.AddDays(7);
            }
            else if (range == "month")
            {
                start = new DateTime(now.Year, now.Month, 1);
                end = start.Value.AddMonths(1);
            }

         
            var q = _db.Appointments
                .AsNoTracking()
                .Where(a => a.TenantId == tenantId)
                .Include(a => a.Customer)
                .AsQueryable();

           
            if (start.HasValue && end.HasValue)
                q = q.Where(a => a.AppointmentDate >= start.Value && a.AppointmentDate < end.Value);

          
            if (!string.IsNullOrWhiteSpace(status) && status != "all" &&
                Enum.TryParse<AppointmentStatus>(status, true, out var parsed))
            {
                q = q.Where(a => a.Status == parsed);
            }

           
            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(a =>
                    a.OrderNumber.Contains(s) ||
                    a.Customer!.FirstName.Contains(s) ||
                    a.Customer!.LastName.Contains(s) ||
                    a.Customer!.PhoneNumber.Contains(s) ||
                    (a.Customer!.SecondPhoneNumber != null && a.Customer.SecondPhoneNumber.Contains(s)) ||
                    a.Customer!.State.Contains(s));
            }

          
            var allAppointmentsTenant = _db.Appointments.AsNoTracking().Where(a => a.TenantId == tenantId);

            var totalAppointments = await allAppointmentsTenant.CountAsync();
            var scheduledAppointments = await allAppointmentsTenant.CountAsync(a => a.Status == AppointmentStatus.Scheduled);
            var completedAppointments = await allAppointmentsTenant.CountAsync(a => a.Status == AppointmentStatus.Completed);
            var cancelledAppointments = await allAppointmentsTenant.CountAsync(a => a.Status == AppointmentStatus.Cancelled);
            
            var customersCount = await _db.Customers.AsNoTracking()
                .Where(c => c.TenantId == tenantId)
                .CountAsync();

       
            var rows = await q
                .OrderByDescending(a => a.AppointmentDate)
                .Take(20)
                .Select(a => new AppointmentRowVm
                {
                    Id = a.Id,
                    CustomerName = a.Customer != null ? (a.Customer.FirstName + " " + a.Customer.LastName) : "—",
                    Description = a.Description,
                    OrderNumber = a.OrderNumber,
                    AppointmentDate = a.AppointmentDate,
                    Email = a.Customer != null ? a.Customer.Email : "",
                    Phone1 = a.Customer != null ? a.Customer.PhoneNumber : "",
                    Phone2 = a.Customer != null ? a.Customer.SecondPhoneNumber : null,
                    Location = a.Customer != null ? a.Customer.State : "",
                    Status = a.Status.ToString()
                })
                .ToListAsync();

            var vm = new DashboardVm
            {
                BusinessName = business.Name,
                LogoUrl = business.LogoUrl,

                TotalAppointments = totalAppointments,
                ScheduledAppointments = scheduledAppointments,
                CompletedAppointments = completedAppointments,
                CancelledAppointments = cancelledAppointments,
                CustomersCount = customersCount,

                Range = range,
                Status = status,
                Search = search,

                Rows = rows
            };

            return View(vm);
        }
    }
}
