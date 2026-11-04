using AppointMe.Domain.DomainModels;

namespace AppointMe.Service.Calendar
{
    /// <summary>
    /// How long a booking takes. The result is stored on the appointment as a snapshot, so later changes
    /// to a service or to the slot length don't affect appointments that already exist.
    /// </summary>
    public static class AppointmentDurationCalculator
    {
        private const int FallbackSlotMinutes = 30;

        /// <summary>The business's time slot interval, or the fallback if the stored value is invalid.</summary>
        public static int SlotLength(int defaultSlotMinutes) =>
            defaultSlotMinutes <= 0 ? FallbackSlotMinutes : defaultSlotMinutes;

        /// <summary>
        /// The sum of the services' durations, where a service without one counts as one slot, rounded up
        /// to a multiple of the slot length. Without services it is one slot.
        /// </summary>
        public static int Calculate(int defaultSlotMinutes, IEnumerable<ServiceOffering> services)
        {
            var slot = SlotLength(defaultSlotMinutes);

            var total = services.Sum(s => s.DurationMinutes ?? slot);
            if (total <= 0)
                return slot;

            return (total + slot - 1) / slot * slot;
        }
    }
}
