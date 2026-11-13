using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AppointMe.Repository.Interface
{
    /// <summary>
    /// Writes only. Reads live on each entity's repository and always take the tenant,
    /// so no method here can return another tenant's rows (TenantRepositoryShapeTests).
    /// </summary>
    public interface IRepository<T> where T : class
    {
        Task AddAsync(T entity);
        Task UpdateAsync(T entity);
        Task DeleteAsync(T entity);
        Task SaveChangesAsync();
    }
}
