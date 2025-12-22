using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DomainModels
{
    public class InvoiceLine
    {
        [Key]
        public Guid Id { get; set; }

        public Guid InvoiceId { get; set; }
        public Invoice Invoice { get; set; } = default!;

        [Required, MaxLength(200)]
        public string NameSnapshot { get; set; } = default!;

        [MaxLength(120)]
        public string? CategorySnapshot { get; set; }

        public int Qty { get; set; } = 1;

        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }
    }
}
