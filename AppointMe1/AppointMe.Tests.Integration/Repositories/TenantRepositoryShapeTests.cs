using AppointMe.Repository.Implementation;
using AppointMe.Repository.Interface;
using AwesomeAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;

namespace AppointMe.Tests.Integration.Repositories
{
    /// <summary>
    /// Audit A8: no tenant repository may offer a public read without the tenant.
    /// A method that returns a value counts as a read and must take a Guid named
    /// tenantId or businessId; methods that return nothing (writes, Detach) are fine.
    /// </summary>
    public class TenantRepositoryShapeTests
    {
        private static readonly string[] TenantParameterNames = { "tenantId", "businessId" };

        /// <summary>Return a value but read no tenant data. Add here only with a reason.</summary>
        private static readonly HashSet<string> NotReads = new()
        {
            "SaveChangesAsync",      // number of rows written
            "BeginTransactionAsync", // transaction handle
        };

        /// <summary>Reads across tenants by design; PlatformAdminBoundaryTests limits who may use it.</summary>
        private static readonly HashSet<Type> CrossTenantByDesign = new()
        {
            typeof(PlatformAdminRepository),
            typeof(IPlatformAdminRepository),
        };

        private static IEnumerable<Type> TenantRepositoryTypes() =>
            typeof(BaseRepository<>).Assembly.GetExportedTypes()
                .Where(t => t.Name.Split('`')[0].EndsWith("Repository"))
                .Where(t => !CrossTenantByDesign.Contains(t));

        [Fact]
        public void TenantRepositories_AreDiscovered()
        {
            // Guards against the scan silently finding nothing after a rename.
            TenantRepositoryTypes().Should().Contain(new[]
            {
                typeof(IRepository<>), typeof(BaseRepository<>),
                typeof(ICustomerRepository), typeof(CustomerRepository),
                typeof(IAppointmentRepository), typeof(AppointmentRepository),
                typeof(IInvoiceRepository), typeof(InvoiceRepository),
                typeof(IServiceOfferingRepository), typeof(ServiceOfferingRepository),
                typeof(IBusinessRepository), typeof(BusinessRepository),
            });
        }

        [Fact]
        public void EveryPublicRead_OnATenantRepository_TakesTheTenant()
        {
            var violations = TenantRepositoryTypes().SelectMany(Violations).ToList();

            violations.Should().BeEmpty(
                "every public read on a tenant repository must take a Guid tenantId or businessId " +
                "(audit A8); filter by tenant in the query instead of offering an unfiltered method");
        }

        [Fact]
        public void TheCheck_FlagsUnfilteredReads_AndAcceptsTenantScopedOnes()
        {
            Violations(typeof(FakeUnfilteredRepository)).Should().BeEquivalentTo(
                "FakeUnfilteredRepository.GetByIdAsync(Guid id)",
                "FakeUnfilteredRepository.GetAllAsync()",
                "FakeUnfilteredRepository.CountAsync(Nullable`1 tenantId)",
                "FakeUnfilteredRepository.FindByTenantAsync(String tenantId)");

            Violations(typeof(IFakeUnfilteredRepository)).Should().BeEquivalentTo(
                "IFakeBaseRepository`1.GetAllAsync()");

            Violations(typeof(FakeTenantScopedRepository)).Should().BeEmpty();
        }

        private static IEnumerable<string> Violations(Type type) =>
            PublicMethods(type)
                .Where(m => !m.IsSpecialName && m.DeclaringType != typeof(object))
                .Where(ReturnsValue)
                .Where(m => !NotReads.Contains(m.Name))
                .Where(m => !m.GetParameters().Any(p =>
                    p.ParameterType == typeof(Guid) && TenantParameterNames.Contains(p.Name)))
                .Select(Describe)
                .Distinct();

        /// <summary>Interfaces don't list inherited members, so walk their base interfaces too.</summary>
        private static IEnumerable<MethodInfo> PublicMethods(Type type) =>
            type.IsInterface
                ? new[] { type }.Concat(type.GetInterfaces()).SelectMany(i => i.GetMethods())
                : type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);

        private static bool ReturnsValue(MethodInfo m) =>
            m.ReturnType != typeof(void)
            && m.ReturnType != typeof(Task)
            && m.ReturnType != typeof(ValueTask);

        private static string Describe(MethodInfo m) =>
            $"{m.DeclaringType!.Name}.{m.Name}(" +
            string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}")) + ")";

        // ----------------- fakes for the self-check -----------------

        public class FakeUnfilteredRepository
        {
            public Task<object?> GetByIdAsync(Guid id) => Task.FromResult<object?>(null);
            public Task<IEnumerable<object>> GetAllAsync() => Task.FromResult(Enumerable.Empty<object>());
            public Task<int> CountAsync(Guid? tenantId) => Task.FromResult(0);
            public Task<object?> FindByTenantAsync(string tenantId) => Task.FromResult<object?>(null);
            public Task AddAsync(object entity) => Task.CompletedTask;
            public Task<int> SaveChangesAsync() => Task.FromResult(0);
            protected Task<object?> FindInternalAsync(Guid id) => Task.FromResult<object?>(null);
        }

        public interface IFakeBaseRepository<T>
        {
            Task<IEnumerable<T>> GetAllAsync();
            Task AddAsync(T entity);
        }

        public interface IFakeUnfilteredRepository : IFakeBaseRepository<object>
        {
            Task<object?> GetByIdAsync(Guid id, Guid tenantId);
        }

        public class FakeTenantScopedRepository
        {
            public Task<object?> GetByIdAsync(Guid id, Guid tenantId) => Task.FromResult<object?>(null);
            public Task<IEnumerable<object>> GetAllByBusinessAsync(Guid businessId) => Task.FromResult(Enumerable.Empty<object>());
            public void Detach(object entity) { }
            public Task DeleteAsync(object entity) => Task.CompletedTask;
        }
    }
}
