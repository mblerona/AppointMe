namespace AppointMe.Domain.DTO
{
    /// <summary>Editable business settings (Settings/Business page).</summary>
    public class BusinessSettingsDTO
    {
        public string Name { get; set; } = "";
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }

        /// <summary>Read-only here; changed only by uploading a new logo.</summary>
        public string? LogoUrl { get; set; }

        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }

        public int DefaultSlotMinutes { get; set; } = 30;
        public bool EnableServices { get; set; }
        public bool EnableInvoices { get; set; }

        public TimeSpan WorkDayStart { get; set; }
        public TimeSpan WorkDayEnd { get; set; }

        public bool OpenMon { get; set; }
        public bool OpenTue { get; set; }
        public bool OpenWed { get; set; }
        public bool OpenThu { get; set; }
        public bool OpenFri { get; set; }
        public bool OpenSat { get; set; }
        public bool OpenSun { get; set; }
    }

    /// <summary>What the appointment forms and the calendar need to know about the business.</summary>
    public class BusinessCalendarConfigDTO
    {
        public TimeSpan WorkDayStart { get; set; }
        public TimeSpan WorkDayEnd { get; set; }

        /// <summary>Slot length for snapping times; never 0 or negative.</summary>
        public int SlotMinutes { get; set; }

        /// <summary>Closed weekdays in FullCalendar numbering (0 = Sunday ... 6 = Saturday).</summary>
        public IReadOnlyList<int> HiddenDays { get; set; } = Array.Empty<int>();
    }
}
