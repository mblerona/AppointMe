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

        public async Task<bool> IsTimeSlotAvailableAsync(DateTime appointmentDate, Guid tenantId, Guid? excludeAppointmentId = null)
        {
            var query = _dbSet.Where(a =>
                a.TenantId == tenantId &&
                a.AppointmentDate == appointmentDate &&
                a.Status != AppointmentStatus.Cancelled);

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
    }
}
