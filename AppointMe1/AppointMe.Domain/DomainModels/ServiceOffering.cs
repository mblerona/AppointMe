using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AppointMe.Domain.DomainModels
{
    public class ServiceOffering
    {
        [Key]
        public Guid Id { get; set; }

        public Guid BusinessId { get; set; }
        public Business Business { get; set; }

        public Guid? CategoryId { get; set; }
        public ServiceCategory? Category { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; }

        public decimal Price { get; set; }

        public bool IsActive { get; set; } = true;
        public virtual ICollection<AppointmentServiceModel> AppointmentServices { get; set; } = new List<AppointmentServiceModel>();


        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
    }
