using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppointMe.Domain.DomainModels
{
    public class Customer
    {
        [Key]
        public Guid Id { get; set; }
        public Guid TenantId { get; set; }

        public int CustomerNumber { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public string Email { get; set; }
        public string PhoneNumber { get; set; }
        public string? SecondPhoneNumber { get; set; }
        public string State { get; set; }
        public string City { get; set; }

        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

       
        public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();



      
    }
}
