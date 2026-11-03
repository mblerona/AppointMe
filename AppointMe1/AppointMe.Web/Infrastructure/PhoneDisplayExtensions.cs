using AppointMe.Domain.Validation;

namespace AppointMe.Web.Infrastructure
{
    public static class PhoneDisplayExtensions
    {
        /// <summary>
        /// Readable phone number for views: "+38970123456" -> "070 123 456", "+49301234567" -> "+49 30 1234567".
        /// Keep the stored E.164 value for tel: links.
        /// </summary>
        public static string ToDisplayPhone(this string? phone) => PhoneNumberNormalizer.FormatForDisplay(phone);
    }
}
