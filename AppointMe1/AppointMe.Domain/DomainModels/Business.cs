using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace AppointMe.Domain.DomainModels
{
    public class Business
    {
        [Key]
        public Guid Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = default!;

        [EmailAddress]
        public string? Email { get; set; }

        [Phone]
        public string? Phone { get; set; }

        public string? Address { get; set; }

        [MaxLength(500)]
        public string? LogoUrl { get; set; }

        [MaxLength(10)]
        public string? PrimaryColor { get; set; }

        [MaxLength(10)]
        public string? SecondaryColor { get; set; }

        public int DefaultSlotMinutes { get; set; } = 30;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public bool EnableServices { get; set; } = true;
        public bool EnableStaffing { get; set; } = true;
        public bool EnablePayments { get; set; } = true;
        public bool EnableInvoices { get; set; } = true;

        public TimeSpan WorkDayStart { get; set; } = new TimeSpan(9, 0, 0);
        public TimeSpan WorkDayEnd { get; set; } = new TimeSpan(17, 0, 0);

        // optional: which days are open (Mon-Fri default)
        public bool OpenMon { get; set; } = true;
        public bool OpenTue { get; set; } = true;
        public bool OpenWed { get; set; } = true;
        public bool OpenThu { get; set; } = true;
        public bool OpenFri { get; set; } = true;
        public bool OpenSat { get; set; } = false;
        public bool OpenSun { get; set; } = false;
        public ICollection<ServiceOffering> Services { get; set; } = new List<ServiceOffering>();
        public ICollection<Customer> Customers { get; set; } = new List<Customer>();

    }
}
