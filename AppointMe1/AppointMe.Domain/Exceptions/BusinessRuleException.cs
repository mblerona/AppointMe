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
    }
}
