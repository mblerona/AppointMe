using System.ComponentModel.DataAnnotations;

namespace AppointMe.Domain.Validation
{
    /// <summary>
    /// Form validation for phone numbers (MK by default, international numbers with + or 00).
    /// Empty values are valid; combine with [Required] for mandatory fields. The services normalize
    /// and validate again, so callers that skip model binding (e.g. the AI assistant) get the same rule.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
    public sealed class ValidPhoneNumberAttribute : ValidationAttribute
    {
        public ValidPhoneNumberAttribute() : base(PhoneNumberNormalizer.InvalidMessage) { }

        public override bool IsValid(object? value) =>
            value switch
            {
                null => true,
                string s when string.IsNullOrWhiteSpace(s) => true,
                string s => PhoneNumberNormalizer.TryNormalize(s, out _),
                _ => false
            };
    }
}
