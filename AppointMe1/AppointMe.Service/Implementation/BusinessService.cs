using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Service.Interface;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AppointMe.Service.Implementation
{
    public class BusinessService : IBusinessService
    {
        public const int MaxBusinessNameLength = 200;

        private readonly ApplicationDbContext _db;
        private readonly UserManager<AppointMeAppUser> _userManager;

        public BusinessService(ApplicationDbContext db, UserManager<AppointMeAppUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        public async Task<IdentityResult> RegisterOwnerAsync(AppointMeAppUser user, string password, string businessName)
        {
            var name = businessName?.Trim();
            if (string.IsNullOrEmpty(name))
                throw new BusinessRuleException("Business name is required.");
            if (name.Length > MaxBusinessNameLength)
                throw new BusinessRuleException($"Business name can be at most {MaxBusinessNameLength} characters.");
            if (user.TenantId != null)
                throw new InvalidOperationException("A new owner must not belong to a tenant yet.");

            var now = DateTime.UtcNow;
            var business = new Business
            {
                Id = Guid.NewGuid(),
                Name = name,
                EnableServices = true,
                EnableInvoices = false,
                EnableStaffing = true,
                EnablePayments = true,
                DefaultSlotMinutes = 30,
                CreatedAt = now,
                UpdatedAt = now
            };

            user.TenantId = business.Id;
            user.CreatedAt = now;
            user.UpdatedAt = now;

            await using var transaction = await _db.Database.BeginTransactionAsync();
            var committed = false;
            try
            {
                // UserManager's store uses this same scoped DbContext, so the SaveChanges inside
                // CreateAsync stores the Business and the user together, inside the transaction.
                _db.Businesses.Add(business);

                var result = await _userManager.CreateAsync(user, password);
                if (!result.Succeeded)
                    return result;

                await transaction.CommitAsync();
                committed = true;
                return result;
            }
            finally
            {
                if (!committed)
                {
                    // Validation failures return before anything is saved; make sure the Business
                    // isn't saved later by another SaveChanges on this context.
                    _db.Entry(business).State = EntityState.Detached;
                    user.TenantId = null;
                }
            }
        }
    }
}
