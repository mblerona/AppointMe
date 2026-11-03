using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Service.Interface;
using AppointMe.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AppointMe.Web.Controllers
{
    [Authorize]
    public class SettingsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;
        private readonly ITenantContext _tenantContext;

        public SettingsController(
            ApplicationDbContext db,
            IWebHostEnvironment env,
            ITenantContext tenantContext)
        {
            _db = db;
            _env = env;
            _tenantContext = tenantContext;
        }

        [HttpGet]
        public async Task<IActionResult> Business()
        {
            var tenant = await _tenantContext.ResolveAsync();
            if (tenant.Status == TenantResolutionStatus.NotAuthenticated) return Challenge();

            if (tenant.Status == TenantResolutionStatus.NoTenant)
                return NoBusiness();

            var business = await _db.Businesses.FirstAsync(b => b.Id == tenant.TenantId!.Value);

            var vm = new BusinessSettingsVm
            {
                Id = business.Id,
                Name = business.Name,
                Email = business.Email,
                Phone = business.Phone,
                Address = business.Address,
                LogoUrl = business.LogoUrl,
                PrimaryColor = business.PrimaryColor,
                SecondaryColor = business.SecondaryColor,
                DefaultSlotMinutes = business.DefaultSlotMinutes,
                WorkDayStart = business.WorkDayStart,
                WorkDayEnd = business.WorkDayEnd,
                OpenMon = business.OpenMon,
                OpenTue = business.OpenTue,
                OpenWed = business.OpenWed,
                OpenThu = business.OpenThu,
                OpenFri = business.OpenFri,
                OpenSat = business.OpenSat,
                OpenSun = business.OpenSun,
                EnableServices = business.EnableServices,
                EnableInvoices = business.EnableInvoices
            };

           
            if (!vm.EnableServices) vm.EnableInvoices = false;

            return View(vm);
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
            if (tenant.TenantId!.Value != vm.Id)
                return Forbid();

            var business = await _db.Businesses.FirstAsync(b => b.Id == tenant.TenantId.Value);

            business.Name = vm.Name.Trim();
            business.Email = vm.Email?.Trim();
            business.Phone = vm.Phone?.Trim();
            business.Address = vm.Address?.Trim();
            business.PrimaryColor = NormalizeHex(vm.PrimaryColor);
            business.SecondaryColor = NormalizeHex(vm.SecondaryColor);
            business.DefaultSlotMinutes = vm.DefaultSlotMinutes;

            if (vm.WorkDayEnd <= vm.WorkDayStart)
            {
                ModelState.AddModelError(nameof(vm.WorkDayEnd), "Work day end must be after start.");
                vm.LogoUrl = business.LogoUrl;
                return View(vm);
            }

            business.WorkDayStart = vm.WorkDayStart;
            business.WorkDayEnd = vm.WorkDayEnd;

            business.OpenMon = vm.OpenMon;
            business.OpenTue = vm.OpenTue;
            business.OpenWed = vm.OpenWed;
            business.OpenThu = vm.OpenThu;
            business.OpenFri = vm.OpenFri;
            business.OpenSat = vm.OpenSat;
            business.OpenSun = vm.OpenSun;
            business.EnableServices = vm.EnableServices;

        
            business.EnableInvoices = vm.EnableServices && vm.EnableInvoices;

            business.UpdatedAt = DateTime.UtcNow;

           
            if (logoFile != null && logoFile.Length > 0)
            {
                const long maxBytes = 2 * 1024 * 1024;
                if (logoFile.Length > maxBytes)
                {
                    ModelState.AddModelError("", "Logo file is too large (max 2MB).");
                    vm.LogoUrl = business.LogoUrl;
                    return View(vm);
                }

                var ext = Path.GetExtension(logoFile.FileName).ToLowerInvariant();
                var allowed = new HashSet<string> { ".png", ".jpg", ".jpeg", ".webp" };
                if (!allowed.Contains(ext))
                {
                    ModelState.AddModelError("", "Only PNG, JPG, JPEG, or WEBP files are allowed.");
                    vm.LogoUrl = business.LogoUrl;
                    return View(vm);
                }

                var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "logos");
                Directory.CreateDirectory(uploadsDir);

                var fileName = $"{business.Id}{ext}";
                var savePath = Path.Combine(uploadsDir, fileName);

                using (var stream = new FileStream(savePath, FileMode.Create))
                {
                    await logoFile.CopyToAsync(stream);
                }

                business.LogoUrl = $"/uploads/logos/{fileName}";
            }

            await _db.SaveChangesAsync();

            TempData["Success"] = "Business settings saved.";
            return RedirectToAction(nameof(Business));
        }

        /// <summary>
        /// The Business is created at registration, so this page never creates one. A user without a
        /// Business (which should not happen after the ConfirmExistingUsers migration) sees an error page.
        /// RequireTenantFilter sends such users here, so this must not redirect back (no loop).
        /// </summary>
        private IActionResult NoBusiness() => View("NoBusiness");

        private static string? NormalizeHex(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return null;

            var s = input.Trim();
            if (!s.StartsWith("#")) s = "#" + s;

            if (s.Length != 4 && s.Length != 7) return null;

            var hex = s.Substring(1);
            if (!hex.All(c => Uri.IsHexDigit(c))) return null;

            return s.ToLowerInvariant();
        }
    }
}
