using AppointMe.Service.Interface;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Service.Implementation
{
    public  class EmailService:IEmailService
    {
        // TODO: Implement actual email sending (Resend, SendGrid, SMTP, etc.)
        public async Task SendAppointmentConfirmationAsync(string email, string customerName, DateTime appointmentDate, string orderNumber)
        {
            var subject = $"Appointment Confirmation - Order {orderNumber}";
            var body = $"Dear {customerName},\n\nYour appointment has been scheduled for {appointmentDate:MMMM dd, yyyy} at {appointmentDate:hh:mm tt}.\n\nOrder Number: {orderNumber}\n\nThank you!";

            // Log for now
            Console.WriteLine($"Sending email to {email}: {subject}");
            Console.WriteLine(body);

            await Task.CompletedTask;
        }

        public async Task SendRescheduleNotificationAsync(string email, string customerName, DateTime newAppointmentDate)
        {
            var subject = "Appointment Rescheduled";
            var body = $"Dear {customerName},\n\nYour appointment has been rescheduled to {newAppointmentDate:MMMM dd, yyyy} at {newAppointmentDate:hh:mm tt}.\n\nThank you!";

            Console.WriteLine($"Sending email to {email}: {subject}");
            Console.WriteLine(body);

            await Task.CompletedTask;
        }

        public async Task SendAppointmentReminderAsync(string email, string customerName, DateTime appointmentDate)
        {
            var subject = "Appointment Reminder";
            var body = $"Dear {customerName},\n\nThis is a reminder that your appointment is coming up on {appointmentDate:MMMM dd, yyyy} at {appointmentDate:hh:mm tt}.\n\nThank you!";

            Console.WriteLine($"Sending email to {email}: {subject}");
            Console.WriteLine(body);

            await Task.CompletedTask;
        }
    }
}
