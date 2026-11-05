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

            if (status != null)
                businesses = businesses.Where(b => b.Status == status);

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
            var suspended = await _context.Businesses.CountAsync(b => b.Status == BusinessStatus.Suspended);

            return new PlatformTotalsDTO
            {
                TotalTenants = total,
                ActiveTenants = total - suspended,
                SuspendedTenants = suspended,
                NewTenantsLast30Days = await _context.Businesses.CountAsync(b => b.CreatedAt >= newSinceUtc)
            };
        }

        public async Task<IReadOnlyList<string>> GetUserEmailsAsync(Guid tenantId) =>
            await _context.Users
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId && u.Email != null)
                .OrderBy(u => u.CreatedAt)
                .Select(u => u.Email!)
                .ToListAsync();

        public void AddActionLog(AdminActionLog entry) => _context.AdminActionLogs.Add(entry);

        public async Task<IReadOnlyList<AdminActionLogDTO>> GetActionLogAsync(Guid? businessId, int limit)
        {
            var entries = _context.AdminActionLogs.AsNoTracking();

            if (businessId != null)
                entries = entries.Where(l => l.BusinessId == businessId);

            return await entries
                .OrderByDescending(l => l.At)
                .ThenBy(l => l.Id)
                .Take(limit)
                .Select(l => new AdminActionLogDTO
                {
                    At = l.At,
                    Action = l.Action,
                    BusinessId = l.BusinessId,
                    BusinessName = l.BusinessName,
                    AdminEmail = l.AdminEmail,
                    Reason = l.Reason,
                    Note = l.Note,
                    ChangedFields = l.ChangedFields,
                    BusinessExists = _context.Businesses.Any(b => b.Id == l.BusinessId)
                })
                .ToListAsync();
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
                Status = b.Status,
                CustomerCount = _context.Customers.Count(c => c.TenantId == b.Id),
                AppointmentCount = _context.Appointments.Count(a => a.TenantId == b.Id),
                InvoiceCount = _context.Invoices.Count(i => i.TenantId == b.Id),
                Email = b.Email,
                Phone = b.Phone,
                Address = b.Address,
                SuspendedAt = b.SuspendedAt,
                SuspensionReason = b.SuspensionReason,
                SuspensionNote = b.SuspensionNote
            });
    }
}
