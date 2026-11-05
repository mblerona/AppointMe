using AppointMe.Domain.DomainModels;
using AppointMe.Domain.DTO;
using AppointMe.Domain.Exceptions;
using AppointMe.Repository.Interface;
using AppointMe.Service.Implementation;
using AwesomeAssertions;
using Moq;
using Xunit;

namespace AppointMe.Tests.Unit.Services;

public class ServiceOfferingServiceTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid OwnCategory = Guid.NewGuid();
    private static readonly Guid OtherTenantsCategory = Guid.NewGuid();

    private readonly Mock<IServiceOfferingRepository> _repo = new();
    private readonly ServiceOfferingService _sut;

    public ServiceOfferingServiceTests()
    {
        _repo.Setup(r => r.CategoryBelongsToBusinessAsync(OwnCategory, Tenant)).ReturnsAsync(true);
        _repo.Setup(r => r.CategoryBelongsToBusinessAsync(OtherTenantsCategory, Tenant)).ReturnsAsync(false);
        _sut = new ServiceOfferingService(_repo.Object);
    }

    private ServiceOffering ExistingService()
    {
        var s = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = Tenant, Name = "Old", Price = 10, IsActive = true };
        _repo.Setup(r => r.GetByIdAsync(s.Id, Tenant)).ReturnsAsync(s);
        return s;
    }

    // ---------------- create ----------------

    [Fact]
    public async Task Create_TrimsName_AndSavesForTheTenant()
    {
        var created = await _sut.CreateAsync(
            new CreateServiceOfferingDTO { Name = "  Haircut ", Price = 500, IsActive = true, CategoryId = OwnCategory }, Tenant);

        created.Name.Should().Be("Haircut");
        created.CategoryId.Should().Be(OwnCategory);
        _repo.Verify(r => r.AddAsync(created, Tenant), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Create_WithoutCategory_SkipsTheCategoryCheck()
    {
        await _sut.CreateAsync(new CreateServiceOfferingDTO { Name = "Haircut", Price = 1 }, Tenant);

        _repo.Verify(r => r.CategoryBelongsToBusinessAsync(It.IsAny<Guid>(), It.IsAny<Guid>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Create_WithAnotherTenantsCategory_Throws_AndSavesNothing()
    {
        var act = () => _sut.CreateAsync(
            new CreateServiceOfferingDTO { Name = "Haircut", Price = 1, CategoryId = OtherTenantsCategory }, Tenant);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(ServiceOfferingService.CategoryNotFoundMessage);
        _repo.Verify(r => r.AddAsync(It.IsAny<ServiceOffering>(), It.IsAny<Guid>()), Times.Never);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutName_Throws(string name)
    {
        var act = () => _sut.CreateAsync(new CreateServiceOfferingDTO { Name = name, Price = 1 }, Tenant);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be("Name");
    }

    [Fact]
    public async Task GetSelectable_ReturnsActiveServices_AndOnlyTheInactiveOnesAlreadyAttached()
    {
        var active = new ServiceOffering { Id = Guid.NewGuid(), Name = "Active", IsActive = true };
        var attachedInactive = new ServiceOffering { Id = Guid.NewGuid(), Name = "Attached", IsActive = false };
        var otherInactive = new ServiceOffering { Id = Guid.NewGuid(), Name = "Other", IsActive = false };
        _repo.Setup(r => r.GetAllByBusinessAsync(Tenant)).ReturnsAsync(new[] { active, attachedInactive, otherInactive });

        (await _sut.GetSelectableAsync(Tenant)).Should().Equal(active);
        (await _sut.GetSelectableAsync(Tenant, new[] { attachedInactive.Id })).Should().Equal(active, attachedInactive);
    }

    [Fact]
    public async Task Create_WithoutDuration_StoresNull()
    {
        var created = await _sut.CreateAsync(new CreateServiceOfferingDTO { Name = "X", Price = 1 }, Tenant);

        created.DurationMinutes.Should().BeNull();
    }

    [Theory]
    [InlineData(5)]
    [InlineData(45)]
    [InlineData(480)]
    public async Task Create_WithDurationInRange_StoresIt(int minutes)
    {
        var created = await _sut.CreateAsync(new CreateServiceOfferingDTO { Name = "X", Price = 1, DurationMinutes = minutes }, Tenant);

        created.DurationMinutes.Should().Be(minutes);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(481)]
    public async Task Create_WithDurationOutOfRange_Throws_AndSavesNothing(int minutes)
    {
        var act = () => _sut.CreateAsync(new CreateServiceOfferingDTO { Name = "X", Price = 1, DurationMinutes = minutes }, Tenant);

        var ex = (await act.Should().ThrowAsync<BusinessRuleException>()).Which;
        ex.PropertyName.Should().Be("DurationMinutes");
        ex.Message.Should().Be("Duration must be between 5 and 480 minutes.");
        _repo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task Update_SetsAndClearsTheDuration()
    {
        var existing = ExistingService();

        await _sut.UpdateAsync(existing.Id, new UpdateServiceOfferingDTO { Id = existing.Id, Name = "X", Price = 1, DurationMinutes = 60 }, Tenant);
        existing.DurationMinutes.Should().Be(60);

        await _sut.UpdateAsync(existing.Id, new UpdateServiceOfferingDTO { Id = existing.Id, Name = "X", Price = 1, DurationMinutes = null }, Tenant);
        existing.DurationMinutes.Should().BeNull();
    }

    [Fact]
    public async Task Update_WithDurationOutOfRange_Throws_AndLeavesTheServiceUnchanged()
    {
        var existing = ExistingService();
        existing.DurationMinutes = 30;

        var act = () => _sut.UpdateAsync(existing.Id, new UpdateServiceOfferingDTO { Id = existing.Id, Name = "New", Price = 1, DurationMinutes = 481 }, Tenant);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be("DurationMinutes");
        existing.DurationMinutes.Should().Be(30);
        existing.Name.Should().Be("Old");
        _repo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task Create_WithNegativePrice_Throws()
    {
        var act = () => _sut.CreateAsync(new CreateServiceOfferingDTO { Name = "X", Price = -1 }, Tenant);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.PropertyName.Should().Be("Price");
    }

    // ---------------- read / update ----------------

    [Fact]
    public async Task GetById_ForMissingOrOtherTenantService_ThrowsKeyNotFound()
    {
        var act = () => _sut.GetByIdAsync(Guid.NewGuid(), Tenant);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Update_ChangesFields_AndSaves()
    {
        var s = ExistingService();

        await _sut.UpdateAsync(s.Id,
            new UpdateServiceOfferingDTO { Id = s.Id, Name = " New ", Price = 20, IsActive = false, CategoryId = OwnCategory }, Tenant);

        s.Name.Should().Be("New");
        s.Price.Should().Be(20);
        s.IsActive.Should().BeFalse();
        s.CategoryId.Should().Be(OwnCategory);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Update_WithAnotherTenantsCategory_Throws_AndLeavesTheServiceUnchanged()
    {
        var s = ExistingService();

        var act = () => _sut.UpdateAsync(s.Id,
            new UpdateServiceOfferingDTO { Id = s.Id, Name = "New", Price = 20, CategoryId = OtherTenantsCategory }, Tenant);

        await act.Should().ThrowAsync<BusinessRuleException>();
        s.Name.Should().Be("Old");
        s.CategoryId.Should().BeNull();
        _repo.Verify(r => r.SaveChangesAsync(), Times.Never);
    }

    [Fact]
    public async Task Update_ForMissingService_ThrowsKeyNotFound()
    {
        var act = () => _sut.UpdateAsync(Guid.NewGuid(), new UpdateServiceOfferingDTO { Name = "X" }, Tenant);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ---------------- delete ----------------

    [Fact]
    public async Task Delete_UnusedService_Deletes()
    {
        var s = ExistingService();

        await _sut.DeleteAsync(s.Id, Tenant);

        _repo.Verify(r => r.DeleteAsync(s), Times.Once);
        _repo.Verify(r => r.SaveChangesAsync(), Times.Once);
    }

    [Fact]
    public async Task Delete_ServiceUsedByAppointments_Throws_AndDeletesNothing()
    {
        var s = ExistingService();
        _repo.Setup(r => r.IsUsedByAppointmentsAsync(s.Id, Tenant)).ReturnsAsync(true);

        var act = () => _sut.DeleteAsync(s.Id, Tenant);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(ServiceOfferingService.UsedByAppointmentsMessage);
        _repo.Verify(r => r.DeleteAsync(It.IsAny<ServiceOffering>()), Times.Never);
    }

    [Fact]
    public async Task Delete_MissingService_ThrowsKeyNotFound()
    {
        var act = () => _sut.DeleteAsync(Guid.NewGuid(), Tenant);
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
