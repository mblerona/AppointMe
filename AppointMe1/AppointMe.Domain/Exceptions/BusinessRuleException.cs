namespace AppointMe.Domain.Exceptions
{
    /// <summary>
    /// A business rule was violated (overlapping appointment, duplicate email, closed day, ...).
    /// The message is written for end users, so controllers may display it as-is.
    /// Any other exception type is unexpected: log it and show a generic message instead.
    /// Derives from InvalidOperationException so existing callers and tests keep working.
    /// </summary>
    public class BusinessRuleException : InvalidOperationException
    {
        public BusinessRuleException(string message) : base(message) { }

        /// <param name="propertyName">The input field the message belongs to (for example "WorkDayEnd").</param>
        public BusinessRuleException(string message, string? propertyName) : base(message)
        {
            PropertyName = propertyName;
        }

        /// <summary>
        /// The input field the rule is about, so a controller can show the message next to that field
        /// (<c>ModelState.AddModelError(ex.PropertyName ?? "", ex.Message)</c>). Null for form-level errors.
        /// </summary>
        public string? PropertyName { get; }
    }
}
