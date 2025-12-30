using AppointMe.Domain.DomainModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AppointMe.Repository.Interface
{
    public interface IAppointmentRepository : IRepository<Appointment>
    {
        Task<IEnumerable<Appointment>> GetByCustomerIdAsync(Guid customerId, Guid tenantId);

        Task<IEnumerable<Appointment>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, Guid tenantId);

        Task<IEnumerable<Appointment>> GetByStatusAsync(AppointmentStatus status, Guid tenantId);

        Task<Appointment?> GetWithCustomerAsync(Guid appointmentId, Guid tenantId);

        Task<Appointment?> GetByIdAsync(Guid id, Guid tenantId);

        Task<IEnumerable<Appointment>> GetAllByTenantAsync(Guid tenantId);

        Task UpdateAsync(Appointment appointment);

        Task<bool> OrderNumberExistsAsync(string orderNumber, Guid? excludeAppointmentId = null);


        Task<bool> IsTimeSlotAvailableAsync(
            DateTime startTime,
            int durationMinutes,
            Guid tenantId,
            Guid? excludeAppointmentId = null
        );
    }
}

