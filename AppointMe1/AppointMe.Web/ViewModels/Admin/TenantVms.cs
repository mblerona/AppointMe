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
}
