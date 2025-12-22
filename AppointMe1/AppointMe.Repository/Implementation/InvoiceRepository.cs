using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Repository.Implementation
{
    public class InvoiceRepository : BaseRepository<Invoice>, IInvoiceRepository
    {
        public InvoiceRepository(ApplicationDbContext context) : base(context) { }

        public async Task<Invoice?> GetByIdAsync(Guid id, Guid tenantId)
        {
            return await _dbSet
                .Include(i => i.Lines)
                .FirstOrDefaultAsync(i => i.Id == id && i.TenantId == tenantId);
        }

        public async Task<Invoice?> GetByAppointmentIdAsync(Guid appointmentId, Guid tenantId)
        {
            return await _dbSet
                .Include(i => i.Lines)
                .FirstOrDefaultAsync(i => i.AppointmentId == appointmentId && i.TenantId == tenantId);
        }

        public async Task<IEnumerable<Invoice>> GetAllByTenantAsync(Guid tenantId)
        {
            return await _dbSet
                .Include(i => i.Lines)
                .Where(i => i.TenantId == tenantId)
                .OrderByDescending(i => i.IssuedAt)
                .ToListAsync();
        }
        public async Task<int> GetMaxInvoiceSequenceForYearAsync(Guid tenantId, int year)
        {
            var prefix = $"INV-{year}-";

            var last = await _dbSet
                .Where(i => i.TenantId == tenantId && i.InvoiceNumber.StartsWith(prefix))
                .OrderByDescending(i => i.InvoiceNumber)
                .Select(i => i.InvoiceNumber)
                .FirstOrDefaultAsync();

            if (string.IsNullOrWhiteSpace(last)) return 0;

            var part = last.Replace(prefix, "");
            return int.TryParse(part, out var n) ? n : 0;
        }
    }
}
