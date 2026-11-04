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
        private readonly ITenantContext _tenantContext;

        public SettingsController(IBusinessService businessService, ITenantContext tenantContext)
        {
            _businessService = businessService;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> Business()
        {
            var tenant = await _tenantContext.ResolveAsync();
            if (tenant.Status == TenantResolutionStatus.NotAuthenticated) return Challenge();
            if (tenant.Status == TenantResolutionStatus.NoTenant) return NoBusiness();

            var tenantId = tenant.TenantId!.Value;
            var settings = await _businessService.GetSettingsAsync(tenantId);
            return View(ToVm(tenantId, settings));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Business(BusinessSettingsVm vm, IFormFile? logoFile)
        {
            if (!ModelState.IsValid) return View(vm);

            var tenant = await _tenantContext.ResolveAsync();
            if (tenant.Status == TenantResolutionStatus.NotAuthenticated) return Challenge();
            if (tenant.Status == TenantResolutionStatus.NoTenant) return NoBusiness();

            // The tenant comes from the server; a posted Id for another Business is rejected.
            var tenantId = tenant.TenantId!.Value;
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
                vm.LogoUrl = (await _businessService.GetSettingsAsync(tenantId)).LogoUrl;
                return View(vm);
            }

            TempData["Success"] = "Business settings saved.";
            return RedirectToAction(nameof(Business));
        }

        /// <summary>
        /// The Business is created at registration, so this page never creates one. A user without a
        /// Business (which should not happen after the ConfirmExistingUsers migration) sees an error page.
        /// RequireTenantFilter sends such users here, so this must not redirect back (no loop).
        /// </summary>
        private IActionResult NoBusiness() => View("NoBusiness");

        private static BusinessSettingsVm ToVm(Guid tenantId, BusinessSettingsDTO s) => new()
        {
            Id = tenantId,
            Name = s.Name,
            Email = s.Email,
            Phone = string.IsNullOrEmpty(s.Phone) ? null : s.Phone.ToDisplayPhone(),
            Address = s.Address,
            LogoUrl = s.LogoUrl,
            PrimaryColor = s.PrimaryColor,
            SecondaryColor = s.SecondaryColor,
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
            EnableInvoices = s.EnableInvoices
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
