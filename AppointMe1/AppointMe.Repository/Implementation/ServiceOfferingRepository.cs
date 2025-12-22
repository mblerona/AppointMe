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
    public class ServiceOfferingRepository : IServiceOfferingRepository
    {
        private readonly ApplicationDbContext _context;

        public ServiceOfferingRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<ServiceOffering>> GetAllByBusinessAsync(Guid businessId)
        {
            return await _context.ServiceOfferings
                .AsNoTracking()
                .Include(s => s.Category)
                .Where(s => s.BusinessId == businessId)
                .OrderBy(s => s.Category != null ? s.Category.Name : "")
                .ThenBy(s => s.Name)
                .ToListAsync();
        }

        public async Task<IEnumerable<ServiceOffering>> GetActiveByBusinessAsync(Guid businessId)
        {
            return await _context.ServiceOfferings
                .AsNoTracking()
                .Include(s => s.Category)
                .Where(s => s.BusinessId == businessId && s.IsActive)
                .OrderBy(s => s.Category != null ? s.Category.Name : "")
                .ThenBy(s => s.Name)
                .ToListAsync();
        }

        public async Task<ServiceOffering?> GetByIdAsync(Guid id, Guid businessId)
        {
            return await _context.ServiceOfferings
                .Include(s => s.Category)
                .FirstOrDefaultAsync(s => s.Id == id && s.BusinessId == businessId);
        }

        public async Task<IEnumerable<ServiceOffering>> GetByIdsForBusinessAsync(List<Guid> ids, Guid businessId)
        {
            if (ids == null || ids.Count == 0)
                return Enumerable.Empty<ServiceOffering>();

            var distinctIds = ids.Where(x => x != Guid.Empty).Distinct().ToList();

            return await _context.ServiceOfferings
                .AsNoTracking()
                .Include(s => s.Category)
                .Where(s => s.BusinessId == businessId && distinctIds.Contains(s.Id))
                .ToListAsync();
        }

        public async Task AddAsync(ServiceOffering entity, Guid businessId)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (businessId == Guid.Empty) throw new ArgumentException("BusinessId is required.", nameof(businessId));

            // Force tenant/business ownership here to prevent "Business required" errors
            entity.BusinessId = businessId;

            await _context.ServiceOfferings.AddAsync(entity);
        }

        public Task UpdateAsync(ServiceOffering entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            _context.ServiceOfferings.Update(entity);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(ServiceOffering entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            _context.ServiceOfferings.Remove(entity);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync() => _context.SaveChangesAsync();
    }
}
