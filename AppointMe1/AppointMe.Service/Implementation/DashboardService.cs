using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Repository.Interface;
using AppointMe.Service.Interface;

namespace AppointMe.Service.Implementation
{
    public class DashboardService : IDashboardService
    {
        public const int MaxRows = 20;

        private readonly IBusinessRepository _businessRepository;
        private readonly IAppointmentRepository _appointmentRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly TimeProvider _timeProvider;

        public DashboardService(
            IBusinessRepository businessRepository,
            IAppointmentRepository appointmentRepository,
            ICustomerRepository customerRepository,
            TimeProvider timeProvider)
        {
            _businessRepository = businessRepository;
            _appointmentRepository = appointmentRepository;
            _customerRepository = customerRepository;
            _timeProvider = timeProvider;
        }

        public async Task<DashboardDTO> GetDashboardAsync(Guid tenantId, string? range, string? status, string? search)
        {
            var business = await _businessRepository.GetByIdReadOnlyAsync(tenantId)
                ?? throw new KeyNotFoundException("Business not found.");

            var (start, end) = GetRange(range);
            AppointmentStatus? statusFilter =
                !string.IsNullOrWhiteSpace(status) && status != "all" &&
                Enum.TryParse<AppointmentStatus>(status, ignoreCase: true, out var parsed)
                    ? parsed
                    : null;

            var counts = await _appointmentRepository.CountByStatusAsync(tenantId);
            var customers = await _customerRepository.CountByTenantAsync(tenantId);
            var rows = await _appointmentRepository.GetForDashboardAsync(
                tenantId, start, end, statusFilter, search, MaxRows);

            return new DashboardDTO
            {
                BusinessName = business.Name,
                LogoUrl = business.LogoUrl,
                TotalAppointments = counts.Values.Sum(),
                ScheduledAppointments = counts.GetValueOrDefault(AppointmentStatus.Scheduled),
                CompletedAppointments = counts.GetValueOrDefault(AppointmentStatus.Completed),
                CancelledAppointments = counts.GetValueOrDefault(AppointmentStatus.Cancelled),
                CustomersCount = customers,
                Rows = rows.Select(ToRow).ToList()
            };
        }

        /// <summary>
        /// [start, end) for the range filter, in local time like the stored appointment times
        /// (server time until audit item B5 adds a business timezone).
        /// </summary>
        private (DateTime? Start, DateTime? End) GetRange(string? range)
        {
            var today = _timeProvider.GetLocalNow().DateTime.Date;

            switch (range)
            {
                case "today":
                    return (today, today.AddDays(1));
                case "week":
                    return (today, today.AddDays(7)); // next 7 days, starting today
                case "month":
                    var first = new DateTime(today.Year, today.Month, 1);
                    return (first, first.AddMonths(1));
                default:
                    return (null, null);
            }
        }

        private static DashboardAppointmentRowDTO ToRow(Appointment a) => new()
        {
            Id = a.Id,
            CustomerName = a.Customer != null ? $"{a.Customer.FirstName} {a.Customer.LastName}" : "—",
            Description = a.Description,
            OrderNumber = a.OrderNumber,
            AppointmentDate = a.AppointmentDate,
            Email = a.Customer?.Email ?? "",
            Phone1 = a.Customer?.PhoneNumber ?? "",
            Phone2 = a.Customer?.SecondPhoneNumber,
            Location = a.Customer?.State ?? "",
            Status = a.Status.ToString()
        };
    }
}
