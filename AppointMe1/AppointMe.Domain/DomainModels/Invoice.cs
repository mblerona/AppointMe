using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DomainModels
{
    public enum InvoiceStatus
    {
        Draft = 0,
        Sent = 1,
        Paid = 2,
        Voided = 3
    }
    public class Invoice
    {

        [Key]
        public Guid Id { get; set; }

        public Guid TenantId { get; set; }
        public Guid AppointmentId { get; set; }
        public Guid CustomerId { get; set; }
        public string? BusinessLogoSnapshot { get; set; }


        [Required, MaxLength(50)]
        public string InvoiceNumber { get; set; } = default!;

        /// <summary>The year in the number, in the business's time zone.</summary>
        public int InvoiceYear { get; set; }

        /// <summary>
        /// The running number within the tenant and year. Stored as a number because sorting the
        /// text form puts INV-2026-10000 before INV-2026-9999 (audit B10).
        /// </summary>
        public int InvoiceSequence { get; set; }

        public DateTime IssuedAt { get; set; } = DateTime.UtcNow;

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Draft;

        [Required, MaxLength(120)]
        public string CustomerNameSnapshot { get; set; } = default!;

        [MaxLength(256)]
        public string? CustomerEmailSnapshot { get; set; }

        [Required, MaxLength(200)]
        public string BusinessNameSnapshot { get; set; } = default!;

        [MaxLength(500)]
        public string? BusinessAddressSnapshot { get; set; }

        [MaxLength(50)]
        public string? AppointmentOrderNumberSnapshot { get; set; }

        public DateTime AppointmentDateSnapshot { get; set; }

        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();


    }
}
