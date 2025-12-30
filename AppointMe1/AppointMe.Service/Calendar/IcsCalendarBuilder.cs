using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Service.Calendar
{
    public class IcsCalendarBuilder
    {
        public static byte[] BuildAppointmentIcs(
           Guid appointmentId,
           string businessName,
           string? businessAddress,
           DateTime startLocal,
           DateTime endLocal,
           string customerFullName,
           string? orderNumber,
           string? notes)
        {
           
            const string CRLF = "\r\n";

            var uid = $"{appointmentId}@appointme";

            
            var dtStampUtc = DateTime.UtcNow;

            
            var dtStart = FormatIcsLocal(startLocal);
            var dtEnd = FormatIcsLocal(endLocal);
            var dtStamp = FormatIcsUtc(dtStampUtc);

            var summary = $"Appointment at {businessName}";
            var location = businessAddress ?? "";

           
            var descLines = new List<string>
            {
                $"Customer: {customerFullName}"
            };

            if (!string.IsNullOrWhiteSpace(orderNumber))
                descLines.Add($"Order #: {orderNumber}");

            if (!string.IsNullOrWhiteSpace(notes))
                descLines.Add($"Notes: {notes}");

            var description = string.Join("\\n", descLines.Select(EscapeText));

            // Build ICS content
            var sb = new StringBuilder();
            sb.Append("BEGIN:VCALENDAR").Append(CRLF);
            sb.Append("VERSION:2.0").Append(CRLF);
            sb.Append("PRODID:-//AppointMe//EN").Append(CRLF);
            sb.Append("CALSCALE:GREGORIAN").Append(CRLF);
            sb.Append("METHOD:PUBLISH").Append(CRLF);

            sb.Append("BEGIN:VEVENT").Append(CRLF);
            sb.Append("UID:").Append(uid).Append(CRLF);
            sb.Append("DTSTAMP:").Append(dtStamp).Append(CRLF);
            sb.Append("SUMMARY:").Append(EscapeText(summary)).Append(CRLF);
            sb.Append("DTSTART:").Append(dtStart).Append(CRLF);
            sb.Append("DTEND:").Append(dtEnd).Append(CRLF);

            if (!string.IsNullOrWhiteSpace(location))
                sb.Append("LOCATION:").Append(EscapeText(location)).Append(CRLF);

            if (!string.IsNullOrWhiteSpace(description))
                sb.Append("DESCRIPTION:").Append(description).Append(CRLF);

            sb.Append("END:VEVENT").Append(CRLF);
            sb.Append("END:VCALENDAR").Append(CRLF);

            return Encoding.UTF8.GetBytes(sb.ToString());
        }

      
        private static string FormatIcsLocal(DateTime dt)
            => dt.ToString("yyyyMMdd'T'HHmmss");

        
        private static string FormatIcsUtc(DateTime dtUtc)
            => dtUtc.ToString("yyyyMMdd'T'HHmmss'Z'");

      
        private static string EscapeText(string s)
        {
            return s
                .Replace("\\", "\\\\")
                .Replace(";", "\\;")
                .Replace(",", "\\,")
                .Replace("\r\n", "\\n")
                .Replace("\n", "\\n")
                .Replace("\r", "\\n");
        }
    }
}
