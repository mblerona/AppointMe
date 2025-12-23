using AppointMe.Domain.DTO;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Service.Email;
using AppointMe.Service.Interface;
using AppointMe.Service.Pdf;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Controllers
{
    public class InvoicesController : BaseTenantController
    {
        private readonly IInvoiceService _invoiceService;
        private readonly IEmailService _emailService;

        public InvoicesController(
            IInvoiceService invoiceService,
            IEmailService emailService,
            UserManager<AppointMeAppUser> userManager,
            ApplicationDbContext db) : base(userManager, db)
        {
            _invoiceService = invoiceService;
            _emailService = emailService;
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendByEmail(Guid id)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();

            try
            {
                var invoice = await _invoiceService.GetByIdAsync(id, tenantId);

                if (string.IsNullOrWhiteSpace(invoice.CustomerEmail))
                {
                    TempData["ErrorMessage"] = "Customer does not have an email address.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                // Link to the invoice details page in your app
                var invoiceUrl = Url.Action(nameof(Details), "Invoices", new { id = invoice.Id }, Request.Scheme);

                var subject = $"{invoice.BusinessName} Invoice {invoice.InvoiceNumber}";
                var content =
        $@"Greetings {invoice.CustomerName},

Your invoice from {invoice.BusinessName} is ready.

Invoice: {invoice.InvoiceNumber}
Total: {invoice.Total:0.00} den
Appointment: {invoice.AppointmentDate:dddd, dd MMM yyyy, HH:mm}

You can view it here:
{invoiceUrl}

Thank you,
{invoice.BusinessName}";

                await _emailService.SendEmailAsync(new EmailMessage
                {
                    MailTo = invoice.CustomerEmail,
                    Subject = subject,
                    Content = content
                });

                TempData["SuccessMessage"] = "Invoice sent by email.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Details), new { id });
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendPdfByEmail(Guid id)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();

            try
            {
                var invoice = await _invoiceService.GetByIdAsync(id, tenantId);

                if (string.IsNullOrWhiteSpace(invoice.CustomerEmail))
                {
                    TempData["ErrorMessage"] = "Customer does not have an email address.";
                    return RedirectToAction(nameof(Details), new { id });
                }

                // Generate PDF bytes
                var pdfBytes = await InvoicePdfGenerator.GenerateAsync(invoice);

                var subject = $"{invoice.BusinessName} Invoice {invoice.InvoiceNumber}";
                var content =
        $@"Greetings {invoice.CustomerName},

Please find your invoice attached.

Invoice: {invoice.InvoiceNumber}
Total: {invoice.Total:0.00} ден
Appointment: {invoice.AppointmentDate:dddd, dd MMM yyyy, HH:mm}

Thank you,
{invoice.BusinessName}";

                await _emailService.SendEmailWithAttachmentAsync(
                    new EmailMessage
                    {
                        MailTo = invoice.CustomerEmail,
                        Subject = subject,
                        Content = content
                    },
                    pdfBytes,
                    $"{invoice.InvoiceNumber}.pdf",
                    "application/pdf"
                );

                TempData["SuccessMessage"] = "Invoice PDF sent by email.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = ex.ToString();
                return RedirectToAction(nameof(Details), new { id });
            }
        }

    }
}