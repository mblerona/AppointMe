using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AppointMe.Repository.Implementation
{
    public class AppointmentRepository : BaseRepository<Appointment>, IAppointmentRepository
    {
        public AppointmentRepository(ApplicationDbContext context) : base(context) { }

        public async Task<Appointment?> GetByIdAsync(Guid id, Guid tenantId)
        {
            return await _dbSet
                .Include(a => a.Customer)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(x => x.ServiceOffering)
                        .ThenInclude(s => s.Category)
                .FirstOrDefaultAsync(a => a.Id == id && a.TenantId == tenantId);
        }

        public Task UpdateAsync(Appointment appointment)
        {
            _dbSet.Update(appointment);
            return Task.CompletedTask;
        }

        public async Task<IReadOnlyDictionary<AppointmentStatus, int>> CountByStatusAsync(Guid tenantId)
        {
            return await _dbSet
                .AsNoTracking()
                .Where(a => a.TenantId == tenantId)
                .GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Status, x => x.Count);
        }

        public async Task<IReadOnlyList<Appointment>> GetForDashboardAsync(
            Guid tenantId, DateTime? start, DateTime? end, AppointmentStatus? status, string? search, string? normalizedPhone, int take)
        {
            var q = _dbSet
                .AsNoTracking()
                .Include(a => a.Customer)
                .Where(a => a.TenantId == tenantId);

            if (start.HasValue && end.HasValue)
                q = q.Where(a => a.AppointmentDate >= start.Value && a.AppointmentDate < end.Value);

            if (status.HasValue)
                q = q.Where(a => a.Status == status.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                q = q.Where(a =>
                    a.OrderNumber.Contains(s) ||
                    a.Customer!.FirstName.Contains(s) ||
                    a.Customer!.LastName.Contains(s) ||
                    a.Customer!.PhoneNumber.Contains(s) ||
                    (a.Customer!.SecondPhoneNumber != null && a.Customer.SecondPhoneNumber.Contains(s)) ||
                    a.Customer!.State.Contains(s) ||
                    (normalizedPhone != null &&
                        (a.Customer!.PhoneNumber == normalizedPhone || a.Customer.SecondPhoneNumber == normalizedPhone)));
            }

            return await q
                .OrderByDescending(a => a.AppointmentDate)
                .Take(take)
                .ToListAsync();
        }

        public async Task<IEnumerable<Appointment>> GetByCustomerIdAsync(Guid customerId, Guid tenantId)
        {
            return await _dbSet
                .Include(a => a.Customer)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(x => x.ServiceOffering)
                        .ThenInclude(s => s.Category)
                .Where(a => a.CustomerId == customerId && a.TenantId == tenantId)
                .OrderByDescending(a => a.AppointmentDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Appointment>> GetByDateRangeAsync(DateTime startDate, DateTime endDate, Guid tenantId)
        {
            return await _dbSet
                .Include(a => a.Customer)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(x => x.ServiceOffering)
                        .ThenInclude(s => s.Category)
                .Where(a => a.TenantId == tenantId &&
                            a.AppointmentDate >= startDate && a.AppointmentDate <= endDate)
                .OrderBy(a => a.AppointmentDate)
                .ToListAsync();
        }

        public async Task<IEnumerable<Appointment>> GetByStatusAsync(AppointmentStatus status, Guid tenantId)
        {
            return await _dbSet
                .Include(a => a.Customer)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(x => x.ServiceOffering)
                        .ThenInclude(s => s.Category)
                .Where(a => a.TenantId == tenantId && a.Status == status)
                .OrderBy(a => a.AppointmentDate)
                .ToListAsync();
        }

        public async Task<Appointment?> GetWithCustomerAsync(Guid appointmentId, Guid tenantId)
        {
            return await _dbSet
                .Include(a => a.Customer)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(x => x.ServiceOffering)
                        .ThenInclude(s => s.Category)
                .FirstOrDefaultAsync(a => a.Id == appointmentId && a.TenantId == tenantId);
        }


        public async Task<bool> IsTimeSlotAvailableAsync(
            DateTime startTime,
            int durationMinutes,
            Guid tenantId,
            Guid? excludeAppointmentId = null)
        {
            if (durationMinutes <= 0)
                durationMinutes = 30;

            var endTime = startTime.AddMinutes(durationMinutes);

            // Overlap rule:
            // existingStart < newEnd AND existingEnd > newStart
            // durationMinutes is the length of the new booking; each existing one ends by its own stored length.
            var query = _dbSet.Where(a =>
                a.TenantId == tenantId &&
                a.Status != AppointmentStatus.Cancelled &&
                a.AppointmentDate < endTime &&
                a.AppointmentDate.AddMinutes(a.DurationMinutes) > startTime
            );

            if (excludeAppointmentId.HasValue)
                query = query.Where(a => a.Id != excludeAppointmentId.Value);

            return !await query.AnyAsync();
        }

        public async Task<IEnumerable<Appointment>> GetAllByTenantAsync(Guid tenantId)
        {
            return await _dbSet
                .Include(a => a.Customer)
                .Include(a => a.AppointmentServices)
                    .ThenInclude(x => x.ServiceOffering)
                        .ThenInclude(s => s.Category)
                .Where(a => a.TenantId == tenantId)
                .OrderByDescending(a => a.AppointmentDate)
                .ToListAsync();
        }

        public async Task<bool> OrderNumberExistsAsync(string orderNumber, Guid tenantId, Guid? excludeAppointmentId = null)
        {
            var normalized = orderNumber?.Trim();

            if (string.IsNullOrWhiteSpace(normalized))
                return false;

            var query = _dbSet.Where(a => a.TenantId == tenantId && a.OrderNumber == normalized);

            if (excludeAppointmentId.HasValue)
                query = query.Where(a => a.Id != excludeAppointmentId.Value);

            return await query.AnyAsync();
        }
    }
}



