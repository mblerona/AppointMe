using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DTO
{
    public class CustomerProfileDTO
    {
        public Guid Id { get; set; }
        public int CustomerNumber { get; set; }

        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string SecondPhoneNumber { get; set; }
        public string? City { get; set; }
        public string Notes { get; set; }
        public int TotalAppointments { get; set; }
        public int CompletedAppointments { get; set; }
        public int ScheduledAppointments { get; set; }
        public int CancelledAppointments { get; set; }
        /// <summary>One page of the appointments, newest first. The counts above are for all of them.</summary>
        public List<AppointmentDTO> Appointments { get; set; }

        /// <summary>1-based, already limited to 1..AppointmentsTotalPages.</summary>
        public int AppointmentsPage { get; set; } = 1;
        public int AppointmentsTotalPages { get; set; } = 1;
        public int AppointmentsPageSize { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}
