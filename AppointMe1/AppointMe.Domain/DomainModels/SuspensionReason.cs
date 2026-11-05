namespace AppointMe.Domain.DomainModels
{
    /// <summary>The fixed list an admin chooses from. Stored as int, so don't renumber.</summary>
    public enum SuspensionReason
    {
        AbuseOfEmailNotifications = 0,
        FraudulentOrFakeBusiness = 1,
        ViolationOfTermsOfUse = 2,
        SecurityConcern = 3,
        Other = 4
    }

    public static class SuspensionReasons
    {
        public static readonly IReadOnlyList<SuspensionReason> All = Enum.GetValues<SuspensionReason>();

        /// <summary>The text the admin and the tenant see.</summary>
        public static string DisplayText(this SuspensionReason reason) => reason switch
        {
            SuspensionReason.AbuseOfEmailNotifications => "Abuse of email notifications",
            SuspensionReason.FraudulentOrFakeBusiness => "Fraudulent or fake business",
            SuspensionReason.ViolationOfTermsOfUse => "Violation of the terms of use",
            SuspensionReason.SecurityConcern => "Security concern",
            SuspensionReason.Other => "Other",
            _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null)
        };
    }
}
