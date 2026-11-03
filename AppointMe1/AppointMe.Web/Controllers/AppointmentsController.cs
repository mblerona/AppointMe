using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace AppointMe.Web.Controllers
{
    public class AppointmentsController : BaseTenantController
    {
        private readonly IAppointmentService _appointmentService;
        private readonly ICustomerService _customerService;
        private readonly IServiceOfferingRepository _serviceOfferingRepository;
        private readonly IHolidayService _holidaysService;
        // TODO(step 3): replace _db with IBusinessService.
        private readonly ApplicationDbContext _db;

        public AppointmentsController(
            IAppointmentService appointmentService,
            ICustomerService customerService,
            IServiceOfferingRepository serviceOfferingRepository,
            IHolidayService holidaysService,
            ITenantContext tenantContext,
            ApplicationDbContext db)
            : base(tenantContext)
        {
            _db = db;
            _appointmentService = appointmentService;
            _customerService = customerService;
            _serviceOfferingRepository = serviceOfferingRepository;
            _holidaysService = holidaysService;
        }

        private async Task PopulateCustomersSelectListAsync(Guid tenantId, Guid? selectedCustomerId = null)
        {
            var customers = await _customerService.GetAllCustomersAsync(tenantId);

            var items = customers
                .Select(c => new
                {
                    c.Id,
                    FullName = $"{c.FirstName} {c.LastName}"
                })
                .ToList();

            ViewBag.Customers = new SelectList(items, "Id", "FullName", selectedCustomerId);
        }

        private async Task PopulateServicesMultiSelectAsync(Guid tenantId, IEnumerable<Guid>? selectedIds = null)
        {
            var services = await _serviceOfferingRepository.GetAllByBusinessAsync(tenantId);

            var items = services
                .Select(s => new
                {
                    s.Id,
                    Text = $"{s.Name} • {s.Price:0.##} ден"
                })
                .ToList();

            ViewBag.Services = new MultiSelectList(items, "Id", "Text", selectedIds);
        }

        private void PopulateStatusSelectList(AppointmentStatus? selected = null)
        {
            var items = Enum.GetValues(typeof(AppointmentStatus))
                .Cast<AppointmentStatus>()
                .Select(s => new SelectListItem
                {
                    Value = s.ToString(),
                    Text = s.ToString(),
                    Selected = selected.HasValue && selected.Value == s
                })
                .ToList();

            ViewBag.StatusList = items;
        }

        public async Task<IActionResult> Index(string? status, DateTime? startDate, DateTime? endDate)
        {
            var tenantId = TenantId;
            IEnumerable<AppointmentDTO> appointments;

            if (startDate.HasValue && endDate.HasValue)
            {
                appointments = await _appointmentService.GetAppointmentsByDateRangeAsync(startDate.Value, endDate.Value, tenantId);
            }
            else if (!string.IsNullOrWhiteSpace(status) &&
                     Enum.TryParse(status, ignoreCase: true, out AppointmentStatus appointmentStatus))
            {
                appointments = await _appointmentService.GetAppointmentsByStatusAsync(appointmentStatus, tenantId);
            }
            else
            {
                appointments = await _appointmentService.GetAllAppointmentsAsync(tenantId);
            }

            ViewData["Status"] = status;
            ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd");

            return View(appointments);
        }

        public async Task<IActionResult> Details(Guid id)
        {
            var tenantId = TenantId;

            try
            {
                var appointment = await _appointmentService.GetAppointmentByIdAsync(id, tenantId);
                return View(appointment);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        public async Task<IActionResult> Create(Guid? customerId = null)
        {
            var tenantId = TenantId;

            await PopulateCustomersSelectListAsync(tenantId, customerId);
            await PopulateServicesMultiSelectAsync(tenantId);

            var model = new CreateAppointmentDTO();
            if (customerId.HasValue)
                model.CustomerId = customerId.Value;

            var biz = await _db.Businesses.AsNoTracking().FirstAsync(b => b.Id == tenantId);

            ViewBag.WorkStart = biz.WorkDayStart.ToString(@"hh\:mm");
            ViewBag.WorkEnd = biz.WorkDayEnd.ToString(@"hh\:mm");
            ViewBag.HiddenDays = System.Text.Json.JsonSerializer.Serialize(BuildHiddenDays(biz));

            // Slot minutes passed to the view so the JS can snap correctly
            ViewBag.SlotMinutes = biz.DefaultSlotMinutes <= 0 ? 30 : biz.DefaultSlotMinutes;

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateAppointmentDTO dto)
        {
            var tenantId = TenantId;

            // Always repopulate tenant config when returning the view
            async Task LoadTenantConfig()
            {
                var biz = await _db.Businesses.AsNoTracking().FirstAsync(b => b.Id == tenantId);
                ViewBag.WorkStart = biz.WorkDayStart.ToString(@"hh\:mm");
                ViewBag.WorkEnd = biz.WorkDayEnd.ToString(@"hh\:mm");
                ViewBag.HiddenDays = System.Text.Json.JsonSerializer.Serialize(BuildHiddenDays(biz));
                ViewBag.SlotMinutes = biz.DefaultSlotMinutes <= 0 ? 30 : biz.DefaultSlotMinutes;
            }

            if (!ModelState.IsValid)
            {
                await PopulateCustomersSelectListAsync(tenantId, dto.CustomerId);
                await PopulateServicesMultiSelectAsync(tenantId, dto.ServiceOfferingIds);
                await LoadTenantConfig();
                return View(dto);
            }

            try
            {
                var created = await _appointmentService.CreateAppointmentAsync(dto, tenantId);

                if (created.ConfirmationEmailFailed)
                    TempData["WarningMessage"] = "Appointment created, but the confirmation email could not be sent.";
                else
                    TempData["SuccessMessage"] = "Appointment created successfully!";

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex) when (ex is KeyNotFoundException || ex is BusinessRuleException)
            {
                ModelState.AddModelError(string.Empty,
                    ex is BusinessRuleException ? ex.Message : "The selected customer was not found.");

                await PopulateCustomersSelectListAsync(tenantId, dto.CustomerId);
                await PopulateServicesMultiSelectAsync(tenantId, dto.ServiceOfferingIds);
                await LoadTenantConfig();

                return View(dto);
            }
        }

        public async Task<IActionResult> Edit(Guid id)
        {
            var tenantId = TenantId;

            var biz = await _db.Businesses.AsNoTracking().FirstAsync(b => b.Id == tenantId);
            ViewBag.WorkStart = biz.WorkDayStart.ToString(@"hh\:mm");
            ViewBag.WorkEnd = biz.WorkDayEnd.ToString(@"hh\:mm");
            ViewBag.HiddenDays = System.Text.Json.JsonSerializer.Serialize(BuildHiddenDays(biz));
            ViewBag.SlotMinutes = biz.DefaultSlotMinutes <= 0 ? 30 : biz.DefaultSlotMinutes;

            try
            {
                var appointment = await _appointmentService.GetAppointmentByIdAsync(id, tenantId);

                AppointmentStatus parsedStatus = AppointmentStatus.Scheduled;
                if (!string.IsNullOrWhiteSpace(appointment.Status))
                    Enum.TryParse(appointment.Status, ignoreCase: true, out parsedStatus);

                var updateDto = new UpdateAppointmentDTO
                {
                    OrderNumber = appointment.OrderNumber,
                    AppointmentDate = appointment.AppointmentDate,
                    Description = appointment.Description,
                    Status = parsedStatus,
                    ServiceOfferingIds = appointment.Services?.Select(x => x.ServiceId).ToList() ?? new List<Guid>()
                };

                ViewData["AppointmentId"] = id;
                PopulateStatusSelectList(parsedStatus);
                await PopulateServicesMultiSelectAsync(tenantId, updateDto.ServiceOfferingIds);

                return View(updateDto);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, UpdateAppointmentDTO dto)
        {
            var tenantId = TenantId;

            async Task LoadTenantConfig()
            {
                var biz = await _db.Businesses.AsNoTracking().FirstAsync(b => b.Id == tenantId);
                ViewBag.WorkStart = biz.WorkDayStart.ToString(@"hh\:mm");
                ViewBag.WorkEnd = biz.WorkDayEnd.ToString(@"hh\:mm");
                ViewBag.HiddenDays = System.Text.Json.JsonSerializer.Serialize(BuildHiddenDays(biz));
                ViewBag.SlotMinutes = biz.DefaultSlotMinutes <= 0 ? 30 : biz.DefaultSlotMinutes;
            }

            if (!ModelState.IsValid)
            {
                ViewData["AppointmentId"] = id;
                PopulateStatusSelectList(dto.Status);
                await PopulateServicesMultiSelectAsync(tenantId, dto.ServiceOfferingIds);
                await LoadTenantConfig();
                return View(dto);
            }

            try
            {
                await _appointmentService.UpdateAppointmentAsync(id, dto, tenantId);
                TempData["SuccessMessage"] = "Appointment updated successfully!";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);

                ViewData["AppointmentId"] = id;
                PopulateStatusSelectList(dto.Status);
                await PopulateServicesMultiSelectAsync(tenantId, dto.ServiceOfferingIds);
                await LoadTenantConfig();

                return View(dto);
            }
        }

        public async Task<IActionResult> Delete(Guid id)
        {
            var tenantId = TenantId;

            try
            {
                var appointment = await _appointmentService.GetAppointmentByIdAsync(id, tenantId);
                return View(appointment);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var tenantId = TenantId;

            try
            {
                await _appointmentService.DeleteAppointmentAsync(id, tenantId);
                TempData["SuccessMessage"] = "Appointment deleted successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        public async Task<IActionResult> Calendar()
        {
            var tenantId = TenantId;
            var biz = await _db.Businesses.AsNoTracking().FirstAsync(b => b.Id == tenantId);

            ViewBag.WorkStart = biz.WorkDayStart.ToString(@"hh\:mm\:ss");
            ViewBag.WorkEnd = biz.WorkDayEnd.ToString(@"hh\:mm\:ss");
            ViewBag.HiddenDays = System.Text.Json.JsonSerializer.Serialize(BuildHiddenDays(biz));

            return View();
        }

        [HttpGet]
        public async Task<IActionResult> CalendarEvents()
        {
            var tenantId = TenantId;
            var appts = await _appointmentService.GetAllAppointmentsAsync(tenantId);

            //  Remove Completed + Cancelled from calendar
            var filtered = appts.Where(a =>
                !string.Equals(a.Status, AppointmentStatus.Completed.ToString(), StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(a.Status, AppointmentStatus.Cancelled.ToString(), StringComparison.OrdinalIgnoreCase)
            );

            var events = filtered.Select(a => new
            {
                id = a.Id,
                title = $"{a.CustomerName} • {a.OrderNumber}",
                start = a.AppointmentDate.ToString("s"),
                url = Url.Action("Details", "Appointments", new { id = a.Id }),
                extendedProps = new { status = a.Status }
            });

            return Json(events);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetStatus(Guid id, string status, string? returnUrl = null)
        {
            var tenantId = TenantId;

            if (!Enum.TryParse<AppointmentStatus>(status, ignoreCase: true, out var newStatus))
                return BadRequest("Invalid status.");

            try
            {
                await _appointmentService.SetStatusAsync(id, newStatus, tenantId);

                TempData["SuccessMessage"] = "Appointment status updated.";
                return RedirectToLocalOrIndex(returnUrl);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (BusinessRuleException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToLocalOrIndex(returnUrl);
            }
        }

        /// <summary>
        /// Follows returnUrl only when it points inside this app; anything else (e.g. an
        /// external URL) falls back to the list, instead of LocalRedirect throwing a 500.
        /// </summary>
        private IActionResult RedirectToLocalOrIndex(string? returnUrl)
        {
            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction(nameof(Index));
        }

        private static List<int> BuildHiddenDays(Business biz)
        {
            var hidden = new List<int>();
            if (!biz.OpenSun) hidden.Add(0);
            if (!biz.OpenMon) hidden.Add(1);
            if (!biz.OpenTue) hidden.Add(2);
            if (!biz.OpenWed) hidden.Add(3);
            if (!biz.OpenThu) hidden.Add(4);
            if (!biz.OpenFri) hidden.Add(5);
            if (!biz.OpenSat) hidden.Add(6);
            return hidden;
        }

        [HttpGet]
        public async Task<IActionResult> CalendarHolidays(int? year)
        {
            var y = year ?? DateTime.Now.Year;
            var list = await _holidaysService.GetHolidaysAsync(y, "MK");

            var events = list.Select(h => new
            {
                start = h.Date.ToString("yyyy-MM-dd"),
                end = h.Date.AddDays(1).ToString("yyyy-MM-dd"),
                allDay = true,
                display = "background",
                overlap = false,
                classNames = new[] { "holiday-bg" },
                extendedProps = new
                {
                    localName = h.LocalName,
                    holidayName = h.Name
                }
            });

            return Json(events);
        }
    }
}

