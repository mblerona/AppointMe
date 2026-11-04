using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;

namespace AppointMe.Service.Interface
{
    /// <summary>
    /// The services (treatments, products) a business offers. Every method is scoped to the tenant;
    /// a missing service or another tenant's service throws <see cref="KeyNotFoundException"/>.
    /// </summary>
    public interface IServiceOfferingService
    {
        /// <summary>All services of the tenant (active and inactive), with Category, not tracked.</summary>
        Task<IEnumerable<ServiceOffering>> GetAllAsync(Guid tenantId);

        /// <summary>
        /// The services an appointment form may offer: the active ones, plus any inactive ones
        /// already attached to the appointment being edited.
        /// </summary>
        Task<IEnumerable<ServiceOffering>> GetSelectableAsync(Guid tenantId, IEnumerable<Guid>? attachedIds = null);

        Task<ServiceOffering> GetByIdAsync(Guid id, Guid tenantId);

        /// <exception cref="Domain.Exceptions.BusinessRuleException">Invalid input or another tenant's category.</exception>
        Task<ServiceOffering> CreateAsync(CreateServiceOfferingDTO dto, Guid tenantId);

        /// <exception cref="Domain.Exceptions.BusinessRuleException">Invalid input or another tenant's category.</exception>
        Task UpdateAsync(Guid id, UpdateServiceOfferingDTO dto, Guid tenantId);

        /// <exception cref="Domain.Exceptions.BusinessRuleException">The service is used by appointments.</exception>
        Task DeleteAsync(Guid id, Guid tenantId);
    }
}
