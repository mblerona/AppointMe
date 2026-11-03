using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Validation;
using AppointMe.Repository.Interface;
using AppointMe.Service.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AppointMe.Service.Implementation
{
    public class CustomerService : ICustomerService
    {
        private readonly ICustomerRepository _customerRepository;

        public CustomerService(ICustomerRepository customerRepository)
        {
            _customerRepository = customerRepository;
        }

        public async Task<CustomerDTO> GetCustomerByIdAsync(Guid id, Guid tenantId)
        {
            var customer = await _customerRepository.GetByIdAsync(id);
            if (customer == null || customer.TenantId != tenantId)
                throw new KeyNotFoundException($"Customer with ID {id} not found");

            return MapToCustomerDto(customer);
        }

        public async Task<CustomerProfileDTO> GetCustomerProfileAsync(Guid customerId, Guid tenantId)
        {
           
            var customer = await _customerRepository.GetWithAppointmentsAsync(customerId, tenantId);
            if (customer == null)
                throw new KeyNotFoundException($"Customer with ID {customerId} not found");

            var profileDto = new CustomerProfileDTO
            {
                Id = customer.Id,
                CustomerNumber = customer.CustomerNumber,

                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                PhoneNumber = customer.PhoneNumber,
                SecondPhoneNumber = customer.SecondPhoneNumber,
                State = customer.State,
                City = customer.City,
                Notes = customer.Notes,

                TotalAppointments = customer.Appointments.Count,
                CompletedAppointments = customer.Appointments.Count(a => a.Status == AppointmentStatus.Completed),
                ScheduledAppointments = customer.Appointments.Count(a => a.Status == AppointmentStatus.Scheduled),
                CancelledAppointments = customer.Appointments.Count(a => a.Status == AppointmentStatus.Cancelled),

                Appointments = (customer.Appointments ?? new List<Appointment>())
                    .OrderByDescending(a => a.AppointmentDate)
                    .Select(MapToAppointmentDtoWithServices)
                    .ToList(),

                CreatedAt = customer.CreatedAt,
                UpdatedAt = customer.UpdatedAt
            };

            return profileDto;
        }

        public async Task<IEnumerable<CustomerDTO>> GetAllCustomersAsync(Guid tenantId)
        {
            var customers = await _customerRepository.GetAllByTenantAsync(tenantId);
            return customers.Select(MapToCustomerDto).ToList();
        }

        public async Task<IEnumerable<CustomerDTO>> SearchCustomersAsync(string searchTerm, Guid tenantId)
        {
            // "070 123 456" should also find the stored "+38970123456".
            var phone = PhoneNumberNormalizer.TryNormalize(searchTerm, out var e164) ? e164 : null;
            var customers = await _customerRepository.SearchAsync(searchTerm, tenantId, phone);
            return customers.Select(MapToCustomerDto).ToList();
        }

        public async Task<CustomerDTO> CreateCustomerAsync(CreateCustomerDTO createCustomerDto, Guid tenantId)
        {
            var phone = PhoneNumberNormalizer.NormalizeRequired(createCustomerDto.PhoneNumber, nameof(CreateCustomerDTO.PhoneNumber));
            var secondPhone = PhoneNumberNormalizer.NormalizeOptional(createCustomerDto.SecondPhoneNumber, nameof(CreateCustomerDTO.SecondPhoneNumber));

            var existingCustomer = await _customerRepository.GetByEmailAsync(createCustomerDto.Email, tenantId);
            if (existingCustomer != null)
                throw new BusinessRuleException($"Customer with email {createCustomerDto.Email} already exists", nameof(CreateCustomerDTO.Email));

            var maxNumber = await _customerRepository.GetMaxCustomerNumberAsync(tenantId);
            var nextNumber = maxNumber + 1;

            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerNumber = nextNumber,

                FirstName = createCustomerDto.FirstName,
                LastName = createCustomerDto.LastName,
                Email = createCustomerDto.Email,
                PhoneNumber = phone,
                SecondPhoneNumber = secondPhone,
                State = createCustomerDto.State,
                City = createCustomerDto.City,
                Notes = createCustomerDto.Notes,

                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _customerRepository.AddAsync(customer);
            await _customerRepository.SaveChangesAsync();

            return MapToCustomerDto(customer);
        }

        public async Task<CustomerDTO> UpdateCustomerAsync(Guid customerId, UpdateCustomerDTO updateCustomerDto, Guid tenantId)
        {
            var customer = await _customerRepository.GetByIdAsync(customerId);
            if (customer == null || customer.TenantId != tenantId)
                throw new KeyNotFoundException($"Customer with ID {customerId} not found");

            // null = keep the stored number (validate before changing anything).
            var phone = updateCustomerDto.PhoneNumber != null
                ? PhoneNumberNormalizer.NormalizeRequired(updateCustomerDto.PhoneNumber, nameof(UpdateCustomerDTO.PhoneNumber))
                : customer.PhoneNumber;
            var secondPhone = updateCustomerDto.SecondPhoneNumber != null
                ? PhoneNumberNormalizer.NormalizeOptional(updateCustomerDto.SecondPhoneNumber, nameof(UpdateCustomerDTO.SecondPhoneNumber))
                : customer.SecondPhoneNumber;

            if (!string.IsNullOrEmpty(updateCustomerDto.Email) && updateCustomerDto.Email != customer.Email)
            {
                var existingCustomer = await _customerRepository.GetByEmailAsync(updateCustomerDto.Email, tenantId);
                if (existingCustomer != null)
                    throw new BusinessRuleException($"Customer with email {updateCustomerDto.Email} already exists", nameof(UpdateCustomerDTO.Email));
            }

            customer.FirstName = updateCustomerDto.FirstName ?? customer.FirstName;
            customer.LastName = updateCustomerDto.LastName ?? customer.LastName;
            customer.Email = updateCustomerDto.Email ?? customer.Email;
            customer.PhoneNumber = phone;
            customer.SecondPhoneNumber = secondPhone;
            customer.State = updateCustomerDto.State ?? customer.State;
            customer.City = updateCustomerDto.City ?? customer.City;
            customer.Notes = updateCustomerDto.Notes ?? customer.Notes;
            customer.UpdatedAt = DateTime.UtcNow;

            await _customerRepository.UpdateAsync(customer);
            await _customerRepository.SaveChangesAsync();

            return MapToCustomerDto(customer);
        }

        public async Task DeleteCustomerAsync(Guid customerId, Guid tenantId)
        {
            var customer = await _customerRepository.GetByIdAsync(customerId);
            if (customer == null || customer.TenantId != tenantId)
                throw new KeyNotFoundException($"Customer with ID {customerId} not found");

            await _customerRepository.DeleteAsync(customer);
            await _customerRepository.SaveChangesAsync();
        }

        private CustomerDTO MapToCustomerDto(Customer customer)
        {
            return new CustomerDTO
            {
                Id = customer.Id,
                CustomerNumber = customer.CustomerNumber,
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = customer.Email,
                PhoneNumber = customer.PhoneNumber,
                SecondPhoneNumber = customer.SecondPhoneNumber,
                State = customer.State,
                City = customer.City,
                Notes = customer.Notes,
                CreatedAt = customer.CreatedAt,
                UpdatedAt = customer.UpdatedAt
            };
        }

        private AppointmentDTO MapToAppointmentDtoWithServices(Appointment appointment)
        {
            var lines = (appointment.AppointmentServices ?? new List<AppointmentServiceModel>())
                .Where(x => x.ServiceOffering != null)
                .Select(x => new ServiceLineDto
                {
                    ServiceId = x.ServiceOfferingId,
                    Name = x.ServiceOffering!.Name,
                    Category = x.ServiceOffering.Category?.Name,
                    PriceAtBooking = x.PriceAtBooking
                })
                .ToList();

            return new AppointmentDTO
            {
                Id = appointment.Id,
                CustomerId = appointment.CustomerId,
                CustomerName = appointment.Customer != null
                    ? $"{appointment.Customer.FirstName} {appointment.Customer.LastName}"
                    : "",
                OrderNumber = appointment.OrderNumber,
                AppointmentDate = appointment.AppointmentDate,
                Description = appointment.Description,
                Status = appointment.Status.ToString(),
                CreatedAt = appointment.CreatedAt,
                UpdatedAt = appointment.UpdatedAt,

                Services = lines
               
            };
        }
    }
}
