using AppointMe.Domain.DomainModels;

namespace AppointMe.Domain.DTO.Admin
{
    /// <summary>
    /// What the platform admin sees about a tenant: business-level information and counts only. Never add
    /// the tenants' customer details or appointment contents to these types.
    /// </summary>
    public class TenantSummaryDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";

        /// <summary>The tenant's oldest user; null when the Business has no users (placeholder Businesses).</summary>
        public string? OwnerEmail { get; set; }

        /// <summary>Stored in UTC; the admin service converts it for display.</summary>
        public DateTime CreatedAt { get; set; }

        public BusinessStatus Status { get; set; }
        public int CustomerCount { get; set; }
        public int AppointmentCount { get; set; }
        public int InvoiceCount { get; set; }
    }

    public class TenantDetailsDTO : TenantSummaryDTO
    {
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
    }

    /// <summary>The only tenant fields the admin can edit.</summary>
    public class UpdateTenantDTO
    {
        public string? Name { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
    }

    public class PlatformTotalsDTO
    {
        public int TotalTenants { get; set; }
        public int ActiveTenants { get; set; }
        public int SuspendedTenants { get; set; }
        public int NewTenantsLast30Days { get; set; }
    }
}
