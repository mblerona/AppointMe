using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Repository.Data;
using AppointMe.Repository.Interface;
using AppointMe.Service.Interface;
using Microsoft.EntityFrameworkCore;

namespace AppointMe.Service.Implementation
{
    public class AppointmentService : IAppointmentService
    {
        private readonly IAppointmentRepository _appointmentRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IServiceOfferingRepository _serviceOfferingRepository;
        private readonly IHolidayService _holidayService;
        private readonly ApplicationDbContext _db;

        public AppointmentService(
            IAppointmentRepository appointmentRepository,
            ICustomerRepository customerRepository,
            IServiceOfferingRepository serviceOfferingRepository,
            IHolidayService holidayService,
            ApplicationDbContext db)
        {
            _appointmentRepository = appointmentRepository;
            _customerRepository = customerRepository;
            _serviceOfferingRepository = serviceOfferingRepository;
            _holidayService = holidayService;
            _db = db;
        }

        public async Task<AppointmentDTO> GetAppointmentByIdAsync(Guid id, Guid tenantId)
        {
            var appointment = await _appointmentRepository.GetWithCustomerAsync(id, tenantId);
            if (appointment == null)
                throw new KeyNotFoundException($"Appointment with ID {id} not found");

            return MapToAppointmentDto(appointment);
        }

        public async Task<IEnumerable<AppointmentDTO>> GetAllAppointmentsAsync(Guid tenantId)
        {
            var appointments = await _appointmentRepository.GetAllByTenantAsync(tenantId);
            return appointments.Select(MapToAppointmentDto).ToList();
        }

        public async Task<IEnumerable<AppointmentDTO>> GetAppointmentsByCustomerAsync(Guid customerId, Guid tenantId)
        {
            var appointments = await _appointmentRepository.GetByCustomerIdAsync(customerId, tenantId);
            return appointments.Select(MapToAppointmentDto).ToList();
        }

        public async Task<IEnumerable<AppointmentDTO>> GetAppointmentsByDateRangeAsync(DateTime startDate, DateTime endDate, Guid tenantId)
        {
            var appointments = await _appointmentRepository.GetByDateRangeAsync(startDate, endDate, tenantId);
            return appointments.Select(MapToAppointmentDto).ToList();
        }

        public async Task<IEnumerable<AppointmentDTO>> GetAppointmentsByStatusAsync(AppointmentStatus status, Guid tenantId)
        {
            var appointments = await _appointmentRepository.GetByStatusAsync(status, tenantId);
            return appointments.Select(MapToAppointmentDto).ToList();
        }

        public async Task<AppointmentDTO> CreateAppointmentAsync(CreateAppointmentDTO dto, Guid tenantId)
        {
            var customer = await _customerRepository.GetByIdAsync(dto.CustomerId);
            if (customer == null || customer.TenantId != tenantId)
                throw new KeyNotFoundException($"Customer with ID {dto.CustomerId} not found");

            var business = await GetBusinessOrThrow(tenantId);
            await ValidateAgainstBusinessRulesAsync(dto.AppointmentDate, business);

            var isAvailable = await _appointmentRepository.IsTimeSlotAvailableAsync(dto.AppointmentDate, tenantId);
            if (!isAvailable)
                throw new InvalidOperationException("Time slot is not available");

            var businessId = tenantId;

            var selectedIds = (dto.ServiceOfferingIds ?? new List<Guid>())
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            var selectedServices = new List<ServiceOffering>();
            if (selectedIds.Count > 0)
            {
                selectedServices = (await _serviceOfferingRepository.GetByIdsForBusinessAsync(selectedIds, businessId)).ToList();
                if (selectedServices.Count != selectedIds.Count)
                    throw new InvalidOperationException("One or more selected services are invalid.");
            }

            var appointment = new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = dto.CustomerId,
                OrderNumber = dto.OrderNumber,
                AppointmentDate = dto.AppointmentDate,
                Description = dto.Description,
                Status = AppointmentStatus.Scheduled,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            foreach (var s in selectedServices)
            {
                appointment.AppointmentServices.Add(new AppointmentServiceModel
                {
                    AppointmentId = appointment.Id,
                    ServiceOfferingId = s.Id,
                    PriceAtBooking = s.Price
                });
            }

            await _appointmentRepository.AddAsync(appointment);
            await _appointmentRepository.SaveChangesAsync();

            return MapToAppointmentDto(appointment);
        }

        public async Task<AppointmentDTO> UpdateAppointmentAsync(Guid appointmentId, UpdateAppointmentDTO dto, Guid tenantId)
        {
            var appointment = await _appointmentRepository.GetWithCustomerAsync(appointmentId, tenantId);
            if (appointment == null)
                throw new KeyNotFoundException($"Appointment with ID {appointmentId} not found");

            if (dto.AppointmentDate != default && dto.AppointmentDate != appointment.AppointmentDate)
            {
                var business = await GetBusinessOrThrow(tenantId);
                await ValidateAgainstBusinessRulesAsync(dto.AppointmentDate, business);

                var isAvailable = await _appointmentRepository.IsTimeSlotAvailableAsync(dto.AppointmentDate, tenantId, appointmentId);
                if (!isAvailable)
                    throw new InvalidOperationException("Time slot is not available");

                appointment.AppointmentDate = dto.AppointmentDate;
            }

            if (!string.IsNullOrWhiteSpace(dto.Description))
                appointment.Description = dto.Description;

            appointment.Status = dto.Status;

            var businessId = tenantId;

            var selectedIds = (dto.ServiceOfferingIds ?? new List<Guid>())
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            var selectedServices = new List<ServiceOffering>();
            if (selectedIds.Count > 0)
            {
                selectedServices = (await _serviceOfferingRepository.GetByIdsForBusinessAsync(selectedIds, businessId)).ToList();
                if (selectedServices.Count != selectedIds.Count)
                    throw new InvalidOperationException("One or more selected services are invalid.");
            }

            appointment.AppointmentServices.Clear();
            foreach (var s in selectedServices)
            {
                appointment.AppointmentServices.Add(new AppointmentServiceModel
                {
                    AppointmentId = appointment.Id,
                    ServiceOfferingId = s.Id,
                    PriceAtBooking = s.Price
                });
            }

            appointment.UpdatedAt = DateTime.UtcNow;

            await _appointmentRepository.UpdateAsync(appointment);
            await _appointmentRepository.SaveChangesAsync();

            return MapToAppointmentDto(appointment);
        }

        public async Task DeleteAppointmentAsync(Guid appointmentId, Guid tenantId)
        {
            var appointment = await _appointmentRepository.GetByIdAsync(appointmentId, tenantId);
            if (appointment == null)
                throw new KeyNotFoundException($"Appointment with ID {appointmentId} not found");

            await _appointmentRepository.DeleteAsync(appointment);
            await _appointmentRepository.SaveChangesAsync();
        }

        public async Task SetStatusAsync(Guid appointmentId, AppointmentStatus status, Guid tenantId)
        {
            var appointment = await _appointmentRepository.GetByIdAsync(appointmentId, tenantId);
            if (appointment == null)
                throw new KeyNotFoundException("Appointment not found.");

            appointment.Status = status;
            appointment.UpdatedAt = DateTime.UtcNow;

            await _appointmentRepository.UpdateAsync(appointment);
            await _appointmentRepository.SaveChangesAsync();
        }

        // =========================
        // BUSINESS RULES + HOLIDAYS
        // =========================
        private async Task<Business> GetBusinessOrThrow(Guid tenantId)
        {
            var biz = await _db.Businesses.AsNoTracking().FirstOrDefaultAsync(b => b.Id == tenantId);
            if (biz == null) throw new InvalidOperationException("Business not found.");
            return biz;
        }

        private async Task ValidateAgainstBusinessRulesAsync(DateTime appointmentDate, Business biz)
        {
            // 1) must be future (use local time because datetime-local is local)
            if (appointmentDate <= DateTime.Now)
                throw new InvalidOperationException("Appointment date/time must be in the future.");

            // 2) must be on open day
            if (!IsOpenDay(appointmentDate.DayOfWeek, biz))
                throw new InvalidOperationException("Business is closed on the selected day.");

            // 3) must be within working hours
            var t = appointmentDate.TimeOfDay;
            if (t < biz.WorkDayStart || t >= biz.WorkDayEnd)
                throw new InvalidOperationException($"Appointment must be between {biz.WorkDayStart:hh\\:mm} and {biz.WorkDayEnd:hh\\:mm}.");

            // 4) must align to slot minutes
            var slot = biz.DefaultSlotMinutes <= 0 ? 30 : biz.DefaultSlotMinutes;
            var minutes = (int)t.TotalMinutes;
            if (minutes % slot != 0)
                throw new InvalidOperationException($"Appointment time must align to {slot}-minute slots.");

            // 5) must NOT be a public holiday (MK)
            var y = appointmentDate.Year;

          
            var holidays = await _holidayService.GetHolidaysAsync(y, "MK");

            var dateOnly = DateOnly.FromDateTime(appointmentDate);
            var holiday = holidays.FirstOrDefault(h => DateOnly.FromDateTime(h.Date) == dateOnly);

            if (holiday != null)
            {
                var name = !string.IsNullOrWhiteSpace(holiday.LocalName) ? holiday.LocalName : holiday.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    throw new InvalidOperationException($"Cannot create an appointment on a public holiday: {name}.");
                throw new InvalidOperationException("Cannot create an appointment on a public holiday.");
            }
        }

        private static bool IsOpenDay(DayOfWeek day, Business b)
        {
            return day switch
            {
                DayOfWeek.Monday => b.OpenMon,
                DayOfWeek.Tuesday => b.OpenTue,
                DayOfWeek.Wednesday => b.OpenWed,
                DayOfWeek.Thursday => b.OpenThu,
                DayOfWeek.Friday => b.OpenFri,
                DayOfWeek.Saturday => b.OpenSat,
                DayOfWeek.Sunday => b.OpenSun,
                _ => false
            };
        }

        private AppointmentDTO MapToAppointmentDto(Appointment appointment)
        {
            var services = appointment.AppointmentServices?
                .Select(x => new ServiceLineDto
                {
                    ServiceId = x.ServiceOfferingId,
                    Name = x.ServiceOffering?.Name ?? "",
                    Category = x.ServiceOffering?.Category?.Name,
                    PriceAtBooking = x.PriceAtBooking
                })
                .ToList() ?? new List<ServiceLineDto>();

            return new AppointmentDTO
            {
                Id = appointment.Id,
                CustomerId = appointment.CustomerId,
                CustomerName = appointment.Customer != null ? $"{appointment.Customer.FirstName} {appointment.Customer.LastName}" : "",
                OrderNumber = appointment.OrderNumber,
                AppointmentDate = appointment.AppointmentDate,
                Description = appointment.Description,
                Status = appointment.Status.ToString(),
                CreatedAt = appointment.CreatedAt,
                UpdatedAt = appointment.UpdatedAt,
                Services = services
            };
        }
    }
}
