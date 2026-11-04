using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Repository.Interface;
using AppointMe.Service.Calendar;
using AppointMe.Service.Email;
using AppointMe.Service.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AppointMe.Service.Implementation
{
    public class AppointmentService : IAppointmentService
    {
        private readonly IAppointmentRepository _appointmentRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IServiceOfferingRepository _serviceOfferingRepository;
        private readonly IHolidayService _holidayService;
        private readonly IBusinessRepository _businessRepository;
        private readonly IEmailService _emailService;
        private readonly ILogger<AppointmentService> _logger;

        public AppointmentService(
            IAppointmentRepository appointmentRepository,
            ICustomerRepository customerRepository,
            IServiceOfferingRepository serviceOfferingRepository,
            IHolidayService holidayService,
            IBusinessRepository businessRepository,
            IEmailService emailService,
            ILogger<AppointmentService> logger)
        {
            _appointmentRepository = appointmentRepository;
            _customerRepository = customerRepository;
            _serviceOfferingRepository = serviceOfferingRepository;
            _holidayService = holidayService;
            _businessRepository = businessRepository;
            _emailService = emailService;
            _logger = logger;
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

            // A new appointment takes the business's current slot length and keeps it from then on.
            var durationMinutes = business.DefaultSlotMinutes <= 0 ? 30 : business.DefaultSlotMinutes;

            // Validate: future, open day, working hours, holidays
            await ValidateAgainstBusinessRulesAsync(dto.AppointmentDate, durationMinutes, business);

            var isAvailable = await _appointmentRepository.IsTimeSlotAvailableAsync(dto.AppointmentDate, durationMinutes, tenantId);
            if (!isAvailable)
                throw new BusinessRuleException("The selected time overlaps with another appointment.");

            dto.OrderNumber = dto.OrderNumber?.Trim();
            if (await _appointmentRepository.OrderNumberExistsAsync(dto.OrderNumber, tenantId))
                throw new BusinessRuleException("Order number already exists. Please use a different order number.");

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
                    throw new BusinessRuleException("One or more selected services are invalid.");
            }

            var appointment = new Appointment
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                CustomerId = dto.CustomerId,
                OrderNumber = dto.OrderNumber,
                AppointmentDate = dto.AppointmentDate,
                DurationMinutes = durationMinutes,
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

            var result = MapToAppointmentDto(appointment);

            if (dto.NotifyByEmail && !string.IsNullOrWhiteSpace(customer.Email))
            {
                var msg = new EmailMessage
                {
                    MailTo = customer.Email,
                    Subject = $"{business.Name} appointment",
                    Content = BuildAppointmentEmailContent(customer, business, appointment)
                };

                try
                {
                    var endTime = appointment.AppointmentDate.AddMinutes(appointment.DurationMinutes);

                    // ICS attachment
                    var icsBytes = IcsCalendarBuilder.BuildAppointmentIcs(
                        appointment.Id,
                        business.Name,
                        business.Address,
                        appointment.AppointmentDate,
                        endTime,
                        $"{customer.FirstName} {customer.LastName}".Trim(),
                        appointment.OrderNumber,
                        appointment.Description
                    );

                    await _emailService.SendEmailWithAttachmentAsync(
                        msg,
                        icsBytes,
                        "appointment.ics",
                        "text/calendar"
                    );
                }
                catch (Exception ex)
                {
                    // The appointment is already saved; a failed notification must not undo it.
                    _logger.LogError(ex,
                        "Confirmation email for appointment {AppointmentId} (tenant {TenantId}) could not be sent.",
                        appointment.Id, tenantId);
                    result.ConfirmationEmailFailed = true;
                }
            }

            return result;
        }

        public async Task<AppointmentDTO> UpdateAppointmentAsync(Guid appointmentId, UpdateAppointmentDTO dto, Guid tenantId)
        {
            var appointment = await _appointmentRepository.GetWithCustomerAsync(appointmentId, tenantId);
            if (appointment == null)
                throw new KeyNotFoundException($"Appointment with ID {appointmentId} not found");

            var business = await GetBusinessOrThrow(tenantId);

            // The form works in whole minutes, so a stored date with seconds isn't "moved" just by being saved.
            if (dto.AppointmentDate != default && ToMinute(dto.AppointmentDate) != ToMinute(appointment.AppointmentDate))
            {
                await ValidateAgainstBusinessRulesAsync(dto.AppointmentDate, appointment.DurationMinutes, business);

                // A moved appointment keeps its own length, even if the slot length has changed since booking.
                var isAvailable = await _appointmentRepository.IsTimeSlotAvailableAsync(dto.AppointmentDate, appointment.DurationMinutes, tenantId, appointmentId);
                if (!isAvailable)
                    throw new BusinessRuleException("The selected time overlaps with another appointment.");

                appointment.AppointmentDate = dto.AppointmentDate;
            }
            else
            {
                await EnsureCanReactivateAsync(appointment, dto.Status, tenantId);
            }

            var newOrder = (dto.OrderNumber ?? "").Trim();
            if (string.IsNullOrWhiteSpace(newOrder))
                throw new BusinessRuleException("Order number is required.");

            if (!string.Equals(appointment.OrderNumber, newOrder, StringComparison.Ordinal))
            {
                var exists = await _appointmentRepository.OrderNumberExistsAsync(newOrder, tenantId, appointmentId);
                if (exists)
                    throw new BusinessRuleException("Order number already exists. Please use a different order number.");

                appointment.OrderNumber = newOrder;
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
                    throw new BusinessRuleException("One or more selected services are invalid.");
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

            await EnsureCanReactivateAsync(appointment, status, tenantId);

            appointment.Status = status;
            appointment.UpdatedAt = DateTime.UtcNow;

            await _appointmentRepository.UpdateAsync(appointment);
            await _appointmentRepository.SaveChangesAsync();
        }

        // =========================
        // EMAIL CONTENT HELPERS
        // =========================
        private static string BuildAppointmentEmailContent(Customer customer, Business business, Appointment appointment)
        {
            var customerName = $"{customer.FirstName} {customer.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(customerName))
                customerName = "there";

            var businessName = string.IsNullOrWhiteSpace(business.Name) ? "our business" : business.Name;

            return
$@"Greetings {customerName},

Your appointment at {businessName} was successfully created.
See you on {appointment.AppointmentDate:dddd, dd MMM yyyy} at {appointment.AppointmentDate:HH:mm}.

📎 Attached you will find a calendar file (appointment.ics) that you can open to add this appointment to your calendar
(Google Calendar, Outlook, Apple Calendar).

Thank you for choosing {businessName}.

Best regards,
{businessName}";
        }

        // =========================
        // BUSINESS RULES + HOLIDAYS
        // =========================
        private async Task<Business> GetBusinessOrThrow(Guid tenantId)
        {
            var biz = await _businessRepository.GetByIdReadOnlyAsync(tenantId);
            if (biz == null) throw new BusinessRuleException("Business not found.");
            return biz;
        }

        /// <summary>
        /// A cancelled appointment frees its time, so another one may have been booked there since.
        /// Any change away from Cancelled makes it occupy that time again, so it has to pass the overlap
        /// check, and it has to fit before closing time (the hours or the length may differ from when it was booked).
        /// </summary>
        private async Task EnsureCanReactivateAsync(Appointment appointment, AppointmentStatus newStatus, Guid tenantId)
        {
            if (appointment.Status != AppointmentStatus.Cancelled || newStatus == AppointmentStatus.Cancelled)
                return;

            var business = await GetBusinessOrThrow(tenantId);
            EnsureEndsByClosingTime(appointment.AppointmentDate, appointment.DurationMinutes, business);

            var isAvailable = await _appointmentRepository.IsTimeSlotAvailableAsync(
                appointment.AppointmentDate, appointment.DurationMinutes, tenantId, appointment.Id);
            if (!isAvailable)
                throw new BusinessRuleException(
                    "This appointment can't be reactivated because its time now overlaps with another appointment. Move it to a free time first.");
        }

        private async Task ValidateAgainstBusinessRulesAsync(DateTime appointmentDate, int durationMinutes, Business biz)
        {
            // 1) must be future
            if (appointmentDate <= DateTime.Now)
                throw new BusinessRuleException("Appointment date/time must be in the future.");

            // 2) must be on open day
            if (!IsOpenDay(appointmentDate.DayOfWeek, biz))
                throw new BusinessRuleException("Business is closed on the selected day.");

            // 3) must be within working hours
            var t = appointmentDate.TimeOfDay;
            if (t < biz.WorkDayStart || t >= biz.WorkDayEnd)
                throw new BusinessRuleException($"Appointment must be between {biz.WorkDayStart:hh\\:mm} and {biz.WorkDayEnd:hh\\:mm}.");

            EnsureEndsByClosingTime(appointmentDate, durationMinutes, biz);

            // 4) must NOT be a public holiday (MK)
            var y = appointmentDate.Year;
            var holidays = await _holidayService.GetHolidaysAsync(y, "MK");

            var dateOnly = DateOnly.FromDateTime(appointmentDate);
            var holiday = holidays.FirstOrDefault(h => DateOnly.FromDateTime(h.Date) == dateOnly);

            if (holiday != null)
            {
                var name = !string.IsNullOrWhiteSpace(holiday.LocalName) ? holiday.LocalName : holiday.Name;
                if (!string.IsNullOrWhiteSpace(name))
                    throw new BusinessRuleException($"Cannot create an appointment on a public holiday: {name}.");
                throw new BusinessRuleException("Cannot create an appointment on a public holiday.");
            }
        }

        private static DateTime ToMinute(DateTime value) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMinute));

        /// <summary>TimeSpan doesn't wrap at midnight, so a late start can't slip through.</summary>
        private static void EnsureEndsByClosingTime(DateTime start, int durationMinutes, Business biz)
        {
            var end = start.TimeOfDay.Add(TimeSpan.FromMinutes(durationMinutes));
            if (end > biz.WorkDayEnd)
                throw new BusinessRuleException(
                    $"This appointment takes {durationMinutes} minutes and would end after closing time ({biz.WorkDayEnd:hh\\:mm}). Please choose an earlier time.");
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
                DurationMinutes = appointment.DurationMinutes,
                Description = appointment.Description,
                Status = appointment.Status.ToString(),
                CreatedAt = appointment.CreatedAt,
                UpdatedAt = appointment.UpdatedAt,
                Services = services
            };
        }
    }
}




