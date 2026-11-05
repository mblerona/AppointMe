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

        /// <summary>Stored in UTC; the admin service converts it for display. Set while a request is pending.</summary>
        public DateTime? DeletionRequestedAt { get; set; }
    }

    public class TenantDetailsDTO : TenantSummaryDTO
    {
        public string? DeletionRequestReason { get; set; }

        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }

        /// <summary>Stored in UTC; the admin service converts it for display.</summary>
        public DateTime? SuspendedAt { get; set; }
        public SuspensionReason? SuspensionReason { get; set; }
        public string? SuspensionNote { get; set; }

        /// <summary>Newest first. Filled by the admin service.</summary>
        public IReadOnlyList<AdminActionLogDTO> ActionLog { get; set; } = Array.Empty<AdminActionLogDTO>();

        /// <summary>The tenant's login accounts, oldest first. Filled by the admin service.</summary>
        public IReadOnlyList<TenantAccountDTO> Accounts { get; set; } = Array.Empty<TenantAccountDTO>();
    }

    /// <summary>A login account's health. Never add a password hash, security stamp or token to this type.</summary>
    public class TenantAccountDTO
    {
        public string Id { get; set; } = "";
        public string? Email { get; set; }
        public bool EmailConfirmed { get; set; }

        /// <summary>The stored value, which can be in the past. Pages use <see cref="LockedOutUntil"/>.</summary>
        public DateTimeOffset? LockoutEnd { get; set; }

        /// <summary>Set only while the account is locked out. The admin service fills it, for display.</summary>
        public DateTime? LockedOutUntil { get; set; }

        public bool IsLockedOut => LockedOutUntil != null;
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

    public class AdminActionLogDTO
    {
        /// <summary>Stored in UTC; the admin service converts it for display.</summary>
        public DateTime At { get; set; }
        public AdminActionType Action { get; set; }
        public Guid BusinessId { get; set; }
        public string BusinessName { get; set; } = "";
        public string? AdminEmail { get; set; }
        public string? Reason { get; set; }
        public string? Note { get; set; }
        public string? ChangedFields { get; set; }

        /// <summary>False once the tenant has been deleted, so the log page doesn't link to it.</summary>
        public bool BusinessExists { get; set; }
    }

    public class DeletionRequestDTO
    {
        public Guid TenantId { get; set; }
        public string BusinessName { get; set; } = "";
        public string? OwnerEmail { get; set; }

        /// <summary>Stored in UTC; the admin service converts it for display.</summary>
        public DateTime RequestedAt { get; set; }
        public string? Reason { get; set; }
        public BusinessStatus Status { get; set; }
    }

    /// <summary>What happened to the email telling the tenant about an action. The action itself is saved either way.</summary>
    public enum TenantEmailOutcome
    {
        Sent,
        Failed,
        NoRecipient
    }

    /// <summary>The tenant is gone either way; these two steps can't be rolled back, so they run after the commit.</summary>
    public sealed record TenantDeletionResult(TenantEmailOutcome Email, bool LogoRemoved);
}
