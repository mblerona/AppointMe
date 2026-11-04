using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Controllers
{
    [Authorize]
    public class ServicesController : BaseTenantController
    {
        private readonly IServiceOfferingService _serviceOfferingService;

        public ServicesController(
            IServiceOfferingService serviceOfferingService,
            ITenantContext tenantContext)
            : base(tenantContext)
        {
            _serviceOfferingService = serviceOfferingService;
        }

        public async Task<IActionResult> Index()
        {
            var services = await _serviceOfferingService.GetAllAsync(TenantId);
            return View(services);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new CreateServiceOfferingDTO { IsActive = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateServiceOfferingDTO dto)
        {
            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                await _serviceOfferingService.CreateAsync(dto, TenantId);
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(ex.PropertyName ?? string.Empty, ex.Message);
                return View(dto);
            }

            TempData["SuccessMessage"] = "Service created successfully!";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            try
            {
                var entity = await _serviceOfferingService.GetByIdAsync(id, TenantId);

                return View(new UpdateServiceOfferingDTO
                {
                    Id = entity.Id,
                    Name = entity.Name,
                    Price = entity.Price,
                    DurationMinutes = entity.DurationMinutes,
                    IsActive = entity.IsActive,
                    CategoryId = entity.CategoryId
                });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, UpdateServiceOfferingDTO dto)
        {
            if (id != dto.Id) return BadRequest();

            if (!ModelState.IsValid)
                return View(dto);

            try
            {
                await _serviceOfferingService.UpdateAsync(id, dto, TenantId);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(ex.PropertyName ?? string.Empty, ex.Message);
                return View(dto);
            }

            TempData["SuccessMessage"] = "Service updated successfully!";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Delete(Guid id)
        {
            try
            {
                return View(await _serviceOfferingService.GetByIdAsync(id, TenantId));
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
            try
            {
                await _serviceOfferingService.DeleteAsync(id, TenantId);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
            catch (BusinessRuleException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }

            TempData["SuccessMessage"] = "Service deleted successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}
