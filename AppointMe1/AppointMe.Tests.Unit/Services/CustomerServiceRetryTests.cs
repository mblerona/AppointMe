using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Implementation;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

/// <summary>
/// Audit B10: customer numbers are max + 1, so two requests can pick the same one. Uses a real DbContext,
/// because a failed customer stays tracked as Added, which mocks can't show.
/// </summary>
public class CustomerServiceRetryTests
{
    private readonly InMemoryDatabaseRoot _root = new();
    private readonly string _dbName = $"customer-retry-{Guid.NewGuid()}";
    private readonly Guid _tenantId = Guid.NewGuid();

    private sealed class FailCustomerInsertInterceptor(int failures, Action? onFailure = null) : SaveChangesInterceptor
    {
        private int _failuresLeft = failures;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var insertingCustomer = eventData.Context!.ChangeTracker.Entries<Customer>().Any(e => e.State == EntityState.Added);

            if (insertingCustomer && _failuresLeft > 0)
            {
                _failuresLeft--;
                onFailure?.Invoke();
                throw new DbUpdateException("Simulated unique index violation");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private ApplicationDbContext NewContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(_dbName, _root)
            .AddInterceptors(interceptors)
            .Options);

    private static CustomerService BuildService(ApplicationDbContext db) =>
        new(new CustomerRepository(db), new BusinessRepository(db));

    private static CreateCustomerDTO Dto(string email) => new()
    {
        FirstName = "Ana", LastName = "Doe", Email = email, PhoneNumber = "070123456", State = "MK", City = "Skopje"
    };

    private void AddOtherCustomer(string email, int number)
    {
        using var other = NewContext();
        other.Customers.Add(new Customer
        {
            Id = Guid.NewGuid(), TenantId = _tenantId, CustomerNumber = number, FirstName = "Other", LastName = "Request",
            Email = email, PhoneNumber = "+38970123456", State = "MK", City = "Skopje"
        });
        other.SaveChanges();
    }

    [Fact]
    public async Task AfterOneFailedSave_StoresExactlyOneCustomer()
    {
        await using var db = NewContext(new FailCustomerInsertInterceptor(failures: 1));

        var result = await BuildService(db).CreateCustomerAsync(Dto("ana@test.com"), _tenantId);

        await using var check = NewContext();
        var stored = await check.Customers.ToListAsync();
        stored.Should().ContainSingle("the failed attempt must not be inserted again on retry");
        stored[0].Id.Should().Be(result.Id);
    }

    [Fact]
    public async Task WhenAnotherRequestTookTheNumber_TheRetryUsesTheNextOne()
    {
        await using var db = NewContext(new FailCustomerInsertInterceptor(failures: 1, () => AddOtherCustomer("other@test.com", 1)));

        var result = await BuildService(db).CreateCustomerAsync(Dto("ana@test.com"), _tenantId);

        result.CustomerNumber.Should().Be(2);
    }

    [Fact]
    public async Task WhenAnotherRequestSavedTheSameEmail_TheRetryReportsTheDuplicate()
    {
        await using var db = NewContext(new FailCustomerInsertInterceptor(failures: 1, () => AddOtherCustomer("ana@test.com", 1)));

        var act = () => BuildService(db).CreateCustomerAsync(Dto("ana@test.com"), _tenantId);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be("Email");
    }

    [Fact]
    public async Task SucceedsOnTheLastAllowedAttempt()
    {
        await using var db = NewContext(new FailCustomerInsertInterceptor(failures: CustomerService.MaxCreateAttempts - 1));

        await BuildService(db).CreateCustomerAsync(Dto("ana@test.com"), _tenantId);

        await using var check = NewContext();
        (await check.Customers.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task WhenEveryAttemptFails_ShowsAFriendlyError_AndStoresNothing()
    {
        await using var db = NewContext(new FailCustomerInsertInterceptor(failures: CustomerService.MaxCreateAttempts));

        var act = () => BuildService(db).CreateCustomerAsync(Dto("ana@test.com"), _tenantId);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage("*could not be saved*");
        await using var check = NewContext();
        (await check.Customers.CountAsync()).Should().Be(0);
    }
}
