using AppointMe.Domain.DTO;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Controllers
{
    public class InvoicesController : BaseTenantController
    {
        private readonly IInvoiceService _invoiceService;

        public InvoicesController(
            IInvoiceService invoiceService,
            UserManager<AppointMeAppUser> userManager,
            ApplicationDbContext db) : base(userManager, db)
        {
            _invoiceService = invoiceService;
        }

        public async Task<IActionResult> Index()
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();
            var invoices = await _invoiceService.GetAllAsync(tenantId);
            return View(invoices);
        }

        public async Task<IActionResult> Details(Guid id)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();
            var invoice = await _invoiceService.GetByIdAsync(id, tenantId);
            return View(invoice);
        }

       
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFromAppointment(Guid appointmentId)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();

            try
            {
                var invoice = await _invoiceService.CreateOrGetForAppointmentAsync(appointmentId, tenantId);
                TempData["SuccessMessage"] = "Invoice ready.";
                return RedirectToAction(nameof(Details), new { id = invoice.Id });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction("Details", "Appointments", new { id = appointmentId });
            }
        }
    }
}