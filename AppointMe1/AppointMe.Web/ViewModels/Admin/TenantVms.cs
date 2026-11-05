using System.ComponentModel.DataAnnotations;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;
using AppointMe.Domain.Validation;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace AppointMe.Web.ViewModels.Admin
{
    public class TenantListVm
    {
        public string? Search { get; set; }
        public BusinessStatus? Status { get; set; }
        public IReadOnlyList<TenantSummaryDTO> Tenants { get; set; } = Array.Empty<TenantSummaryDTO>();
    }

    public class TenantEditVm
    {
        /// <summary>Always taken from the URL, never from the form.</summary>
        [BindNever]
        public Guid Id { get; set; }

        [Required, MaxLength(200)]
        [Display(Name = "Business name")]
        public string Name { get; set; } = "";

        [EmailAddress, MaxLength(256)]
        public string? Email { get; set; }

        [ValidPhoneNumber]
        public string? Phone { get; set; }

        [MaxLength(500)]
        public string? Address { get; set; }
    }

    public class TenantSuspendVm
    {
        [BindNever]
        public Guid Id { get; set; }

        [BindNever]
        public string BusinessName { get; set; } = "";

        [Required(ErrorMessage = "Please choose a reason from the list.")]
        public SuspensionReason? Reason { get; set; }

        [MaxLength(AdminActionLog.MaxNoteLength)]
        public string? Note { get; set; }
    }

    public class AdminHomeVm
    {
        public PlatformTotalsDTO Totals { get; set; } = new();
        public IReadOnlyList<DeletionRequestDTO> PendingDeletionRequests { get; set; } = Array.Empty<DeletionRequestDTO>();
    }

    /// <summary>The same page for approving a request and for deleting without one.</summary>
    public class TenantDeleteVm
    {
        [BindNever]
        public Guid Id { get; set; }

        [BindNever]
        public bool IsApproval { get; set; }

        [BindNever]
        public TenantDetailsDTO Tenant { get; set; } = new();

        [Required(ErrorMessage = "Type the business name to confirm.")]
        [Display(Name = "Business name")]
        public string? ConfirmName { get; set; }

        [MaxLength(AdminActionLog.MaxReasonLength)]
        [Display(Name = "Reason (optional)")]
        public string? Reason { get; set; }
    }

    public class TenantDismissVm
    {
        [BindNever]
        public Guid Id { get; set; }

        [BindNever]
        public string BusinessName { get; set; } = "";

        [MaxLength(AdminActionLog.MaxNoteLength)]
        public string? Note { get; set; }
    }

    public class TenantReactivateVm
    {
        [BindNever]
        public Guid Id { get; set; }

        [BindNever]
        public string BusinessName { get; set; } = "";

        [MaxLength(AdminActionLog.MaxNoteLength)]
        public string? Note { get; set; }
    }
}
