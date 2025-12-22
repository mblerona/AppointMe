using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Service.Interface
{
    public interface IEmailService
    {
        Task SendAppointmentConfirmationAsync(string email, string customerName, DateTime appointmentDate, string orderNumber);
        Task SendRescheduleNotificationAsync(string email, string customerName, DateTime newAppointmentDate);
        Task SendAppointmentReminderAsync(string email, string customerName, DateTime appointmentDate);
    }
}
