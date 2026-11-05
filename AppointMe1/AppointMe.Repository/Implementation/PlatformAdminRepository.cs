using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO.Admin;
using AppointMe.Domain.Identity;
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

        public async Task<IReadOnlyList<TenantAccountDTO>> GetTenantAccountsAsync(Guid tenantId) =>
            await _context.Users
                .AsNoTracking()
                .Where(u => u.TenantId == tenantId
                    && !_context.UserRoles.Any(ur => ur.UserId == u.Id
                        && _context.Roles.Any(r => r.Id == ur.RoleId && r.Name == PlatformRoles.Admin)))
                .OrderBy(u => u.CreatedAt)
                .ThenBy(u => u.Id)
                .Select(u => new TenantAccountDTO
                {
                    Id = u.Id,
                    Email = u.Email,
                    EmailConfirmed = u.EmailConfirmed,
                    // Identity ignores LockoutEnd on an account with lockout turned off.
                    LockoutEnd = u.LockoutEnabled ? u.LockoutEnd : null,
                    LastLoginAt = u.LastLoginAt
                })
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

        public async Task<IReadOnlyList<DeletionRequestDTO>> GetPendingDeletionRequestsAsync() =>
            await _context.Businesses
                .AsNoTracking()
                .Where(b => b.DeletionRequestedAt != null)
                .OrderBy(b => b.DeletionRequestedAt)
                .ThenBy(b => b.Name)
                .Select(b => new DeletionRequestDTO
                {
                    TenantId = b.Id,
                    BusinessName = b.Name,
                    OwnerEmail = _context.Users
                        .Where(u => u.TenantId == b.Id)
                        .OrderBy(u => u.CreatedAt)
                        .ThenBy(u => u.Id)
                        .Select(u => u.Email)
                        .FirstOrDefault(),
                    RequestedAt = b.DeletionRequestedAt!.Value,
                    Reason = b.DeletionRequestReason,
                    Status = b.Status
                })
                .ToListAsync();

        public async Task<bool> HasCrossTenantLinksAsync(Guid tenantId)
        {
            var appointmentsOnOurCustomers = await _context.Appointments.AnyAsync(a =>
                a.TenantId != tenantId
                && _context.Customers.Any(c => c.Id == a.CustomerId && c.TenantId == tenantId));

            var linesOnOurServices = await _context.AppointmentServices.AnyAsync(line =>
                _context.ServiceOfferings.Any(s => s.Id == line.ServiceOfferingId && s.BusinessId == tenantId)
                && _context.Appointments.Any(a => a.Id == line.AppointmentId && a.TenantId != tenantId));

            var servicesInOurCategories = await _context.ServiceOfferings.AnyAsync(s =>
                s.BusinessId != tenantId
                && _context.ServiceCategories.Any(c => c.Id == s.CategoryId && c.BusinessId == tenantId));

            return appointmentsOnOurCustomers || linesOnOurServices || servicesInOurCategories;
        }

        public async Task DeleteTenantAsync(Guid tenantId, AdminActionLog deletionEntry)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Every foreign key to Business is NO ACTION and invoices have none to appointments or
                // customers, so each step is saved on its own, in this order.
                await RemoveAsync(_context.InvoiceLines.Where(l =>
                    _context.Invoices.Any(i => i.Id == l.InvoiceId && i.TenantId == tenantId)));
                await RemoveAsync(_context.Invoices.Where(i => i.TenantId == tenantId));
                await RemoveAsync(_context.AppointmentServices.Where(line =>
                    _context.Appointments.Any(a => a.Id == line.AppointmentId && a.TenantId == tenantId)));
                await RemoveAsync(_context.Appointments.Where(a => a.TenantId == tenantId));
                await RemoveAsync(_context.Customers.Where(c => c.TenantId == tenantId));
                await RemoveAsync(_context.ServiceOfferings.Where(s => s.BusinessId == tenantId));
                await RemoveAsync(_context.ServiceCategories.Where(c => c.BusinessId == tenantId));
                await RemoveUsersAsync(tenantId);

                await ReduceActionLogAsync(tenantId);
                _context.AdminActionLogs.Add(deletionEntry);

                _context.Businesses.Remove(await _context.Businesses.SingleAsync(b => b.Id == tenantId));
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
            }
            catch
            {
                // Nothing is saved; don't let a later save on this context try again.
                _context.ChangeTracker.Clear();
                throw;
            }
        }

        private async Task RemoveAsync<T>(IQueryable<T> rows) where T : class
        {
            _context.RemoveRange(await rows.ToListAsync());
            await _context.SaveChangesAsync();
        }

        private async Task RemoveUsersAsync(Guid tenantId)
        {
            var users = await _context.Users.Where(u => u.TenantId == tenantId).ToListAsync();
            var userIds = users.Select(u => u.Id).ToList();

            var adminIds = await _context.UserRoles
                .Where(ur => userIds.Contains(ur.UserId)
                    && _context.Roles.Any(r => r.Id == ur.RoleId && r.Name == PlatformRoles.Admin))
                .Select(ur => ur.UserId)
                .ToListAsync();

            foreach (var admin in users.Where(u => adminIds.Contains(u.Id)))
                admin.TenantId = null;

            var deleted = users.Where(u => !adminIds.Contains(u.Id)).ToList();
            var deletedIds = deleted.Select(u => u.Id).ToList();

            _context.UserRoles.RemoveRange(await _context.UserRoles.Where(x => deletedIds.Contains(x.UserId)).ToListAsync());
            _context.UserClaims.RemoveRange(await _context.UserClaims.Where(x => deletedIds.Contains(x.UserId)).ToListAsync());
            _context.UserLogins.RemoveRange(await _context.UserLogins.Where(x => deletedIds.Contains(x.UserId)).ToListAsync());
            _context.UserTokens.RemoveRange(await _context.UserTokens.Where(x => deletedIds.Contains(x.UserId)).ToListAsync());
            _context.Users.RemoveRange(deleted);

            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// After a tenant is deleted its log keeps the business's name and id, action, reason and date, and who
        /// did it. The free text and field names, which are about the tenant, go.
        /// </summary>
        private async Task ReduceActionLogAsync(Guid tenantId)
        {
            var entries = await _context.AdminActionLogs.Where(l => l.BusinessId == tenantId).ToListAsync();
            foreach (var entry in entries)
            {
                entry.Note = null;
                entry.ChangedFields = null;
            }
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
                SuspensionNote = b.SuspensionNote,
                DeletionRequestedAt = b.DeletionRequestedAt,
                DeletionRequestReason = b.DeletionRequestReason,
                // The same accounts as GetTenantAccountsAsync: an admin account is never one of them.
                LastLoginAt = _context.Users
                    .Where(u => u.TenantId == b.Id
                        && !_context.UserRoles.Any(ur => ur.UserId == u.Id
                            && _context.Roles.Any(r => r.Id == ur.RoleId && r.Name == PlatformRoles.Admin)))
                    .Max(u => u.LastLoginAt)
            });
    }
}
