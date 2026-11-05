using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;
using AppointMe.Domain.Exceptions;
using AppointMe.Service.Interface;
using AppointMe.Web.Infrastructure;
using AppointMe.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Areas.Admin.Controllers
{
    /// <summary>
    /// POST actions with an id use [FromRoute]: otherwise a form field named "Id" would win over the id in the URL.
    /// </summary>
    public class TenantsController : BaseAdminController
    {
        private readonly IPlatformAdminService _adminService;

        public TenantsController(IPlatformAdminService adminService)
        {
            _adminService = adminService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? search, BusinessStatus? status)
        {
            return View(new TenantListVm
            {
                Search = search,
                Status = status,
                Tenants = await _adminService.GetTenantsAsync(search, status)
            });
        }

        [HttpGet]
        public async Task<IActionResult> Details(Guid id)
        {
            try
            {
                return View(await _adminService.GetTenantAsync(id));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        // ---------------- edit ----------------

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            try
            {
                var tenant = await _adminService.GetTenantAsync(id);
                return View(new TenantEditVm
                {
                    Id = id,
                    Name = tenant.Name,
                    Email = tenant.Email,
                    Phone = string.IsNullOrEmpty(tenant.Phone) ? null : tenant.Phone.ToDisplayPhone(),
                    Address = tenant.Address
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost]
        public async Task<IActionResult> Edit([FromRoute] Guid id, TenantEditVm vm)
        {
            vm.Id = id;
            if (!ModelState.IsValid)
                return View(vm);

            try
            {
                await _adminService.UpdateTenantAsync(id, new UpdateTenantDTO
                {
                    Name = vm.Name,
                    Email = vm.Email,
                    Phone = vm.Phone,
                    Address = vm.Address
                }, AdminEmail);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(ex.PropertyName ?? string.Empty, ex.Message);
                return View(vm);
            }

            TempData["SuccessMessage"] = "Business details saved.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------------- suspend: choose a reason, confirm, done ----------------

        [HttpGet]
        public async Task<IActionResult> Suspend(Guid id)
        {
            TenantDetailsDTO tenant;
            try
            {
                tenant = await _adminService.GetTenantAsync(id);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }

            if (tenant.Status == BusinessStatus.Suspended)
            {
                TempData["ErrorMessage"] = "This business is already suspended.";
                return RedirectToAction(nameof(Details), new { id });
            }

            return View(new TenantSuspendVm { Id = id, BusinessName = tenant.Name });
        }

        /// <summary>Checks the choice and shows the confirmation step. Nothing is suspended yet.</summary>
        [HttpPost]
        public async Task<IActionResult> Suspend([FromRoute] Guid id, TenantSuspendVm vm)
        {
            if (!await FillAsync(id, vm))
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    _adminService.ValidateSuspension(vm.Reason, vm.Note);
                }
                catch (BusinessRuleException ex)
                {
                    ModelState.AddModelError(ex.PropertyName ?? string.Empty, ex.Message);
                }
            }

            return View(ModelState.IsValid ? "ConfirmSuspend" : "Suspend", vm);
        }

        [HttpPost]
        public async Task<IActionResult> ConfirmSuspend([FromRoute] Guid id, TenantSuspendVm vm)
        {
            if (!await FillAsync(id, vm))
                return NotFound();

            if (!ModelState.IsValid)
                return View("Suspend", vm);

            try
            {
                var email = await _adminService.SuspendTenantAsync(id, vm.Reason, vm.Note, AdminEmail);
                ReportOutcome("The business has been suspended.", email);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (BusinessRuleException ex) when (ex.PropertyName != null)
            {
                ModelState.AddModelError(ex.PropertyName, ex.Message);
                return View("Suspend", vm);
            }
            catch (BusinessRuleException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------------- reactivate ----------------

        /// <summary>This page is the confirmation step.</summary>
        [HttpGet]
        public async Task<IActionResult> Reactivate(Guid id)
        {
            TenantDetailsDTO tenant;
            try
            {
                tenant = await _adminService.GetTenantAsync(id);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }

            if (tenant.Status != BusinessStatus.Suspended)
            {
                TempData["ErrorMessage"] = "This business is not suspended.";
                return RedirectToAction(nameof(Details), new { id });
            }

            return View(new TenantReactivateVm { Id = id, BusinessName = tenant.Name });
        }

        [HttpPost]
        public async Task<IActionResult> Reactivate([FromRoute] Guid id, TenantReactivateVm vm)
        {
            vm.Id = id;
            try
            {
                vm.BusinessName = (await _adminService.GetTenantAsync(id)).Name;

                if (!ModelState.IsValid)
                    return View(vm);

                var email = await _adminService.ReactivateTenantAsync(id, vm.Note, AdminEmail);
                ReportOutcome("The business has been reactivated.", email);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (BusinessRuleException ex) when (ex.PropertyName != null)
            {
                ModelState.AddModelError(ex.PropertyName, ex.Message);
                return View(vm);
            }
            catch (BusinessRuleException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        // ---------------- helpers ----------------

        private async Task<bool> FillAsync(Guid id, TenantSuspendVm vm)
        {
            vm.Id = id;
            try
            {
                vm.BusinessName = (await _adminService.GetTenantAsync(id)).Name;
                return true;
            }
            catch (KeyNotFoundException)
            {
                return false;
            }
        }

        /// <summary>The action is saved either way; a problem with the email is only a warning.</summary>
        private void ReportOutcome(string done, TenantEmailOutcome email)
        {
            switch (email)
            {
                case TenantEmailOutcome.Sent:
                    TempData["SuccessMessage"] = $"{done} The tenant has been emailed.";
                    break;
                case TenantEmailOutcome.Failed:
                    TempData["WarningMessage"] = $"{done} The email to the tenant could not be sent.";
                    break;
                default:
                    TempData["WarningMessage"] = $"{done} The tenant has no email address to notify.";
                    break;
            }
        }
    }
}
