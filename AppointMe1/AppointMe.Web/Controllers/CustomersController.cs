using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using AppointMe.Domain.DTO;

namespace AppointMe.Web.Controllers
{
    public class CustomersController : BaseTenantController
    {
        private readonly ICustomerService _customerService;

        public CustomersController(
            ICustomerService customerService,
            UserManager<AppointMeAppUser> userManager,
            ApplicationDbContext db)
            : base(userManager, db)
        {
            _customerService = customerService;
        }

        // GET: Customers
        public async Task<IActionResult> Index(string searchTerm)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();

            var customers = string.IsNullOrEmpty(searchTerm)
                ? await _customerService.GetAllCustomersAsync(tenantId)
                : await _customerService.SearchCustomersAsync(searchTerm, tenantId);

            ViewData["SearchTerm"] = searchTerm;
            return View(customers);
        }

        public async Task<IActionResult> Details(Guid id)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();

            try
            {
                var customer = await _customerService.GetCustomerProfileAsync(id, tenantId);
                return View(customer);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        public async Task<IActionResult> Create()
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateCustomerDTO createCustomerDto)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            if (!ModelState.IsValid)
                return View(createCustomerDto);

            var tenantId = await GetTenantIdAsync();
            await _customerService.CreateCustomerAsync(createCustomerDto, tenantId);

            TempData["SuccessMessage"] = "Customer created successfully!";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(Guid id)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();

            try
            {
                var customer = await _customerService.GetCustomerByIdAsync(id, tenantId);

                var updateDto = new UpdateCustomerDTO
                {
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Email = customer.Email,
                    PhoneNumber = customer.PhoneNumber,
                    SecondPhoneNumber = customer.SecondPhoneNumber,
                    State = customer.State,
                    Notes = customer.Notes,
                    City = customer.City,

                };

                ViewData["CustomerId"] = id;
                return View(updateDto);
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(Guid id, UpdateCustomerDTO updateCustomerDto)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            if (!ModelState.IsValid)
            {
                ViewData["CustomerId"] = id;
                return View(updateCustomerDto);
            }

            var tenantId = await GetTenantIdAsync();

            try
            {
                await _customerService.UpdateCustomerAsync(id, updateCustomerDto, tenantId);
                TempData["SuccessMessage"] = "Customer updated successfully!";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }

        public async Task<IActionResult> Delete(Guid id)
        {
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();

            try
            {
                var customer = await _customerService.GetCustomerByIdAsync(id, tenantId);
                return View(customer);
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
            var redirect = await EnsureTenantAsync();
            if (redirect != null) return redirect;

            var tenantId = await GetTenantIdAsync();

            try
            {
                await _customerService.DeleteCustomerAsync(id, tenantId);
                TempData["SuccessMessage"] = "Customer deleted successfully!";
                return RedirectToAction(nameof(Index));
            }
            catch (KeyNotFoundException)
            {
                return NotFound();
            }
        }
    }
}





