namespace AppointMe.Domain.DTO
{
    /// <summary>
    /// One day of the business's calendar, for the booking form's day planner. Times are minutes since
    /// midnight on the business's clock. It only informs: booking still runs every rule on the server.
    /// </summary>
    public class DayAvailabilityDTO
    {
        public DateOnly Date { get; set; }

        /// <summary>False on a closed weekday or a public holiday; then there are no slots.</summary>
        public bool IsOpen { get; set; }

        /// <summary>Set when the day is a public holiday (local name when there is one).</summary>
        public string? HolidayName { get; set; }

        public int OpeningMinute { get; set; }
        public int ClosingMinute { get; set; }
        public int SlotMinutes { get; set; }

        public List<DaySlotDTO> Slots { get; set; } = new();

        /// <summary>The day's bookings that block time (everything except cancelled), in start order.</summary>
        public List<DayBookingDTO> Bookings { get; set; } = new();
    }

    public enum DaySlotState
    {
        Free,
        Booked,
        /// <summary>At or before the business's current time, so it can't be booked any more.</summary>
        Past
    }

    public class DaySlotDTO
    {
        public int StartMinute { get; set; }
        public DaySlotState State { get; set; }
    }

    public class DayBookingDTO
    {
        public Guid Id { get; set; }
        public string OrderNumber { get; set; } = "";
        public string CustomerName { get; set; } = "";
        public int StartMinute { get; set; }
        public int EndMinute { get; set; }
        public string Status { get; set; } = "";
    }
}
