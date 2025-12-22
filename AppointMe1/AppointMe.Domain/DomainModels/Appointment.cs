using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppointMe.Domain.DomainModels
{
    public enum AppointmentStatus
                {
            Scheduled = 0,
           Completed = 1,
           Cancelled = 2,
            NoShow = 3
            }

    public class Appointment
    {
        public Guid Id { get; set; }
        public Guid CustomerId { get; set; }
        public Guid TenantId { get; set; }
        public string OrderNumber { get; set; }
        public DateTime AppointmentDate { get; set; }
        public string Description { get; set; }
        public AppointmentStatus Status { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        public Customer? Customer { get; set; }
        public ICollection<AppointmentServiceModel> AppointmentServices { get; set; } = new List<AppointmentServiceModel>();

     
    }
}
