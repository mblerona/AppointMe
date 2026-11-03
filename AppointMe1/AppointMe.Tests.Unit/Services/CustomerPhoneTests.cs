using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Validation;
using AppointMe.Repository.Interface;
using AppointMe.Service.Implementation;
using FluentAssertions;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

/// <summary>Phone validation and normalization in CustomerService (callers that skip form validation included).</summary>
public class CustomerPhoneTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private readonly Mock<ICustomerRepository> _repo = new();
    private readonly CustomerService _sut;

    public CustomerPhoneTests()
    {
        _repo.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), Tenant)).ReturnsAsync((Customer)null!);
        _repo.Setup(r => r.GetMaxCustomerNumberAsync(Tenant)).ReturnsAsync(0);
        _repo.Setup(r => r.SaveChangesAsync()).Returns(Task.CompletedTask);
        _sut = new CustomerService(_repo.Object);
    }

    private static CreateCustomerDTO NewCustomer(string phone, string? second = null) => new()
    {
        FirstName = "Ana",
        LastName = "Doe",
        Email = $"{Guid.NewGuid():N}@test.com",
        PhoneNumber = phone,
        SecondPhoneNumber = second,
        State = "MK",
        City = "Skopje"
    };

    private Customer Stored(string phone = "+38970123456", string? second = "+38971000111")
    {
        var c = new Customer
        {
            Id = Guid.NewGuid(), TenantId = Tenant, FirstName = "Ana", LastName = "Doe", Email = "ana@test.com",
            PhoneNumber = phone, SecondPhoneNumber = second, State = "MK", City = "Skopje"
        };
        _repo.Setup(r => r.GetByIdAsync(c.Id)).ReturnsAsync(c);
        return c;
    }

    [Fact]
    public async Task Create_StoresBothNumbersInE164()
    {
        Customer? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<Customer>())).Callback<Customer>(c => added = c).Returns(Task.CompletedTask);

        await _sut.CreateCustomerAsync(NewCustomer("070 123 456", "0049 30 1234567"), Tenant);

        added!.PhoneNumber.Should().Be("+38970123456");
        added.SecondPhoneNumber.Should().Be("+49301234567");
    }

    [Fact]
    public async Task Create_EmptySecondNumber_IsStoredAsNull()
    {
        Customer? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<Customer>())).Callback<Customer>(c => added = c).Returns(Task.CompletedTask);

        await _sut.CreateCustomerAsync(NewCustomer("070 123 456", "  "), Tenant);

        added!.SecondPhoneNumber.Should().BeNull();
    }

    [Theory]
    [InlineData("123", null, nameof(CreateCustomerDTO.PhoneNumber))]
    [InlineData("", null, nameof(CreateCustomerDTO.PhoneNumber))]
    [InlineData("070 123 456", "12", nameof(CreateCustomerDTO.SecondPhoneNumber))]
    public async Task Create_InvalidNumber_ThrowsForTheField_AndSavesNothing(string phone, string? second, string field)
    {
        var act = () => _sut.CreateCustomerAsync(NewCustomer(phone, second), Tenant);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be(field);
        _repo.Verify(r => r.AddAsync(It.IsAny<Customer>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task Update_NormalizesTheNewNumbers()
    {
        var c = Stored();

        await _sut.UpdateCustomerAsync(c.Id, new UpdateCustomerDTO { PhoneNumber = "075 555 444", SecondPhoneNumber = "+44 20 7946 0958" }, Tenant);

        c.PhoneNumber.Should().Be("+38975555444");
        c.SecondPhoneNumber.Should().Be("+442079460958");
    }

    [Fact]
    public async Task Update_NullNumbers_KeepTheStoredOnes()
    {
        var c = Stored();

        await _sut.UpdateCustomerAsync(c.Id, new UpdateCustomerDTO { FirstName = "Maja" }, Tenant);

        c.PhoneNumber.Should().Be("+38970123456");
        c.SecondPhoneNumber.Should().Be("+38971000111");
    }

    [Fact]
    public async Task Update_InvalidNumber_Throws_AndChangesNothing()
    {
        var c = Stored();

        var act = () => _sut.UpdateCustomerAsync(c.Id, new UpdateCustomerDTO { FirstName = "Maja", PhoneNumber = "12" }, Tenant);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be(nameof(UpdateCustomerDTO.PhoneNumber));
        c.FirstName.Should().Be("Ana", "the update is validated before anything changes");
        _repo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task DuplicateEmail_IsReportedOnTheEmailField()
    {
        _repo.Setup(r => r.GetByEmailAsync("taken@test.com", Tenant)).ReturnsAsync(new Customer());
        var dto = NewCustomer("070 123 456");
        dto.Email = "taken@test.com";

        var act = () => _sut.CreateCustomerAsync(dto, Tenant);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be(nameof(CreateCustomerDTO.Email));
    }

    [Theory]
    [InlineData("070 123 456", "+38970123456")]
    [InlineData("Ana", null)]
    public async Task Search_PassesTheNormalizedNumber_WhenTheTermIsAPhoneNumber(string term, string? expectedPhone)
    {
        _repo.Setup(r => r.SearchAsync(term, Tenant, expectedPhone)).ReturnsAsync(new List<Customer>());

        await _sut.SearchCustomersAsync(term, Tenant);

        _repo.Verify(r => r.SearchAsync(term, Tenant, expectedPhone), Times.Once);
    }
}
