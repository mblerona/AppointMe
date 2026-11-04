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

        /// <summary>Stops tracking a customer whose save failed, so a retry doesn't try to insert it again.</summary>
        void Detach(Customer customer);

        Task<Customer> GetByEmailAsync(string email, Guid tenantId);
        /// <param name="normalizedPhone">The search term in E.164 if it is a valid phone number, else null.</param>
        Task<IEnumerable<Customer>> SearchAsync(string searchTerm, Guid tenantId, string? normalizedPhone);
        Task<Customer> GetWithAppointmentsAsync(Guid customerId, Guid tenantId);
        Task<IEnumerable<Customer>> GetAllByTenantAsync(Guid tenantId);
        Task<int> CountByTenantAsync(Guid tenantId);
    }
}
