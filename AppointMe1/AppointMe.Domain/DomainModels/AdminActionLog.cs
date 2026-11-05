using System.ComponentModel.DataAnnotations;

namespace AppointMe.Domain.DomainModels
{
    /// <summary>Stored as int, so don't renumber.</summary>
    public enum AdminActionType
    {
        TenantEdited = 0,
        TenantSuspended = 1,
        TenantReactivated = 2,
        DeletionRequestDismissed = 3,
        DeletionRequestApproved = 4,
        TenantDeleted = 5
    }

    public static class AdminActionTypes
    {
        public static string DisplayText(this AdminActionType action) => action switch
        {
            AdminActionType.TenantEdited => "Edited business details",
            AdminActionType.TenantSuspended => "Suspended",
            AdminActionType.TenantReactivated => "Reactivated",
            AdminActionType.DeletionRequestDismissed => "Dismissed the deletion request",
            AdminActionType.DeletionRequestApproved => "Deleted (request approved)",
            AdminActionType.TenantDeleted => "Deleted by the admin",
            _ => action.ToString()
        };
    }

    /// <summary>
    /// Who did what to which tenant, when and why. It has no foreign key to Business on purpose, because
    /// it must outlive a deleted tenant. Deleting a tenant keeps BusinessId, BusinessName, Action, Reason,
    /// At and AdminEmail on its rows, and clears Note and ChangedFields: the minimum rule applies to the
    /// tenant's data, not to who performed the action.
    /// </summary>
    public class AdminActionLog
    {
        public const int MaxReasonLength = 200;
        public const int MaxNoteLength = 500;
        public const int MaxChangedFieldsLength = 200;

        [Key]
        public Guid Id { get; set; }

        /// <summary>UTC.</summary>
        public DateTime At { get; set; }

        public AdminActionType Action { get; set; }

        public Guid BusinessId { get; set; }

        /// <summary>The name at the time of the action.</summary>
        [Required, MaxLength(200)]
        public string BusinessName { get; set; } = default!;

        [MaxLength(256)]
        public string? AdminEmail { get; set; }

        [MaxLength(MaxReasonLength)]
        public string? Reason { get; set; }

        [MaxLength(MaxNoteLength)]
        public string? Note { get; set; }

        /// <summary>For an edit: the names of the changed fields, never their values.</summary>
        [MaxLength(MaxChangedFieldsLength)]
        public string? ChangedFields { get; set; }
    }
}
