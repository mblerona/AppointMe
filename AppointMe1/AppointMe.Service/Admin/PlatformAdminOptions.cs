namespace AppointMe.Service.Admin
{
    /// <summary>The platform admin account, from configuration (user secrets locally). Never log the password.</summary>
    public sealed class PlatformAdminOptions
    {
        public const string SectionName = "PlatformAdmin";

        public string? Email { get; set; }
        public string? Password { get; set; }
    }
}
