using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DomainModels
{
    public  class AppointmentServiceModel
    {
        public Guid AppointmentId { get; set; }
        public Guid ServiceOfferingId { get; set; }

        public decimal PriceAtBooking { get; set; }

        public virtual Appointment? Appointment { get; set; }
        public virtual ServiceOffering? ServiceOffering { get; set; }
    }
}
