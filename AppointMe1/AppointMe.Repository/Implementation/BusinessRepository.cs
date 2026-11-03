using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AppointMe.Repository.Implementation
{
    public class BusinessRepository : IBusinessRepository
    {
        private readonly ApplicationDbContext _context;

        public BusinessRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public Task<Business?> GetByIdAsync(Guid tenantId) =>
            _context.Businesses.FirstOrDefaultAsync(b => b.Id == tenantId);

        public Task<Business?> GetByIdReadOnlyAsync(Guid tenantId) =>
            _context.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == tenantId);

        public void Add(Business business) => _context.Businesses.Add(business);

        public void Detach(Business business) => _context.Entry(business).State = EntityState.Detached;

        public Task<IDbContextTransaction> BeginTransactionAsync() => _context.Database.BeginTransactionAsync();

        public Task<int> SaveChangesAsync() => _context.SaveChangesAsync();
    }
}
