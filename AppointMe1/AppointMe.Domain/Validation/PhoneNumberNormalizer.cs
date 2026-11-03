using AppointMe.Domain.Exceptions;
using PhoneNumbers;

namespace AppointMe.Domain.Validation
{
    /// <summary>
    /// Phone number parsing with libphonenumber. Numbers are stored in E.164 (e.g. "+38970123456").
    /// Input without a country code is read as a North Macedonian number ("MK"); international numbers
    /// are accepted with "+" or "00". Region "MK" is hard-coded like the holiday country (audit A13).
    /// </summary>
    public static class PhoneNumberNormalizer
    {
        public const string DefaultRegion = "MK";

        public const string InvalidMessage =
            "Enter a valid phone number, for example 070 123 456 or +49 30 1234567.";

        private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

        /// <summary>Parses and validates <paramref name="input"/>; on success returns it in E.164.</summary>
        public static bool TryNormalize(string? input, out string e164, string defaultRegion = DefaultRegion)
        {
            e164 = "";
            if (!TryParseValid(input, defaultRegion, out var number))
                return false;

            e164 = Util.Format(number, PhoneNumberFormat.E164);
            return true;
        }

        /// <summary>For required fields: returns E.164 or throws a BusinessRuleException for that field.</summary>
        public static string NormalizeRequired(string? input, string propertyName)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new BusinessRuleException("Phone number is required.", propertyName);

            return TryNormalize(input, out var e164)
                ? e164
                : throw new BusinessRuleException(InvalidMessage, propertyName);
        }

        /// <summary>For optional fields: empty input becomes null, anything else must be valid.</summary>
        public static string? NormalizeOptional(string? input, string propertyName) =>
            string.IsNullOrWhiteSpace(input) ? null : NormalizeRequired(input, propertyName);

        /// <summary>
        /// Readable form of a stored number: Macedonian numbers in national format ("070 123 456"),
        /// others in international format ("+49 30 1234567"). Values that can't be parsed (older data
        /// saved before validation) are returned unchanged.
        /// </summary>
        public static string FormatForDisplay(string? stored, string homeRegion = DefaultRegion)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return "";

            if (!TryParseValid(stored, homeRegion, out var number))
                return stored.Trim();

            var format = Util.GetRegionCodeForNumber(number) == homeRegion
                ? PhoneNumberFormat.NATIONAL
                : PhoneNumberFormat.INTERNATIONAL;
            return Util.Format(number, format);
        }

        private static bool TryParseValid(string? input, string region, out PhoneNumber number)
        {
            number = null!;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            try
            {
                number = Util.Parse(input.Trim(), region);
            }
            catch (NumberParseException)
            {
                return false;
            }

            return Util.IsValidNumber(number);
        }
    }
}
