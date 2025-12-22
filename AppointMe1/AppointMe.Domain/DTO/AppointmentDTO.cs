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

        public string Description { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

   
        public List<ServiceLineDto> Services { get; set; } = new();

      
        public decimal TotalPrice => Services?.Sum(x => x.PriceAtBooking) ?? 0m;

        
        public List<Guid> ServiceOfferingIds { get; set; } = new();
    }
}



