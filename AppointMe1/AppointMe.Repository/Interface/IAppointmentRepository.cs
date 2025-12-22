using AppointMe.Domain.DomainModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Repository.Interface
{
    public interface IAppointmentRepository:IRepository<Appointment>
    {
        Task<IEnumerable<Appointment>> GetByCustomerIdAsync(Guid customerId, Guid tenantId);
        Task<IEnumerable<Appointment>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, Guid tenantId);
        Task<IEnumerable<Appointment>> GetByStatusAsync(AppointmentStatus status, Guid tenantId);
        Task<Appointment> GetWithCustomerAsync(Guid appointmentId, Guid tenantId);
        Task<bool> IsTimeSlotAvailableAsync(DateTime appointmentDate, Guid tenantId, Guid? excludeAppointmentId = null);
        Task<IEnumerable<Appointment>> GetAllByTenantAsync(Guid tenantId);
        Task<Appointment?> GetByIdAsync(Guid id, Guid tenantId);
        Task UpdateAsync(Appointment appointment);
    }
}
