using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.TimeZones;
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
        private readonly IBusinessRepository _businessRepository;
        private readonly TimeProvider _timeProvider;

        public InvoiceService(
            IInvoiceRepository invoiceRepository,
            IAppointmentRepository appointmentRepository,
            IBusinessRepository businessRepository,
            TimeProvider timeProvider)
        {
            _invoiceRepository = invoiceRepository;
            _appointmentRepository = appointmentRepository;
            _businessRepository = businessRepository;
            _timeProvider = timeProvider;
        }

        public async Task<IEnumerable<InvoiceDTO>> GetAllAsync(Guid tenantId)
        {
            var invoices = await _invoiceRepository.GetAllByTenantAsync(tenantId);
            var zone = await ZoneOfAsync(tenantId);
            return invoices.Select(i => MapToDto(i, zone)).ToList();
        }

        public async Task<InvoiceDTO> GetByIdAsync(Guid invoiceId, Guid tenantId)
        {
            var invoice = await _invoiceRepository.GetByIdAsync(invoiceId, tenantId);
            if (invoice == null) throw new KeyNotFoundException("Invoice not found.");
            return MapToDto(invoice, await ZoneOfAsync(tenantId));
        }

        private async Task<string?> ZoneOfAsync(Guid tenantId) =>
            (await _businessRepository.GetByIdReadOnlyAsync(tenantId))?.TimeZoneId;

        public async Task<InvoiceDTO> CreateOrGetForAppointmentAsync(Guid appointmentId, Guid tenantId)
        {
           
            var biz = await _businessRepository.GetByIdReadOnlyAsync(tenantId);
            if (biz == null) throw new BusinessRuleException("Business not found.");
            if (!biz.EnableInvoices) throw new BusinessRuleException("Invoices are disabled in settings.");

          
            var existing = await _invoiceRepository.GetByAppointmentIdAsync(appointmentId, tenantId);
            if (existing != null) return MapToDto(existing, biz.TimeZoneId);

           
            var appt = await _appointmentRepository.GetByIdAsync(appointmentId, tenantId);
            if (appt == null) throw new KeyNotFoundException("Appointment not found.");

        
            var hasServices = appt.AppointmentServices != null && appt.AppointmentServices.Any();

            

            // The business's year: on New Year's Eve it can differ from the year in UTC.
            var year = BusinessTimeZones.Now(_timeProvider, biz.TimeZoneId).Year;
            var utcNow = _timeProvider.GetUtcNow().UtcDateTime;

           
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                if (attempt > 1)
                {
                    // A concurrent request may have created the invoice for this appointment meanwhile.
                    var created = await _invoiceRepository.GetByAppointmentIdAsync(appointmentId, tenantId);
                    if (created != null) return MapToDto(created, biz.TimeZoneId);
                }

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
                    InvoiceYear = year,
                    InvoiceSequence = nextSeq,
                    IssuedAt = utcNow,
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

                invoice.CreatedAt = utcNow;
                invoice.UpdatedAt = utcNow;

                try
                {
                    await _invoiceRepository.AddAsync(invoice);
                    await _invoiceRepository.SaveChangesAsync();
                    return MapToDto(invoice, biz.TimeZoneId);
                }
                catch (DbUpdateException)
                {
                    // A unique index was hit (number or appointment taken concurrently). The failed
                    // invoice and its lines are still tracked as Added; detach them, otherwise the
                    // next SaveChanges would try to insert them again and every retry would fail.
                    _invoiceRepository.Detach(invoice);

                    if (attempt == 3) throw;
                }
            }

            throw new BusinessRuleException("Could not create invoice. Please retry.");
        }

        /// <summary>
        /// IssuedAt is stored in UTC, so it is converted for display. AppointmentDateSnapshot isn't:
        /// appointment times are already the business's clock times.
        /// </summary>
        private static InvoiceDTO MapToDto(Invoice i, string? zone)
        {
            return new InvoiceDTO
            {
                Id = i.Id,
                AppointmentId = i.AppointmentId,
                CustomerId = i.CustomerId,
                InvoiceNumber = i.InvoiceNumber,
                Status = i.Status.ToString(),
                IssuedAt = BusinessTimeZones.FromUtc(i.IssuedAt, zone),

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
