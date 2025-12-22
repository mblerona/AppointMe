using AppointMe.Domain.DomainModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Repository.Interface
{
    public interface IInvoiceRepository : IRepository<Invoice>
    {
        Task<Invoice?> GetByIdAsync(Guid id, Guid tenantId);
        Task<Invoice?> GetByAppointmentIdAsync(Guid appointmentId, Guid tenantId);
        Task<IEnumerable<Invoice>> GetAllByTenantAsync(Guid tenantId);

        Task<int> GetMaxInvoiceSequenceForYearAsync(Guid tenantId, int year);

    }
}
