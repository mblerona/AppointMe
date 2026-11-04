namespace AppointMe.Domain.TimeZones
{
    /// <summary>
    /// The time zones a business can choose, and the conversions between them and UTC. Appointment times
    /// are stored as the business's clock time, without an offset; "now" must be read in the same zone.
    /// </summary>
    public static class BusinessTimeZones
    {
        public const string Default = "Europe/Skopje";
        public const int MaxIdLength = 64;

        /// <summary>IANA ids, with the default first because that is where most businesses are.</summary>
        public static readonly IReadOnlyList<string> Supported = new[]
        {
            Default,
            "Europe/Amsterdam", "Europe/Athens", "Europe/Belgrade", "Europe/Berlin", "Europe/Brussels",
            "Europe/Bucharest", "Europe/Budapest", "Europe/Copenhagen", "Europe/Dublin", "Europe/Helsinki",
            "Europe/Istanbul", "Europe/Lisbon", "Europe/Ljubljana", "Europe/London", "Europe/Madrid",
            "Europe/Moscow", "Europe/Oslo", "Europe/Paris", "Europe/Podgorica", "Europe/Prague", "Europe/Rome",
            "Europe/Sarajevo", "Europe/Sofia", "Europe/Stockholm", "Europe/Tirane", "Europe/Vienna",
            "Europe/Warsaw", "Europe/Zagreb", "Europe/Zurich",
            "America/New_York", "America/Chicago", "America/Denver", "America/Los_Angeles", "America/Toronto",
            "America/Sao_Paulo", "Asia/Dubai", "Asia/Kolkata", "Asia/Bangkok", "Asia/Shanghai", "Asia/Tokyo",
            "Australia/Sydney", "Pacific/Auckland", "UTC"
        };

        public static bool IsSupported(string? id) => id != null && Supported.Contains(id);

        /// <summary>
        /// The zone for a stored id. An id this machine doesn't know falls back to the default instead of
        /// failing, because reading the time must never take down a page.
        /// </summary>
        public static TimeZoneInfo Find(string? id)
        {
            foreach (var candidate in new[] { id, Default })
            {
                if (string.IsNullOrWhiteSpace(candidate))
                    continue;

                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(candidate);
                }
                catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
                {
                    // Try the next one.
                }
            }

            return TimeZoneInfo.Utc;
        }

        /// <summary>The business's clock right now, for comparing with stored appointment times.</summary>
        public static DateTime Now(TimeProvider clock, string? id) =>
            TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Find(id)).DateTime;

        /// <summary>
        /// A moment stored in UTC (CreatedAt, UpdatedAt, IssuedAt) as the business's clock time, for display.
        /// Not for appointment times: those already are clock times.
        /// </summary>
        public static DateTime FromUtc(DateTime utc, string? id) =>
            DateTime.SpecifyKind(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Find(id)),
                DateTimeKind.Unspecified);

        /// <summary>For the Settings dropdown, for example "Europe/Skopje (UTC+01:00)". Standard time, not today's offset.</summary>
        public static string DisplayName(string id)
        {
            var offset = Find(id).BaseUtcOffset;
            return $"{id} (UTC{(offset < TimeSpan.Zero ? "-" : "+")}{offset:hh\\:mm})";
        }

        /// <summary>
        /// A stored clock time as UTC. Twice a year this needs a decision. A time skipped when the clocks go
        /// forward doesn't exist, so it moves forward by the jump, and a time repeated when they go back
        /// counts as the later (standard time) one, which is what TimeZoneInfo chooses.
        /// </summary>
        public static DateTime ToUtc(DateTime clockTime, string? id)
        {
            var zone = Find(id);
            var unspecified = DateTime.SpecifyKind(clockTime, DateTimeKind.Unspecified);

            if (zone.IsInvalidTime(unspecified))
            {
                var jump = zone.GetAdjustmentRules()
                    .FirstOrDefault(r => r.DateStart <= unspecified.Date && unspecified.Date <= r.DateEnd)?.DaylightDelta
                    ?? TimeSpan.FromHours(1);
                unspecified = unspecified.Add(jump);
            }

            return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
        }
    }
}
