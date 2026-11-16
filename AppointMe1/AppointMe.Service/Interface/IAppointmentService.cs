using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Service.Interface
{
    public interface IAppointmentService
    {
        Task<AppointmentDTO> GetAppointmentByIdAsync(Guid id, Guid tenantId);
        Task<IEnumerable<AppointmentDTO>> GetAllAppointmentsAsync(Guid tenantId);
        Task<IEnumerable<AppointmentDTO>> GetAppointmentsByCustomerAsync(Guid customerId, Guid tenantId);
        Task<IEnumerable<AppointmentDTO>> GetAppointmentsByDateRangeAsync(DateTime startDate, DateTime endDate, Guid tenantId);
        Task<IEnumerable<AppointmentDTO>> GetAppointmentsByStatusAsync(AppointmentStatus status, Guid tenantId);
        Task<AppointmentDTO> CreateAppointmentAsync(CreateAppointmentDTO createAppointmentDto, Guid tenantId);
        Task<AppointmentDTO> UpdateAppointmentAsync(Guid appointmentId, UpdateAppointmentDTO updateAppointmentDto, Guid tenantId);
        Task DeleteAppointmentAsync(Guid appointmentId, Guid tenantId);
        Task SetStatusAsync(Guid appointmentId, AppointmentStatus status, Guid tenantId);

        /// <summary>
        /// The day's slots (on the time slot interval, from opening to closing) and the bookings that block
        /// them, for the booking form. Uses the same rules as booking: closed days, public holidays, the
        /// business's "now" and the overlap rule (cancelled appointments don't block).
        /// </summary>
        Task<DayAvailabilityDTO> GetDayAvailabilityAsync(DateOnly date, Guid tenantId);
    }
}
