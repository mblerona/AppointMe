using AppointMe.Domain.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Service.Interface
{
    public interface IInvoiceService
    {
        Task<InvoiceDTO> CreateOrGetForAppointmentAsync(Guid appointmentId, Guid tenantId);
        Task<InvoiceDTO> GetByIdAsync(Guid invoiceId, Guid tenantId);
        Task<IEnumerable<InvoiceDTO>> GetAllAsync(Guid tenantId);

        /// <summary>The invoice as a PDF, with the business logo it was issued with. KeyNotFoundException if missing or another tenant's.</summary>
        Task<byte[]> GeneratePdfAsync(Guid invoiceId, Guid tenantId);
    }
}
