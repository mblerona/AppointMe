using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Repository.Interface;
using AppointMe.Service.Implementation;
using FluentAssertions;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services
{
    public class CustomerServiceTests
    {
        private readonly Mock<ICustomerRepository> _customerRepo = new();
        private readonly Mock<IAppointmentRepository> _appointmentRepo = new(); // ctor requires it, even though not used
        private readonly CustomerService _sut;

        public CustomerServiceTests()
        {
            _sut = new CustomerService(_customerRepo.Object, _appointmentRepo.Object);
        }

        [Fact]
        public async Task GetCustomerByIdAsync_WhenCustomerMissing_ShouldThrow()
        {
            // arrange
            var tenantId = Guid.NewGuid();
            var id = Guid.NewGuid();

            _customerRepo.Setup(r => r.GetByIdAsync(id))
                .ReturnsAsync((Customer)null!);

            // act
            Func<Task> act = () => _sut.GetCustomerByIdAsync(id, tenantId);

            // assert
            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage($"Customer with ID {id} not found");
        }

        [Fact]
        public async Task GetCustomerByIdAsync_WhenTenantMismatch_ShouldThrow()
        {
            // arrange
            var tenantId = Guid.NewGuid();
            var id = Guid.NewGuid();

            var customer = BuildCustomer(id, tenantId: Guid.NewGuid()); // mismatch

            _customerRepo.Setup(r => r.GetByIdAsync(id))
                .ReturnsAsync(customer);

            // act
            Func<Task> act = () => _sut.GetCustomerByIdAsync(id, tenantId);

            // assert
            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage($"Customer with ID {id} not found");
        }

        [Fact]
        public async Task GetCustomerByIdAsync_WhenOk_ShouldMapToDto()
        {
            // arrange
            var tenantId = Guid.NewGuid();
            var id = Guid.NewGuid();

            var customer = BuildCustomer(id, tenantId, number: 12, first: "Ana", last: "Doe", email: "ana@test.com");

            _customerRepo.Setup(r => r.GetByIdAsync(id))
                .ReturnsAsync(customer);

            // act
            var dto = await _sut.GetCustomerByIdAsync(id, tenantId);

            // assert
            dto.Id.Should().Be(id);
            dto.CustomerNumber.Should().Be(12);
            dto.FirstName.Should().Be("Ana");
            dto.LastName.Should().Be("Doe");
            dto.Email.Should().Be("ana@test.com");
        }

        [Fact]
        public async Task GetAllCustomersAsync_ShouldReturnMappedDtos()
        {
            // arrange
            var tenantId = Guid.NewGuid();
            var customers = new List<Customer>
            {
                BuildCustomer(Guid.NewGuid(), tenantId, number: 1, first:"A"),
                BuildCustomer(Guid.NewGuid(), tenantId, number: 2, first:"B"),
            };

            _customerRepo.Setup(r => r.GetAllByTenantAsync(tenantId))
                .ReturnsAsync(customers);

            // act
            var result = (await _sut.GetAllCustomersAsync(tenantId)).ToList();

            // assert
            result.Should().HaveCount(2);
            result.Select(x => x.CustomerNumber).Should().BeEquivalentTo(new[] { 1, 2 });
        }

        [Fact]
        public async Task SearchCustomersAsync_ShouldCallRepoAndMap()
        {
            // arrange
            var tenantId = Guid.NewGuid();
            var customers = new List<Customer>
            {
                BuildCustomer(Guid.NewGuid(), tenantId, number: 5, first:"John", last:"Smith"),
            };

            _customerRepo.Setup(r => r.SearchAsync("john", tenantId))
                .ReturnsAsync(customers);

            // act
            var result = (await _sut.SearchCustomersAsync("john", tenantId)).ToList();

            // assert
            result.Should().HaveCount(1);
            result[0].FirstName.Should().Be("John");

            _customerRepo.Verify(r => r.SearchAsync("john", tenantId), Times.Once);
        }

        [Fact]
        public async Task CreateCustomerAsync_WhenEmailAlreadyExists_ShouldThrow()
        {
            // arrange
            var tenantId = Guid.NewGuid();

            var dto = new CreateCustomerDTO
            {
                FirstName = "A",
                LastName = "B",
                Email = "dup@test.com",
                PhoneNumber = "111",
                State = "MK",
                City = "Skopje"
            };

            _customerRepo.Setup(r => r.GetByEmailAsync(dto.Email, tenantId))
                .ReturnsAsync(BuildCustomer(Guid.NewGuid(), tenantId, email: dto.Email));

            // act
            Func<Task> act = () => _sut.CreateCustomerAsync(dto, tenantId);

            // assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage($"Customer with email {dto.Email} already exists");
        }

        [Fact]
        public async Task CreateCustomerAsync_WhenOk_ShouldAssignNextCustomerNumber_AndSave()
        {
            // arrange
            var tenantId = Guid.NewGuid();
            var dto = new CreateCustomerDTO
            {
                FirstName = "New",
                LastName = "Customer",
                Email = "new@test.com",
                PhoneNumber = "222",
                SecondPhoneNumber = "333",
                State = "MK",
                City = "Skopje",
                Notes = "note"
            };

            _customerRepo.Setup(r => r.GetByEmailAsync(dto.Email, tenantId))
                .ReturnsAsync((Customer)null!);

            _customerRepo.Setup(r => r.GetMaxCustomerNumberAsync(tenantId))
                .ReturnsAsync(10);

            Customer? added = null;
            _customerRepo.Setup(r => r.AddAsync(It.IsAny<Customer>()))
                .Callback<Customer>(c => added = c)
                .Returns(Task.CompletedTask);

            _customerRepo.Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            // act
            var created = await _sut.CreateCustomerAsync(dto, tenantId);

            // assert
            added.Should().NotBeNull();
            added!.TenantId.Should().Be(tenantId);
            added.CustomerNumber.Should().Be(11); // max+1
            added.Email.Should().Be("new@test.com");

            created.CustomerNumber.Should().Be(11);

            _customerRepo.Verify(r => r.AddAsync(It.IsAny<Customer>()), Times.Once);
            _customerRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task UpdateCustomerAsync_WhenMissingOrTenantMismatch_ShouldThrow()
        {
            // arrange
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();

            _customerRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync((Customer)null!);

            var dto = new UpdateCustomerDTO
            {
                FirstName = "X",
                LastName = "Y",
                Email = "x@test.com",
                PhoneNumber = "111",
                State = "MK",
                City = "Sk"
            };

            // act
            Func<Task> act = () => _sut.UpdateCustomerAsync(customerId, dto, tenantId);

            // assert
            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage($"Customer with ID {customerId} not found");
        }

        [Fact]
        public async Task UpdateCustomerAsync_WhenEmailChangedToExisting_ShouldThrow()
        {
            // arrange
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();

            var customer = BuildCustomer(customerId, tenantId, email: "old@test.com");
            _customerRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(customer);

            var dto = new UpdateCustomerDTO
            {
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                Email = "taken@test.com", // change
                PhoneNumber = customer.PhoneNumber,
                State = customer.State,
                City = customer.City
            };

            _customerRepo.Setup(r => r.GetByEmailAsync(dto.Email, tenantId))
                .ReturnsAsync(BuildCustomer(Guid.NewGuid(), tenantId, email: dto.Email));

            // act
            Func<Task> act = () => _sut.UpdateCustomerAsync(customerId, dto, tenantId);

            // assert
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage($"Customer with email {dto.Email} already exists");
        }

        [Fact]
        public async Task UpdateCustomerAsync_WhenOk_ShouldUpdateFields_AndSave()
        {
            // arrange
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();

            var customer = BuildCustomer(customerId, tenantId, email: "old@test.com", first: "Old");
            _customerRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(customer);

            _customerRepo.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), tenantId))
                .ReturnsAsync((Customer)null!);

            _customerRepo.Setup(r => r.UpdateAsync(It.IsAny<Customer>()))
                .Returns(Task.CompletedTask);

            _customerRepo.Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            var dto = new UpdateCustomerDTO
            {
                FirstName = "NewFirst",
                LastName = customer.LastName,
                Email = "new@test.com",
                PhoneNumber = "999",
                SecondPhoneNumber = "888",
                State = "MK",
                City = "Skopje",
                Notes = "updated"
            };

            var oldUpdatedAt = customer.UpdatedAt;

            // act
            var updated = await _sut.UpdateCustomerAsync(customerId, dto, tenantId);

            // assert
            customer.FirstName.Should().Be("NewFirst");
            customer.Email.Should().Be("new@test.com");
            customer.PhoneNumber.Should().Be("999");
            customer.SecondPhoneNumber.Should().Be("888");
            customer.Notes.Should().Be("updated");
            customer.UpdatedAt.Should().NotBe(oldUpdatedAt);

            updated.FirstName.Should().Be("NewFirst");

            _customerRepo.Verify(r => r.UpdateAsync(customer), Times.Once);
            _customerRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task DeleteCustomerAsync_WhenOk_ShouldDelete_AndSave()
        {
            // arrange
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();

            var customer = BuildCustomer(customerId, tenantId);

            _customerRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(customer);

            _customerRepo.Setup(r => r.DeleteAsync(customer))
                .Returns(Task.CompletedTask);

            _customerRepo.Setup(r => r.SaveChangesAsync())
                .Returns(Task.CompletedTask);

            // act
            await _sut.DeleteCustomerAsync(customerId, tenantId);

            // assert
            _customerRepo.Verify(r => r.DeleteAsync(customer), Times.Once);
            _customerRepo.Verify(r => r.SaveChangesAsync(), Times.Once);
        }

        [Fact]
        public async Task DeleteCustomerAsync_WhenTenantMismatch_ShouldThrow()
        {
            // arrange
            var tenantId = Guid.NewGuid();
            var customerId = Guid.NewGuid();

            var customer = BuildCustomer(customerId, tenantId: Guid.NewGuid()); // mismatch
            _customerRepo.Setup(r => r.GetByIdAsync(customerId))
                .ReturnsAsync(customer);

            // act
            Func<Task> act = () => _sut.DeleteCustomerAsync(customerId, tenantId);

            // assert
            await act.Should().ThrowAsync<KeyNotFoundException>()
                .WithMessage($"Customer with ID {customerId} not found");
        }

        private static Customer BuildCustomer(
            Guid id,
            Guid tenantId,
            int number = 1,
            string first = "F",
            string last = "L",
            string email = "a@b.com")
        {
            return new Customer
            {
                Id = id,
                TenantId = tenantId,
                CustomerNumber = number,
                FirstName = first,
                LastName = last,
                Email = email,
                PhoneNumber = "123",
                SecondPhoneNumber = "456",
                State = "MK",
                City = "Skopje",
                Notes = "n",
                CreatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow.AddDays(-1),
                Appointments = new List<Appointment>()
            };
        }
    }
}
