using AppointMe.Domain.DTO;

namespace AppointMe.Service.Interface
{
    public interface IDashboardService
    {
        /// <param name="range">"today", "week" (next 7 days), "month" (this calendar month); anything else = all.</param>
        /// <param name="status">An AppointmentStatus name (case-insensitive); anything else = all.</param>
        /// <param name="search">Optional text matched against order number, customer name, phones and state.</param>
        /// <exception cref="KeyNotFoundException">The tenant has no Business.</exception>
        Task<DashboardDTO> GetDashboardAsync(Guid tenantId, string? range, string? status, string? search);
    }
}
