using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;
using AppointMe.Domain.Exceptions;
using AppointMe.Service.Interface;
using AppointMe.Web.Infrastructure;
using AppointMe.Web.ViewModels.Admin;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Areas.Admin.Controllers
{
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
            // [FromRoute]: otherwise a form field named "Id" would win over the id in the URL.
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
                });
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
    }
}
