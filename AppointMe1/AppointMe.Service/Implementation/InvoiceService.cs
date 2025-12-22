using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using AppointMe.Service.Interface;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Service.Implementation
{
    public class InvoiceService : IInvoiceService
    {
        private readonly IInvoiceRepository _invoiceRepository;
        private readonly IAppointmentRepository _appointmentRepository;
        private readonly ApplicationDbContext _db;

        public InvoiceService(
            IInvoiceRepository invoiceRepository,
            IAppointmentRepository appointmentRepository,
            ApplicationDbContext db)
        {
            _invoiceRepository = invoiceRepository;
            _appointmentRepository = appointmentRepository;
            _db = db;
        }

        public async Task<IEnumerable<InvoiceDTO>> GetAllAsync(Guid tenantId)
        {
            var invoices = await _invoiceRepository.GetAllByTenantAsync(tenantId);
            return invoices.Select(MapToDto).ToList();
        }

        public async Task<InvoiceDTO> GetByIdAsync(Guid invoiceId, Guid tenantId)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, tenantId);
            if (invoice == null) throw new KeyNotFoundException("Invoice not found.");
            return MapToDto(invoice);
        }

        public async Task<InvoiceDTO> CreateOrGetForAppointmentAsync(Guid appointmentId, Guid tenantId)
        {
           
            var biz = await _db.Businesses.FindAsync(tenantId);
            if (biz == null) throw new InvalidOperationException("Business not found.");
            if (!biz.EnableInvoices) throw new InvalidOperationException("Invoices are disabled in settings.");

          
            var existing = await _invoiceRepository.GetByAppointmentIdAsync(appointmentId, tenantId);
            if (existing != null) return MapToDto(existing);

           
            var appt = await _appointmentRepository.GetByIdAsync(appointmentId, tenantId);
            if (appt == null) throw new KeyNotFoundException("Appointment not found.");

        
            var hasServices = appt.AppointmentServices != null && appt.AppointmentServices.Any();

            

           
            var year = DateTime.UtcNow.Year;

           
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                var nextSeq = (await _invoiceRepository.GetMaxInvoiceSequenceForYearAsync(tenantId, year)) + 1;
                var number = $"INV-{year}-{nextSeq:0000}";

                var customerName = appt.Customer != null
                    ? $"{appt.Customer.FirstName} {appt.Customer.LastName}"
                    : "Customer";

                var invoice = new Invoice
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    AppointmentId = appt.Id,
                    CustomerId = appt.CustomerId,

                    InvoiceNumber = number,
                    IssuedAt = DateTime.UtcNow,
                    Status = InvoiceStatus.Draft,

                    CustomerNameSnapshot = customerName,
                    CustomerEmailSnapshot = appt.Customer?.Email,

                    BusinessNameSnapshot = biz.Name,
                    BusinessAddressSnapshot = biz.Address,
                    BusinessLogoSnapshot = biz.LogoUrl,
                    AppointmentOrderNumberSnapshot = appt.OrderNumber,
                    AppointmentDateSnapshot = appt.AppointmentDate,

                    Discount = 0m,
                    Tax = 0m
                };

               
                if (hasServices)
                {
                    foreach (var j in appt.AppointmentServices)
                    {
                        var svc = j.ServiceOffering;
                        var unit = j.PriceAtBooking;
                        invoice.Lines.Add(new InvoiceLine
                        {
                            Id = Guid.NewGuid(),
                            InvoiceId = invoice.Id,
                            NameSnapshot = svc?.Name ?? "Service",
                            CategorySnapshot = svc?.Category?.Name,
                            Qty = 1,
                            UnitPrice = unit,
                            LineTotal = unit
                        });
                    }
                }

                invoice.Subtotal = invoice.Lines.Sum(x => x.LineTotal);
                invoice.Total = invoice.Subtotal - invoice.Discount + invoice.Tax;

                invoice.CreatedAt = DateTime.UtcNow;
                invoice.UpdatedAt = DateTime.UtcNow;

                try
                {
                    await _invoiceRepository.AddAsync(invoice);
                    await _invoiceRepository.SaveChangesAsync();
                    return MapToDto(invoice);
                }
                catch (DbUpdateException)
                {
                    
                    if (attempt == 3) throw;
                }
            }

            throw new InvalidOperationException("Could not create invoice. Please retry.");
        }

        private static InvoiceDTO MapToDto(Invoice i)
        {
            return new InvoiceDTO
            {
                Id = i.Id,
                AppointmentId = i.AppointmentId,
                CustomerId = i.CustomerId,
                InvoiceNumber = i.InvoiceNumber,
                Status = i.Status.ToString(),
                IssuedAt = i.IssuedAt,

                CustomerName = i.CustomerNameSnapshot,
                CustomerEmail = i.CustomerEmailSnapshot,
                BusinessName = i.BusinessNameSnapshot,
                BusinessAddress = i.BusinessAddressSnapshot,
                BusinessLogoUrl = i.BusinessLogoSnapshot,

                OrderNumber = i.AppointmentOrderNumberSnapshot,
                AppointmentDate = i.AppointmentDateSnapshot,

                Subtotal = i.Subtotal,
                Discount = i.Discount,
                Tax = i.Tax,
                Total = i.Total,

                Lines = (i.Lines ?? new List<InvoiceLine>())
                    .Select(l => new InvoiceLineDTO
                    {
                        Name = l.NameSnapshot,
                        Category = l.CategorySnapshot,
                        Qty = l.Qty,
                        UnitPrice = l.UnitPrice,
                        LineTotal = l.LineTotal
                    })
                    .ToList()
            };
        }
    }
}
