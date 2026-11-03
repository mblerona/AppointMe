using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>Seeding and assertion helpers for the SQL Server tests (current schema, via EF).</summary>
    public static class SqlServerSeed
    {
        /// <summary>SQL Server error numbers: duplicate key in a unique index / constraint, and FK/check conflict.</summary>
        public const int UniqueIndexViolation = 2601;
        public const int UniqueConstraintViolation = 2627;
        public const int ForeignKeyViolation = 547;

        public static async Task<Business> AddBusinessAsync(ApplicationDbContext db, Guid? id = null)
        {
            var business = new Business { Id = id ?? Guid.NewGuid(), Name = "Test business" };
            db.Businesses.Add(business);
            await db.SaveChangesAsync();
            return business;
        }

        public static Customer NewCustomer(Guid tenantId, int number = 1, string? email = null) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerNumber = number,
            FirstName = "Ana",
            LastName = "Doe",
            Email = email ?? $"{Guid.NewGuid():N}@test.com",
            PhoneNumber = "+38970123456",
            State = "North Macedonia",
            City = "Skopje"
        };

        public static Appointment NewAppointment(Guid tenantId, Guid customerId, string? orderNumber = null) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            CustomerId = customerId,
            OrderNumber = orderNumber ?? $"ORD-{Guid.NewGuid():N}"[..20],
            Description = "Test",
            AppointmentDate = new DateTime(2026, 12, 1, 10, 0, 0),
            Status = AppointmentStatus.Scheduled
        };

        public static Invoice NewInvoice(Guid tenantId, Guid appointmentId, Guid customerId, string number) => new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AppointmentId = appointmentId,
            CustomerId = customerId,
            InvoiceNumber = number,
            CustomerNameSnapshot = "Ana Doe",
            BusinessNameSnapshot = "Test business"
        };

        /// <summary>Asserts that saving fails with one of the given SQL Server error numbers.</summary>
        public static async Task ShouldFailWithSqlErrorAsync(this ApplicationDbContext db, params int[] errorNumbers)
        {
            var act = () => db.SaveChangesAsync();

            var ex = (await act.Should().ThrowAsync<DbUpdateException>()).Which;
            ex.InnerException.Should().BeOfType<SqlException>()
                .Which.Number.Should().BeOneOf(errorNumbers);
        }
    }
}
