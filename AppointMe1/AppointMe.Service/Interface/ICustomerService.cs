using AppointMe.Domain.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace AppointMe.Service.Interface
{
    public  interface ICustomerService
    {
        Task<CustomerDTO> GetCustomerByIdAsync(Guid id, Guid tenantId);
        Task<CustomerProfileDTO> GetCustomerProfileAsync(Guid customerId, Guid tenantId);
        Task<IEnumerable<CustomerDTO>> GetAllCustomersAsync(Guid tenantId);
        Task<IEnumerable<CustomerDTO>> SearchCustomersAsync(string searchTerm, Guid tenantId);
        Task<CustomerDTO> CreateCustomerAsync(CreateCustomerDTO createCustomerDto, Guid tenantId);
        Task<CustomerDTO> UpdateCustomerAsync(Guid customerId, UpdateCustomerDTO updateCustomerDto, Guid tenantId);
        Task DeleteCustomerAsync(Guid customerId, Guid tenantId);
    }
}
