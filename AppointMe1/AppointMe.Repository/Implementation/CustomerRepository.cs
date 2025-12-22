using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Repository.Implementation
{
    public class CustomerRepository : BaseRepository<Customer>, ICustomerRepository
    {
        public CustomerRepository(ApplicationDbContext context) : base(context) { }

        public async Task<Customer> GetByEmailAsync(string email, Guid tenantId)
        {
            return await _dbSet.FirstOrDefaultAsync(c => c.Email == email && c.TenantId == tenantId);
        }

        public async Task<IEnumerable<Customer>> SearchAsync(string searchTerm, Guid tenantId)
        {
            return await _dbSet
                .Where(c => c.TenantId == tenantId &&
                           (c.FirstName.Contains(searchTerm) ||
                            c.LastName.Contains(searchTerm) ||
                            c.Email.Contains(searchTerm) ||
                            c.PhoneNumber.Contains(searchTerm) ||
                            c.State.Contains(searchTerm)))
                .ToListAsync();
        }

        public async Task<Customer> GetWithAppointmentsAsync(Guid customerId, Guid tenantId)
        {
            return await _dbSet
       .Include(c => c.Appointments)
           .ThenInclude(a => a.AppointmentServices)
               .ThenInclude(x => x.ServiceOffering)
                   .ThenInclude(s => s.Category)
       .FirstOrDefaultAsync(c => c.Id == customerId && c.TenantId == tenantId);
        }

        public async Task<IEnumerable<Customer>> GetAllByTenantAsync(Guid tenantId)
        {
            return await _dbSet.Where(c => c.TenantId == tenantId).ToListAsync();
        }
        public async Task<int> GetMaxCustomerNumberAsync(Guid tenantId)
        {
            return await _context.Customers
                .Where(c => c.TenantId == tenantId)
                .Select(c => (int?)c.CustomerNumber)
                .MaxAsync() ?? 0;
        }
    }
}
