using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using Microsoft.EntityFrameworkCore;

namespace AppointMe.Repository.Implementation
{
    public class PlatformAdminRepository : IPlatformAdminRepository
    {
        private readonly ApplicationDbContext _context;

        public PlatformAdminRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IReadOnlyList<TenantSummaryDTO>> GetTenantsAsync(string? search, BusinessStatus? status)
        {
            var businesses = _context.Businesses.AsNoTracking();

            // Every Business is Active until suspension exists.
            if (status != null && status != BusinessStatus.Active)
                businesses = businesses.Where(b => false);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                businesses = businesses.Where(b =>
                    b.Name.ToLower().Contains(term)
                    || (b.Email != null && b.Email.ToLower().Contains(term))
                    || _context.Users.Any(u => u.TenantId == b.Id && u.Email != null && u.Email.ToLower().Contains(term)));
            }

            return await ToDetails(businesses)
                .OrderByDescending(t => t.CreatedAt)
                .ThenBy(t => t.Name)
                .ToListAsync();
        }

        public Task<TenantDetailsDTO?> GetTenantAsync(Guid tenantId) =>
            ToDetails(_context.Businesses.AsNoTracking().Where(b => b.Id == tenantId)).FirstOrDefaultAsync();

        public async Task<PlatformTotalsDTO> GetTotalsAsync(DateTime newSinceUtc)
        {
            var total = await _context.Businesses.CountAsync();

            return new PlatformTotalsDTO
            {
                TotalTenants = total,
                ActiveTenants = total,
                SuspendedTenants = 0,
                NewTenantsLast30Days = await _context.Businesses.CountAsync(b => b.CreatedAt >= newSinceUtc)
            };
        }

        private IQueryable<TenantDetailsDTO> ToDetails(IQueryable<Business> businesses) =>
            businesses.Select(b => new TenantDetailsDTO
            {
                Id = b.Id,
                Name = b.Name,
                OwnerEmail = _context.Users
                    .Where(u => u.TenantId == b.Id)
                    .OrderBy(u => u.CreatedAt)
                    .ThenBy(u => u.Id)
                    .Select(u => u.Email)
                    .FirstOrDefault(),
                CreatedAt = b.CreatedAt,
                Status = BusinessStatus.Active,
                CustomerCount = _context.Customers.Count(c => c.TenantId == b.Id),
                AppointmentCount = _context.Appointments.Count(a => a.TenantId == b.Id),
                InvoiceCount = _context.Invoices.Count(i => i.TenantId == b.Id),
                Email = b.Email,
                Phone = b.Phone,
                Address = b.Address
            });
    }
}
