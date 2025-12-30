using AppointMe.Domain.DomainModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DTO
{
    public class UpdateAppointmentDTO
    {
        public DateTime AppointmentDate { get; set; }
        public string Description { get; set; }
        public AppointmentStatus Status { get; set; }
        public string OrderNumber { get; set; }
        public List<Guid> ServiceOfferingIds { get; set; } = new();
    }
}
