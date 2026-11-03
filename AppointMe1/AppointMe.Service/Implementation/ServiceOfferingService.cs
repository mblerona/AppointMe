using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Repository.Interface;
using AppointMe.Service.Interface;

namespace AppointMe.Service.Implementation
{
    public class ServiceOfferingService : IServiceOfferingService
    {
        public const string UsedByAppointmentsMessage =
            "This service is used by existing appointments and cannot be deleted. Mark it as inactive instead.";

        public const string CategoryNotFoundMessage = "The selected category was not found.";

        private readonly IServiceOfferingRepository _repository;

        public ServiceOfferingService(IServiceOfferingRepository repository)
        {
            _repository = repository;
        }

        public Task<IEnumerable<ServiceOffering>> GetAllAsync(Guid tenantId) =>
            _repository.GetAllByBusinessAsync(tenantId);

        public async Task<ServiceOffering> GetByIdAsync(Guid id, Guid tenantId) =>
            await _repository.GetByIdAsync(id, tenantId)
            ?? throw new KeyNotFoundException("Service not found.");

        public async Task<ServiceOffering> CreateAsync(CreateServiceOfferingDTO dto, Guid tenantId)
        {
            var name = ValidateName(dto.Name);
            ValidatePrice(dto.Price);
            await EnsureCategoryBelongsToTenantAsync(dto.CategoryId, tenantId);

            var now = DateTime.UtcNow;
            var entity = new ServiceOffering
            {
                Id = Guid.NewGuid(),
                Name = name,
                Price = dto.Price,
                IsActive = dto.IsActive,
                CategoryId = dto.CategoryId,
                CreatedAt = now,
                UpdatedAt = now
            };

            // The repository sets BusinessId = tenantId.
            await _repository.AddAsync(entity, tenantId);
            await _repository.SaveChangesAsync();
            return entity;
        }

        public async Task UpdateAsync(Guid id, UpdateServiceOfferingDTO dto, Guid tenantId)
        {
            var entity = await GetByIdAsync(id, tenantId);

            var name = ValidateName(dto.Name);
            ValidatePrice(dto.Price);
            await EnsureCategoryBelongsToTenantAsync(dto.CategoryId, tenantId);

            entity.Name = name;
            entity.Price = dto.Price;
            entity.IsActive = dto.IsActive;
            entity.CategoryId = dto.CategoryId;
            entity.UpdatedAt = DateTime.UtcNow;

            await _repository.UpdateAsync(entity);
            await _repository.SaveChangesAsync();
        }

        public async Task DeleteAsync(Guid id, Guid tenantId)
        {
            var entity = await GetByIdAsync(id, tenantId);

            // Appointments keep a Restrict FK to their services, so deleting a used service would fail.
            if (await _repository.IsUsedByAppointmentsAsync(id, tenantId))
                throw new BusinessRuleException(UsedByAppointmentsMessage);

            await _repository.DeleteAsync(entity);
            await _repository.SaveChangesAsync();
        }

        private static string ValidateName(string? input)
        {
            var name = input?.Trim();
            if (string.IsNullOrEmpty(name))
                throw new BusinessRuleException("Service name is required.", nameof(CreateServiceOfferingDTO.Name));
            if (name.Length > 200)
                throw new BusinessRuleException("Service name can be at most 200 characters.", nameof(CreateServiceOfferingDTO.Name));
            return name;
        }

        private static void ValidatePrice(decimal price)
        {
            if (price < 0)
                throw new BusinessRuleException("Price can't be negative.", nameof(CreateServiceOfferingDTO.Price));
        }

        /// <summary>
        /// Audit S2: CategoryId comes from the form, so it must be checked against the tenant.
        /// Missing and other-tenant categories get the same message, so ids can't be probed.
        /// </summary>
        private async Task EnsureCategoryBelongsToTenantAsync(Guid? categoryId, Guid tenantId)
        {
            if (categoryId is not { } id)
                return;

            if (!await _repository.CategoryBelongsToBusinessAsync(id, tenantId))
                throw new BusinessRuleException(CategoryNotFoundMessage);
        }
    }
}
