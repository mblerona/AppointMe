namespace AppointMe.Service.Admin
{
    /// <summary>Support contact shown to suspended tenants (configuration key Support:Email).</summary>
    public sealed class SupportOptions
    {
        public const string SectionName = "Support";

        public string? Email { get; set; }
    }
}
