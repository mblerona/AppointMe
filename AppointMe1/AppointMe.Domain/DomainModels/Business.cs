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

        /// <summary>IANA id. Appointment times are this zone's clock times; see <see cref="TimeZones.BusinessTimeZones"/>.</summary>
        [Required, MaxLength(TimeZones.BusinessTimeZones.MaxIdLength)]
        public string TimeZoneId { get; set; } = TimeZones.BusinessTimeZones.Default;

        // optional: which days are open (Mon-Fri default)
        public bool OpenMon { get; set; } = true;
        public bool OpenTue { get; set; } = true;
        public bool OpenWed { get; set; } = true;
        public bool OpenThu { get; set; } = true;
        public bool OpenFri { get; set; } = true;
        public bool OpenSat { get; set; } = false;
        public bool OpenSun { get; set; } = false;

        public BusinessStatus Status { get; set; } = BusinessStatus.Active;

        /// <summary>UTC. These three are only set while the Business is suspended.</summary>
        public DateTime? SuspendedAt { get; set; }

        public SuspensionReason? SuspensionReason { get; set; }

        /// <summary>Shown to the tenant, on the suspended page and in the email.</summary>
        [MaxLength(AdminActionLog.MaxNoteLength)]
        public string? SuspensionNote { get; set; }

        /// <summary>UTC. Set while the tenant's request to delete the account is waiting for review.</summary>
        public DateTime? DeletionRequestedAt { get; set; }

        [MaxLength(MaxDeletionRequestReasonLength)]
        public string? DeletionRequestReason { get; set; }

        public const int MaxDeletionRequestReasonLength = 500;

        public ICollection<ServiceOffering> Services { get; set; } = new List<ServiceOffering>();
        public ICollection<Customer> Customers { get; set; } = new List<Customer>();

    }
}
