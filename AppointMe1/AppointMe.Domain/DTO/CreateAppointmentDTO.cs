using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DTO
{
    public class CreateAppointmentDTO
    {
        [Required]
        public Guid CustomerId { get; set; }

        [Required]
        public string OrderNumber { get; set; }

        [Required]
        public DateTime AppointmentDate { get; set; }

        [Required]
        public string ?Description { get; set; }

        public List<Guid> ServiceOfferingIds { get; set; } = new();
        public bool NotifyByEmail { get; set; }
    
    }
}
