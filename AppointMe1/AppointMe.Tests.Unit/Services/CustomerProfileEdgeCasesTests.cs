using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Interface;
using AppointMe.Service.Implementation;
using AwesomeAssertions;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services
{
    public class CustomerProfileEdgeCasesTests
    {
        private readonly Mock<ICustomerRepository> _customerRepo = new();
        private readonly CustomerService _sut;

        public CustomerProfileEdgeCasesTests()
        {
            _sut = new CustomerService(_customerRepo.Object, new Mock<IBusinessRepository>().Object);
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

        private Customer SetUpCustomerWithAppointments(Guid tenantId, Guid customerId, int count)
        {
            var start = new DateTime(2026, 1, 1, 9, 0, 0);
            var customer = new Customer
            {
                Id = customerId,
                TenantId = tenantId,
                FirstName = "Ana",
                LastName = "Doe",
                Email = "ana@test.com",
                PhoneNumber = "123",
                // Appointment i is i days after the start, so the newest is the last one built.
                Appointments = Enumerable.Range(0, count)
                    .Select(i => BuildAppointment(start.AddDays(i), i % 2 == 0 ? AppointmentStatus.Scheduled : AppointmentStatus.Completed))
                    .ToList()
            };

            _customerRepo.Setup(r => r.GetWithAppointmentsAsync(customerId, tenantId)).ReturnsAsync(customer);
            return customer;
        }

        [Fact]
        public async Task GetCustomerProfileAsync_ShowsOnePageOf15_NewestFirst_ButCountsEverything()
        {
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            SetUpCustomerWithAppointments(tenantId, customerId, 40);

            var first = await _sut.GetCustomerProfileAsync(customerId, tenantId);

            CustomerService.AppointmentsPageSize.Should().Be(15);
            first.Appointments.Should().HaveCount(15);
            first.AppointmentsPage.Should().Be(1);
            first.AppointmentsTotalPages.Should().Be(3);
            first.Appointments[0].AppointmentDate.Should().Be(new DateTime(2026, 1, 1, 9, 0, 0).AddDays(39));
            first.TotalAppointments.Should().Be(40);
            first.ScheduledAppointments.Should().Be(20);
            first.CompletedAppointments.Should().Be(20);

            var last = await _sut.GetCustomerProfileAsync(customerId, tenantId, page: 3);

            last.Appointments.Should().HaveCount(10);
            last.Appointments[^1].AppointmentDate.Should().Be(new DateTime(2026, 1, 1, 9, 0, 0), "the oldest is last");
        }

        [Fact]
        public async Task GetCustomerProfileAsync_PagesDontOverlap_AndTogetherHoldEveryAppointment()
        {
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var customer = SetUpCustomerWithAppointments(tenantId, customerId, 31);

            var ids = new List<Guid>();
            for (var page = 1; page <= 3; page++)
                ids.AddRange((await _sut.GetCustomerProfileAsync(customerId, tenantId, page)).Appointments.Select(a => a.Id));

            ids.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(customer.Appointments.Select(a => a.Id));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-5, 1)]
        [InlineData(99, 2)]
        public async Task GetCustomerProfileAsync_OutOfRangePage_MovesToTheNearestPage(int requested, int expected)
        {
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            SetUpCustomerWithAppointments(tenantId, customerId, 20);

            var profile = await _sut.GetCustomerProfileAsync(customerId, tenantId, requested);

            profile.AppointmentsPage.Should().Be(expected);
            profile.Appointments.Should().NotBeEmpty();
        }

        [Fact]
        public async Task GetCustomerProfileAsync_WithoutAppointments_IsOneEmptyPage()
        {
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            SetUpCustomerWithAppointments(tenantId, customerId, 0);

            var profile = await _sut.GetCustomerProfileAsync(customerId, tenantId, page: 3);

            profile.AppointmentsPage.Should().Be(1);
            profile.AppointmentsTotalPages.Should().Be(1);
            profile.Appointments.Should().BeEmpty();
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
