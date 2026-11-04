using System;
using System.Collections.Generic;
using System.Linq;

namespace AppointMe.Domain.DTO
{
    public class AppointmentDTO
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }

        public string CustomerName { get; set; } = string.Empty;
        public string OrderNumber { get; set; } = string.Empty;

        public DateTime AppointmentDate { get; set; }
        public int DurationMinutes { get; set; }

        /// <summary>For display, for example "45 min", "1 h" or "1 h 30 min".</summary>
        public string DurationDisplay => DurationMinutes switch
        {
            < 60 => $"{DurationMinutes} min",
            _ when DurationMinutes % 60 == 0 => $"{DurationMinutes / 60} h",
            _ => $"{DurationMinutes / 60} h {DurationMinutes % 60} min"
        };

        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

   
        public List<ServiceLineDto> Services { get; set; } = new();

      
        public decimal TotalPrice => Services?.Sum(x => x.PriceAtBooking) ?? 0m;

        
        public List<Guid> ServiceOfferingIds { get; set; } = new();

        /// <summary>Set by CreateAppointmentAsync when a confirmation email was requested but could not be sent.</summary>
        public bool ConfirmationEmailFailed { get; set; }
    }
}



