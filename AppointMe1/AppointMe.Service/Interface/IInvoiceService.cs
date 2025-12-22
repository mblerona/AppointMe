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
    }
}
