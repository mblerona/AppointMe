using AppointMe.Domain.DomainModels;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AppointMe.Repository.Interface
{
    public interface IServiceOfferingRepository
    {

        Task<IEnumerable<ServiceOffering>> GetAllByBusinessAsync(Guid businessId);
        Task<IEnumerable<ServiceOffering>> GetActiveByBusinessAsync(Guid businessId);


        Task<IEnumerable<ServiceOffering>> GetByIdsForBusinessAsync(List<Guid> ids, Guid businessId);
        Task<ServiceOffering?> GetByIdAsync(Guid id, Guid businessId);


        Task AddAsync(ServiceOffering entity, Guid businessId);
        Task UpdateAsync(ServiceOffering entity);
        Task DeleteAsync(ServiceOffering entity);
        Task<int> SaveChangesAsync();
    }
}
