using AppointMe.Domain.Exceptions;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Mvc;
using AppointMe.Domain.DTO;
using AppointMe.Web.Infrastructure;

namespace AppointMe.Web.Controllers
{
    public class CustomersController : BaseTenantController
    {
        private readonly ICustomerService _customerService;

        public CustomersController(
            ICustomerService customerService,
            ITenantContext tenantContext)
            : base(tenantContext)
        {
            _customerService = customerService;
        }

        // GET: Customers
        public async Task<IActionResult> Index(string searchTerm)
        {
            var tenantId = TenantId;

            var customers = string.IsNullOrEmpty(searchTerm)
                ? await _customerService.GetAllCustomersAsync(tenantId)
                : await _customerService.SearchCustomersAsync(searchTerm, tenantId);

            ViewData["SearchTerm"] = searchTerm;
            return View(customers);
        }

        public async Task<IActionResult> Details(Guid id)
        {
            var tenantId = TenantId;

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

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateCustomerDTO createCustomerDto)
        {
            if (!ModelState.IsValid)
                return View(createCustomerDto);

            var tenantId = TenantId;

            try
            {
                await _customerService.CreateCustomerAsync(createCustomerDto, tenantId);
            }
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(ex.PropertyName ?? string.Empty, ex.Message);
                return View(createCustomerDto);
            }

            TempData["SuccessMessage"] = "Customer created successfully!";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(Guid id)
        {
            var tenantId = TenantId;

            try
            {
                var customer = await _customerService.GetCustomerByIdAsync(id, tenantId);

                var updateDto = new UpdateCustomerDTO
                {
                    FirstName = customer.FirstName,
                    LastName = customer.LastName,
                    Email = customer.Email,
                    // Readable form in the input; the service normalizes to E.164 again on save.
                    PhoneNumber = customer.PhoneNumber.ToDisplayPhone(),
                    SecondPhoneNumber = string.IsNullOrEmpty(customer.SecondPhoneNumber) ? null : customer.SecondPhoneNumber.ToDisplayPhone(),
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
            if (!ModelState.IsValid)
            {
                ViewData["CustomerId"] = id;
                return View(updateCustomerDto);
            }

            var tenantId = TenantId;

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
            catch (BusinessRuleException ex)
            {
                ModelState.AddModelError(ex.PropertyName ?? string.Empty, ex.Message);
                ViewData["CustomerId"] = id;
                return View(updateCustomerDto);
            }
        }

        public async Task<IActionResult> Delete(Guid id)
        {
            var tenantId = TenantId;

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
            var tenantId = TenantId;

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





