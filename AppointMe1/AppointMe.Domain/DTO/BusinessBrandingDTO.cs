namespace AppointMe.Domain.DTO
{
    /// <summary>What the shared layout shows for the current tenant (name, logo, colors, nav items).</summary>
    public class BusinessBrandingDTO
    {
        public string Name { get; set; } = "";
        public string? LogoUrl { get; set; }
        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }
        public bool EnableServices { get; set; }

        /// <summary>Already false when services are off (invoices are built from services).</summary>
        public bool EnableInvoices { get; set; }
    }
}
