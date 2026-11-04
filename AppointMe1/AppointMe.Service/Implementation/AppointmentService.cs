using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.TimeZones;
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
        private const string InvalidServicesMessage = "One or more selected services are invalid.";

        private readonly IAppointmentRepository _appointmentRepository;
        private readonly ICustomerRepository _customerRepository;
        private readonly IServiceOfferingRepository _serviceOfferingRepository;
        private readonly IHolidayService _holidayService;
        private readonly IBusinessRepository _businessRepository;
        private readonly IEmailService _emailService;
        private readonly ILogger<AppointmentService> _logger;
        private readonly TimeProvider _timeProvider;

        public AppointmentService(
            IAppointmentRepository appointmentRepository,
            ICustomerRepository customerRepository,
            IServiceOfferingRepository serviceOfferingRepository,
            IHolidayService holidayService,
            IBusinessRepository businessRepository,
            IEmailService emailService,
            ILogger<AppointmentService> logger,
            TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
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

            // With services disabled the booking has none, whatever was posted.
            var selectedServices = business.EnableServices
                ? await LoadSelectedServicesAsync(dto.ServiceOfferingIds, tenantId)
                : new List<ServiceOffering>();

            if (selectedServices.Any(s => !s.IsActive))
                throw new BusinessRuleException(InvalidServicesMessage);

            // Calculated once here and stored, like the price at booking.
            var durationMinutes = AppointmentDurationCalculator.Calculate(business.DefaultSlotMinutes, selectedServices);

            // Validate: future, open day, working hours, holidays
            await ValidateAgainstBusinessRulesAsync(dto.AppointmentDate, durationMinutes, business);

            var isAvailable = await _appointmentRepository.IsTimeSlotAvailableAsync(dto.AppointmentDate, durationMinutes, tenantId);
            if (!isAvailable)
                throw new BusinessRuleException("The selected time overlaps with another appointment.");

            dto.OrderNumber = dto.OrderNumber?.Trim();
            if (await _appointmentRepository.OrderNumberExistsAsync(dto.OrderNumber, tenantId))
                throw new BusinessRuleException("Order number already exists. Please use a different order number.");

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
                    // In UTC, so the customer's calendar shows the right moment wherever they are.
                    var startUtc = BusinessTimeZones.ToUtc(appointment.AppointmentDate, business.TimeZoneId);
                    var endUtc = BusinessTimeZones.ToUtc(appointment.AppointmentDate.AddMinutes(appointment.DurationMinutes), business.TimeZoneId);

                    // ICS attachment
                    var icsBytes = IcsCalendarBuilder.BuildAppointmentIcs(
                        appointment.Id,
                        business.Name,
                        business.Address,
                        startUtc,
                        endUtc,
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

            // ---- Everything is checked first, so a refused edit changes nothing. ----

            var newOrder = (dto.OrderNumber ?? "").Trim();
            if (string.IsNullOrWhiteSpace(newOrder))
                throw new BusinessRuleException("Order number is required.");

            if (!string.Equals(appointment.OrderNumber, newOrder, StringComparison.Ordinal)
                && await _appointmentRepository.OrderNumberExistsAsync(newOrder, tenantId, appointmentId))
                throw new BusinessRuleException("Order number already exists. Please use a different order number.");

            // With services disabled the form has no checkboxes, so an empty list doesn't mean "remove them".
            var attachedIds = appointment.AppointmentServices.Select(x => x.ServiceOfferingId).ToHashSet();
            var selectedServices = business.EnableServices
                ? await LoadSelectedServicesAsync(dto.ServiceOfferingIds, tenantId)
                : new List<ServiceOffering>();
            var servicesChanged = business.EnableServices && !attachedIds.SetEquals(selectedServices.Select(s => s.Id));

            // An inactive service can stay on an appointment that already has it, but can't be added.
            if (selectedServices.Any(s => !s.IsActive && !attachedIds.Contains(s.Id)))
                throw new BusinessRuleException(InvalidServicesMessage);

            // The stored duration is a snapshot: it is only recalculated when the services actually change.
            var durationMinutes = servicesChanged
                ? AppointmentDurationCalculator.Calculate(business.DefaultSlotMinutes, selectedServices)
                : appointment.DurationMinutes;

            // The form works in whole minutes, so a stored date with seconds isn't "moved" just by being saved.
            var moved = dto.AppointmentDate != default && ToMinute(dto.AppointmentDate) != ToMinute(appointment.AppointmentDate);
            if (moved)
            {
                await ValidateAgainstBusinessRulesAsync(dto.AppointmentDate, durationMinutes, business);

                var isAvailable = await _appointmentRepository.IsTimeSlotAvailableAsync(dto.AppointmentDate, durationMinutes, tenantId, appointmentId);
                if (!isAvailable)
                    throw new BusinessRuleException("The selected time overlaps with another appointment.");
            }
            else if (appointment.Status == AppointmentStatus.Cancelled && dto.Status != AppointmentStatus.Cancelled)
            {
                await EnsureCanReactivateAsync(appointment, durationMinutes, business);
            }
            else if (durationMinutes > appointment.DurationMinutes && dto.Status != AppointmentStatus.Cancelled)
            {
                // Only a longer appointment can create a new conflict. Shorter ones skip this, so that an older
                // booking that already overlaps or runs past closing can still be shortened. A cancelled one
                // takes up no time, and gets checked if it is reactivated.
                EnsureEndsByClosingTime(appointment.AppointmentDate, durationMinutes, business);

                var isAvailable = await _appointmentRepository.IsTimeSlotAvailableAsync(appointment.AppointmentDate, durationMinutes, tenantId, appointmentId);
                if (!isAvailable)
                    throw new BusinessRuleException($"The selected services make this appointment {durationMinutes} minutes long, which overlaps with another appointment.");
            }

            // ---- All checks passed. ----

            if (moved)
                appointment.AppointmentDate = dto.AppointmentDate;

            appointment.OrderNumber = newOrder;

            if (!string.IsNullOrWhiteSpace(dto.Description))
                appointment.Description = dto.Description;

            appointment.Status = dto.Status;
            appointment.DurationMinutes = durationMinutes;

            if (servicesChanged)
            {
                // Services that stay keep their price at booking; only newly added ones get today's price.
                var selectedIds = selectedServices.Select(s => s.Id).ToHashSet();
                foreach (var removed in appointment.AppointmentServices.Where(x => !selectedIds.Contains(x.ServiceOfferingId)).ToList())
                    appointment.AppointmentServices.Remove(removed);

                foreach (var added in selectedServices.Where(s => !attachedIds.Contains(s.Id)))
                {
                    appointment.AppointmentServices.Add(new AppointmentServiceModel
                    {
                        AppointmentId = appointment.Id,
                        ServiceOfferingId = added.Id,
                        PriceAtBooking = added.Price
                    });
                }
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

            if (appointment.Status == AppointmentStatus.Cancelled && status != AppointmentStatus.Cancelled)
                await EnsureCanReactivateAsync(appointment, appointment.DurationMinutes, await GetBusinessOrThrow(tenantId));

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
        private async Task EnsureCanReactivateAsync(Appointment appointment, int durationMinutes, Business business)
        {
            EnsureEndsByClosingTime(appointment.AppointmentDate, durationMinutes, business);

            var isAvailable = await _appointmentRepository.IsTimeSlotAvailableAsync(
                appointment.AppointmentDate, durationMinutes, appointment.TenantId, appointment.Id);
            if (!isAvailable)
                throw new BusinessRuleException(
                    "This appointment can't be reactivated because its time now overlaps with another appointment. Move it to a free time first.");
        }

        private async Task ValidateAgainstBusinessRulesAsync(DateTime appointmentDate, int durationMinutes, Business biz)
        {
            // 1) must be future
            if (appointmentDate <= BusinessTimeZones.Now(_timeProvider, biz.TimeZoneId))
                throw new BusinessRuleException("Appointment date/time must be in the future.");

            // 2) must be on open day
            if (!IsOpenDay(appointmentDate.DayOfWeek, biz))
                throw new BusinessRuleException("Business is closed on the selected day.");

            // 3) must be within working hours
            var t = appointmentDate.TimeOfDay;
            if (t < biz.WorkDayStart || t >= biz.WorkDayEnd)
                throw new BusinessRuleException($"Appointment must be between {biz.WorkDayStart:hh\\:mm} and {biz.WorkDayEnd:hh\\:mm}.");

            EnsureStartsOnSlotInterval(appointmentDate, biz);
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

        /// <summary>The tenant's services with these ids. An unknown id or another tenant's service is refused.</summary>
        private async Task<List<ServiceOffering>> LoadSelectedServicesAsync(IEnumerable<Guid>? ids, Guid tenantId)
        {
            var selectedIds = (ids ?? Enumerable.Empty<Guid>())
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

            if (selectedIds.Count == 0)
                return new List<ServiceOffering>();

            var services = (await _serviceOfferingRepository.GetByIdsForBusinessAsync(selectedIds, tenantId)).ToList();
            if (services.Count != selectedIds.Count)
                throw new BusinessRuleException(InvalidServicesMessage);

            return services;
        }

        private static DateTime ToMinute(DateTime value) => value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMinute));

        /// <summary>
        /// New and moved appointments start on the interval, counted from opening time. Only called for those,
        /// so an older booking that isn't on the interval can still be edited as long as its date stays.
        /// </summary>
        private static void EnsureStartsOnSlotInterval(DateTime start, Business biz)
        {
            var slot = TimeSpan.FromMinutes(AppointmentDurationCalculator.SlotLength(biz.DefaultSlotMinutes));
            var sinceOpening = start.TimeOfDay - biz.WorkDayStart;
            if (sinceOpening.Ticks % slot.Ticks == 0)
                return;

            var earlier = biz.WorkDayStart + TimeSpan.FromTicks(sinceOpening.Ticks / slot.Ticks * slot.Ticks);
            var later = earlier + slot;
            var suggestion = later < biz.WorkDayEnd
                ? $"Try {earlier:hh\\:mm} or {later:hh\\:mm}."
                : $"Try {earlier:hh\\:mm}.";

            throw new BusinessRuleException(
                $"Appointments start every {slot.TotalMinutes:0} minutes from opening time ({biz.WorkDayStart:hh\\:mm}). {suggestion}");
        }

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




