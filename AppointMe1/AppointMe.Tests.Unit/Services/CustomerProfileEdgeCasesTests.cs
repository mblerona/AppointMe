using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Interface;
using AppointMe.Service.Implementation;
using FluentAssertions;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services
{
    public class CustomerProfileEdgeCasesTests
    {
        private readonly Mock<ICustomerRepository> _customerRepo = new();
        private readonly Mock<IAppointmentRepository> _appointmentRepo = new();
        private readonly CustomerService _sut;

        public CustomerProfileEdgeCasesTests()
        {
            _sut = new CustomerService(_customerRepo.Object, _appointmentRepo.Object);
        }

        [Fact]
        public async Task GetCustomerProfileAsync_WhenMissing_ShouldThrow()
        {
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();

            _customerRepo.Setup(r => r.GetWithAppointmentsAsync(customerId, tenantId))
                .ReturnsAsync((Customer)null!);

            Func<Task> act = () => _sut.GetCustomerProfileAsync(customerId, tenantId);

            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage($"Customer with ID {customerId} not found");
        }

        [Fact]
        public async Task GetCustomerProfileAsync_ShouldComputeCounts_AndOrderAppointmentsDesc()
        {
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();

            var a1 = BuildAppointment(DateTime.UtcNow.AddDays(3), AppointmentStatus.Scheduled);
            var a2 = BuildAppointment(DateTime.UtcNow.AddDays(1), AppointmentStatus.Completed);
            var a3 = BuildAppointment(DateTime.UtcNow.AddDays(2), AppointmentStatus.Cancelled);

            var customer = new Customer
            {
                Id = customerId,
                TenantId = tenantId,
                CustomerNumber = 7,
                FirstName = "Ana",
                LastName = "Doe",
                Email = "ana@test.com",
                PhoneNumber = "123",
                SecondPhoneNumber = "456",
                State = "MK",
                City = "Skopje",
                Notes = "n",
                CreatedAt = DateTime.UtcNow.AddDays(-10),
                UpdatedAt = DateTime.UtcNow.AddDays(-2),
                Appointments = new List<Appointment> { a1, a2, a3 }
            };

            _customerRepo.Setup(r => r.GetWithAppointmentsAsync(customerId, tenantId))
                .ReturnsAsync(customer);

            var profile = await _sut.GetCustomerProfileAsync(customerId, tenantId);

            profile.TotalAppointments.Should().Be(3);
            profile.CompletedAppointments.Should().Be(1);
            profile.ScheduledAppointments.Should().Be(1);
            profile.CancelledAppointments.Should().Be(1);

            // ordered by AppointmentDate desc in service
            profile.Appointments.Select(x => x.AppointmentDate)
                .Should().BeInDescendingOrder();
        }

        private static Appointment BuildAppointment(DateTime when, AppointmentStatus status)
        {
            return new Appointment
            {
                Id = Guid.NewGuid(),
                AppointmentDate = when,
                Status = status,
                OrderNumber = "ORD",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                AppointmentServices = new List<AppointmentServiceModel>()
            };
        }
    }
}
