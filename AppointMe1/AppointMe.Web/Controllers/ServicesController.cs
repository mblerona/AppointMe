using AppointMe.Domain.DTO;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AppointMe.Web.Controllers
{
    [Authorize]
    public class ServicesController : BaseTenantController
    {
        private readonly IServiceOfferingRepository _serviceRepo;

        public ServicesController(
            IServiceOfferingRepository serviceRepo,
            UserManager<AppointMeAppUser> userManager,
            ApplicationDbContext db)
            : base(userManager, db)
        {
            _serviceRepo = serviceRepo;
        }

        public async Task<IActionResult> Index()
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();
            var services = await _serviceRepo.GetAllByBusinessAsync(tenantId);
            return View(services);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            return View(new CreateServiceOfferingDTO { IsActive = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateServiceOfferingDTO dto)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            if (!ModelState.IsValid)
                return View(dto);

            var tenantId = await GetTenantIdAsync();

            var entity = new ServiceOffering
            {
                Id = Guid.NewGuid(),
           
                Name = dto.Name.Trim(),
                Price = dto.Price,
                IsActive = dto.IsActive,
                CategoryId = dto.CategoryId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

          
            await _serviceRepo.AddAsync(entity, tenantId);
            await _serviceRepo.SaveChangesAsync();

            TempData["SuccessMessage"] = "Service created successfully!";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(Guid id)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();
            var entity = await _serviceRepo.GetByIdAsync(id, tenantId);

            if (entity == null) return NotFound();

            var dto = new UpdateServiceOfferingDTO
            {
                Id = entity.Id,
                Name = entity.Name,
                Price = entity.Price,
                IsActive = entity.IsActive,
                CategoryId = entity.CategoryId
            };

            return View(dto);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, UpdateServiceOfferingDTO dto)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            if (id != dto.Id) return BadRequest();

            if (!ModelState.IsValid)
                return View(dto);

            var tenantId = await GetTenantIdAsync();
            var entity = await _serviceRepo.GetByIdAsync(id, tenantId);

            if (entity == null) return NotFound();

            entity.Name = dto.Name.Trim();
            entity.Price = dto.Price;
            entity.IsActive = dto.IsActive;
            entity.CategoryId = dto.CategoryId;
            entity.UpdatedAt = DateTime.UtcNow;

            await _serviceRepo.UpdateAsync(entity);
            await _serviceRepo.SaveChangesAsync();

            TempData["SuccessMessage"] = "Service updated successfully!";
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Delete(Guid id)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();
            var entity = await _serviceRepo.GetByIdAsync(id, tenantId);

            if (entity == null) return NotFound();

            return View(entity);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(Guid id)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();
            var entity = await _serviceRepo.GetByIdAsync(id, tenantId);

            if (entity == null) return NotFound();

            await _serviceRepo.DeleteAsync(entity);
            await _serviceRepo.SaveChangesAsync();

            TempData["SuccessMessage"] = "Service deleted successfully!";
            return RedirectToAction(nameof(Index));
        }
    }
}


