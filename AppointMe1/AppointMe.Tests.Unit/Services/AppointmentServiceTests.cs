//using Xunit;
//using Moq;
//using FluentAssertions;
//using System;
//using System.Threading.Tasks;
//using AppointMe.Domain.DomainModels;
//using AppointMe.Repository.Interface;
//using AppointMe.Service.Implementation;

//// TODO: adjust namespaces
//// using AppointMe.Domain.DomainModels;
//// using AppointMe.Service.Implementation;
//// using AppointMe.Service.Interface;
//// using AppointMe.Repository.Interface;

//public class AppointmentServiceTests
//{
//    [Fact]
//    public async Task SetStatusAsync_ShouldUpdateStatus_WhenAppointmentBelongsToTenant()
//    {
//        // Arrange
//        var tenantId = Guid.NewGuid();
//        var appointmentId = Guid.NewGuid();

//        var appt = new Appointment
//        {
//            Id = appointmentId,
//            TenantId = tenantId,
//            Status = AppointmentStatus.Pending
//        };

//        var repo = new Mock<IAppointmentRepository>();
//        repo.Setup(r => r.GetByIdAsync(appointmentId, tenantId))
//            .ReturnsAsync(appt);

//        // If your service has more dependencies, mock them too.
//        var service = new AppointmentService(repo.Object);

//        // Act
//        await service.SetStatusAsync(appointmentId, tenantId, AppointmentStatus.Completed);

//        // Assert
//        appt.Status.Should().Be(AppointmentStatus.Completed);
//        repo.Verify(r => r.UpdateAsync(appt), Times.Once);
//    }

//    [Fact]
//    public async Task SetStatusAsync_ShouldThrow_WhenAppointmentNotFound()
//    {
//        // Arrange
//        var tenantId = Guid.NewGuid();
//        var appointmentId = Guid.NewGuid();

//        var repo = new Mock<IAppointmentRepository>();
//        repo.Setup(r => r.GetByIdAsync(appointmentId, tenantId))
//            .ReturnsAsync((Appointment?)null);

//        var service = new AppointmentService(repo.Object);

//        // Act
//        Func<Task> act = async () =>
//            await service.SetStatusAsync(appointmentId, tenantId, AppointmentStatus.Cancelled);

//        // Assert
//        await act.Should().ThrowAsync<InvalidOperationException>();
//        repo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>()), Times.Never);
//    }

//    [Fact]
//    public async Task SetStatusAsync_ShouldNotAllow_InvalidTransitions_IfYouHaveRules()
//    {
//        // OPTIONAL: Only keep this if your app enforces transition rules.
//        // Example rule: Completed cannot be changed.

//        var tenantId = Guid.NewGuid();
//        var appointmentId = Guid.NewGuid();

//        var appt = new Appointment
//        {
//            Id = appointmentId,
//            TenantId = tenantId,
//            Status = AppointmentStatus.Completed
//        };

//        var repo = new Mock<IAppointmentRepository>();
//        repo.Setup(r => r.GetByIdAsync(appointmentId, tenantId))
//            .ReturnsAsync(appt);

//        var service = new AppointmentService(repo.Object);

//        Func<Task> act = async () =>
//            await service.SetStatusAsync(appointmentId, tenantId, AppointmentStatus.Pending);

//        await act.Should().ThrowAsync<InvalidOperationException>();
//        repo.Verify(r => r.UpdateAsync(It.IsAny<Appointment>()), Times.Never);
//    }
//}