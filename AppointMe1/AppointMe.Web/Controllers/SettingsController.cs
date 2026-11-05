using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Service.Interface;
using AppointMe.Web.Infrastructure;
using AppointMe.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Controllers
{
    /// <summary>
    /// Not a BaseTenantController: RequireTenantFilter redirects tenant-less users here, so this
    /// controller resolves the tenant itself and shows an error page instead of redirecting (no loop).
    /// </summary>
    [Authorize]
    public class SettingsController : Controller
    {
        private readonly IBusinessService _businessService;
        private readonly ITenantDeletionRequestService _deletionRequests;
        private readonly ITenantContext _tenantContext;

        public SettingsController(
            IBusinessService businessService,
            ITenantDeletionRequestService deletionRequests,
            ITenantContext tenantContext)
        {
            _businessService = businessService;
            _deletionRequests = deletionRequests;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> Business()
        {
            var (tenantId, stop) = await ResolveTenantAsync();
            if (stop != null) return stop;

            var settings = await _businessService.GetSettingsAsync(tenantId);
            return View(ToVm(tenantId, settings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Business(BusinessSettingsVm vm, IFormFile? logoFile)
        {
            var (tenantId, stop) = await ResolveTenantAsync();
            if (stop != null) return stop;

            if (!ModelState.IsValid)
            {
                vm.DeletionRequestedAt = (await _businessService.GetSettingsAsync(tenantId)).DeletionRequestedAt;
                return View(vm);
            }

            // The tenant comes from the server; a posted Id for another Business is rejected.
            if (tenantId != vm.Id)
                return Forbid();

            await using var logoStream = logoFile is { Length: > 0 } ? logoFile.OpenReadStream() : null;
            var logo = logoStream != null ? new LogoUpload(logoStream, logoFile!.FileName, logoFile.Length) : null;

            try
            {
                await _businessService.UpdateSettingsAsync(tenantId, ToDto(vm), logo);
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(ex.PropertyName ?? string.Empty, ex.Message);
                var current = await _businessService.GetSettingsAsync(tenantId);
                vm.LogoUrl = current.LogoUrl;
                vm.DeletionRequestedAt = current.DeletionRequestedAt;
                return View(vm);
            }

            TempData["Success"] = "Business settings saved.";
            return RedirectToAction(nameof(Business));
        }

        // ---------------- deletion request: optional reason, confirm, done. Always the tenant's own Business. ----------------

        [HttpGet]
        public async Task<IActionResult> RequestDeletion()
        {
            var (tenantId, stop) = await ResolveTenantAsync();
            if (stop != null) return stop;

            if ((await _businessService.GetSettingsAsync(tenantId)).DeletionRequestedAt != null)
                return RedirectToAction(nameof(Business));

            return View(new DeletionRequestVm());
        }

        /// <summary>Checks the reason and shows the confirmation step. Nothing is requested yet.</summary>
        [HttpPost]
        public async Task<IActionResult> RequestDeletion(DeletionRequestVm vm)
        {
            var (_, stop) = await ResolveTenantAsync();
            if (stop != null) return stop;

            return View(ModelState.IsValid ? "ConfirmDeletionRequest" : "RequestDeletion", vm);
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmDeletionRequest(DeletionRequestVm vm)
        {
            var (tenantId, stop) = await ResolveTenantAsync();
            if (stop != null) return stop;

            if (!ModelState.IsValid)
                return View("RequestDeletion", vm);

            try
            {
                await _deletionRequests.RequestAsync(tenantId, vm.Reason);
                TempData["SuccessMessage"] = "Your deletion request has been sent for review.";
            }
            catch (BusinessRuleException ex) when (ex.PropertyName != null)
            {
                ModelState.AddModelError(ex.PropertyName, ex.Message);
                return View("RequestDeletion", vm);
            }
            catch (BusinessRuleException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Business));
        }

        [HttpPost]
        public async Task<IActionResult> CancelDeletionRequest()
        {
            var (tenantId, stop) = await ResolveTenantAsync();
            if (stop != null) return stop;

            try
            {
                await _deletionRequests.CancelAsync(tenantId);
                TempData["SuccessMessage"] = "Your deletion request has been cancelled.";
            }
            catch (BusinessRuleException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Business));
        }

        /// <returns>The tenant, or the result to return instead when there is no tenant to work on.</returns>
        private async Task<(Guid TenantId, IActionResult? Stop)> ResolveTenantAsync()
        {
            var tenant = await _tenantContext.ResolveAsync();
            return tenant.Status switch
            {
                TenantResolutionStatus.NotAuthenticated => (Guid.Empty, Challenge()),
                TenantResolutionStatus.PlatformAdmin => (Guid.Empty, AdminHome()),
                TenantResolutionStatus.Suspended => (Guid.Empty, RedirectToAction("Index", "Suspended")),
                TenantResolutionStatus.NoTenant => (Guid.Empty, NoBusiness()),
                _ => (tenant.TenantId!.Value, null)
            };
        }

        /// <summary>
        /// The Business is created at registration, so this page never creates one. A user without a
        /// Business (which should not happen after the ConfirmExistingUsers migration) sees an error page.
        /// RequireTenantFilter sends such users here, so this must not redirect back (no loop).
        /// </summary>
        private IActionResult NoBusiness() => View("NoBusiness");

        /// <summary>A platform admin never has a Business, so these settings don't apply to them.</summary>
        private IActionResult AdminHome() => RedirectToAction("Index", "Home", new { area = "Admin" });

        private static BusinessSettingsVm ToVm(Guid tenantId, BusinessSettingsDTO s) => new()
        {
            Id = tenantId,
            Name = s.Name,
            Email = s.Email,
            Phone = string.IsNullOrEmpty(s.Phone) ? null : s.Phone.ToDisplayPhone(),
            Address = s.Address,
            LogoUrl = s.LogoUrl,
            // A color input can't be empty: without a value the browser shows and posts black.
            PrimaryColor = s.PrimaryColor ?? BusinessBrandingDefaults.NavbarColor,
            SecondaryColor = s.SecondaryColor ?? BusinessBrandingDefaults.BackgroundColor,
            DefaultSlotMinutes = s.DefaultSlotMinutes,
            WorkDayStart = s.WorkDayStart,
            WorkDayEnd = s.WorkDayEnd,
            TimeZoneId = s.TimeZoneId,
            OpenMon = s.OpenMon,
            OpenTue = s.OpenTue,
            OpenWed = s.OpenWed,
            OpenThu = s.OpenThu,
            OpenFri = s.OpenFri,
            OpenSat = s.OpenSat,
            OpenSun = s.OpenSun,
            EnableServices = s.EnableServices,
            EnableInvoices = s.EnableInvoices,
            DeletionRequestedAt = s.DeletionRequestedAt
        };

        private static BusinessSettingsDTO ToDto(BusinessSettingsVm vm) => new()
        {
            Name = vm.Name,
            Email = vm.Email,
            Phone = vm.Phone,
            Address = vm.Address,
            PrimaryColor = vm.PrimaryColor,
            SecondaryColor = vm.SecondaryColor,
            DefaultSlotMinutes = vm.DefaultSlotMinutes,
            WorkDayStart = vm.WorkDayStart,
            WorkDayEnd = vm.WorkDayEnd,
            TimeZoneId = vm.TimeZoneId,
            OpenMon = vm.OpenMon,
            OpenTue = vm.OpenTue,
            OpenWed = vm.OpenWed,
            OpenThu = vm.OpenThu,
            OpenFri = vm.OpenFri,
            OpenSat = vm.OpenSat,
            OpenSun = vm.OpenSun,
            EnableServices = vm.EnableServices,
            EnableInvoices = vm.EnableInvoices
        };
    }
}
