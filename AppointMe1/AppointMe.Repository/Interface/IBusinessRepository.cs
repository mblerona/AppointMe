using AppointMe.Domain.DomainModels;
using Microsoft.EntityFrameworkCore.Storage;

namespace AppointMe.Repository.Interface
{
    /// <summary>
    /// Access to the Business (tenant) row. Every read takes the tenant id, which is the Business id;
    /// there is deliberately no "get all" here (only a future admin service may read across tenants).
    /// </summary>
    public interface IBusinessRepository
    {
        /// <summary>Tracked, for updates.</summary>
        Task<Business?> GetByIdAsync(Guid tenantId);

        /// <summary>Not tracked, for display.</summary>
        Task<Business?> GetByIdReadOnlyAsync(Guid tenantId);

        void Add(Business business);

        /// <summary>Stops tracking a Business that was added but must not be saved.</summary>
        void Detach(Business business);

        Task<IDbContextTransaction> BeginTransactionAsync();

        Task<int> SaveChangesAsync();
    }
}
