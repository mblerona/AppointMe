using System.ComponentModel.DataAnnotations;

namespace AppointMe.Web.ViewModels
{
    public class BusinessSettingsVm
    {
        public Guid Id { get; set; }

        [Required, MaxLength(200)]
        public string Name { get; set; } = "";

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

        [Range(5, 240)]
        public int DefaultSlotMinutes { get; set; } = 30;
        public bool EnableServices { get; set; }
        public bool EnableInvoices { get; set; }
        [Required]
        public TimeSpan WorkDayStart { get; set; } = new TimeSpan(9, 0, 0);

        [Required]
        public TimeSpan WorkDayEnd { get; set; } = new TimeSpan(17, 0, 0);

        public bool OpenMon { get; set; } = true;
        public bool OpenTue { get; set; } = true;
        public bool OpenWed { get; set; } = true;
        public bool OpenThu { get; set; } = true;
        public bool OpenFri { get; set; } = true;
        public bool OpenSat { get; set; } = false;
        public bool OpenSun { get; set; } = false;

    }
}
