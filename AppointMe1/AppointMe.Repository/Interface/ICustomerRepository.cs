using AppointMe.Domain.DomainModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Repository.Interface
{
    public interface ICustomerRepository : IRepository<Customer>
    {
        Task<int> GetMaxCustomerNumberAsync(Guid tenantId);
        Task<Customer> GetByEmailAsync(string email, Guid tenantId);
        Task<IEnumerable<Customer>> SearchAsync(string searchTerm, Guid tenantId);
        Task<Customer> GetWithAppointmentsAsync(Guid customerId, Guid tenantId);
        Task<IEnumerable<Customer>> GetAllByTenantAsync(Guid tenantId);
    }
}
