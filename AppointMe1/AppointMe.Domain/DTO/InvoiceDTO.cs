using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Domain.DTO
{
    public class InvoiceDTO
    {
        public Guid Id { get; set; }
        public Guid AppointmentId { get; set; }
        public Guid CustomerId { get; set; }

        public string InvoiceNumber { get; set; } = "";
        public string Status { get; set; } = "";
        public DateTime IssuedAt { get; set; }

        public string CustomerName { get; set; } = "";
        public string? CustomerEmail { get; set; }

        public string BusinessName { get; set; } = "";
        public string? BusinessAddress { get; set; }
        public string? BusinessLogoUrl { get; set; }

        public string? OrderNumber { get; set; }
        public DateTime AppointmentDate { get; set; }

        public decimal Subtotal { get; set; }
        public decimal Discount { get; set; }
        public decimal Tax { get; set; }
        public decimal Total { get; set; }

        public List<InvoiceLineDTO> Lines { get; set; } = new();
    }
}
