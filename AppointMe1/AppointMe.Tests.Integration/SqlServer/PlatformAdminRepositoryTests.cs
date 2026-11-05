using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using FluentAssertions;
using Xunit;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>The cross-tenant queries are translated to real SQL here; InMemory runs them in .NET instead.</summary>
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", SqlServerCollection.Category)]
    public class PlatformAdminRepositoryTests
    {
        private readonly SqlServerTestDatabase _database;

        public PlatformAdminRepositoryTests(SqlServerDatabaseFixture fixture)
        {
            _database = fixture.Database;
        }

        private static async Task AddUserAsync(ApplicationDbContext db, Guid? tenantId, string email, DateTime createdAt)
        {
            db.Users.Add(new AppointMeAppUser
            {
                Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                Email = email, NormalizedEmail = email.ToUpperInvariant(), FirstName = "T", LastName = "U",
                TenantId = tenantId, CreatedAt = createdAt, UpdatedAt = createdAt
            });
            await db.SaveChangesAsync();
        }

        /// <summary>A tenant with its own unique name and two users, plus usage: 2 customers, 2 appointments, 1 invoice.</summary>
        private static async Task<(Business Business, string Unique)> AddTenantAsync(ApplicationDbContext db, DateTime? createdAt = null)
        {
            var unique = Guid.NewGuid().ToString("N")[..16];
            var business = await SqlServerSeed.AddBusinessAsync(db);
            business.Name = $"Biz {unique}";
            business.Email = $"contact-{unique}@test.com";
            business.Phone = "+38970123456";
            business.Address = "Skopje";
            business.CreatedAt = createdAt ?? DateTime.UtcNow;
            await db.SaveChangesAsync();

            await AddUserAsync(db, business.Id, $"staff-{unique}@test.com", DateTime.UtcNow);
            await AddUserAsync(db, business.Id, $"owner-{unique}@test.com", DateTime.UtcNow.AddDays(-1));

            var first = SqlServerSeed.NewCustomer(business.Id, 1);
            var second = SqlServerSeed.NewCustomer(business.Id, 2);
            var appointment = SqlServerSeed.NewAppointment(business.Id, first.Id);
            var otherAppointment = SqlServerSeed.NewAppointment(business.Id, second.Id);
            db.AddRange(first, second, appointment, otherAppointment,
                SqlServerSeed.NewInvoice(business.Id, appointment.Id, first.Id, $"INV-{unique}"));
            await db.SaveChangesAsync();

            return (business, unique);
        }

        [Fact]
        public async Task Details_AreTranslated_WithOwnerAndCountsForThatTenantOnly()
        {
            await using var db = _database.CreateContext();
            var (tenant, unique) = await AddTenantAsync(db);
            await AddTenantAsync(db);

            var details = await new PlatformAdminRepository(db).GetTenantAsync(tenant.Id);

            details.Should().NotBeNull();
            details!.Should().BeEquivalentTo(new
            {
                tenant.Name,
                OwnerEmail = $"owner-{unique}@test.com",
                Status = BusinessStatus.Active,
                CustomerCount = 2,
                AppointmentCount = 2,
                InvoiceCount = 1,
                Email = $"contact-{unique}@test.com",
                Phone = "+38970123456",
                Address = "Skopje"
            });
        }

        [Fact]
        public async Task Details_OfAnUnknownTenant_AreNull()
        {
            await using var db = _database.CreateContext();

            (await new PlatformAdminRepository(db).GetTenantAsync(Guid.NewGuid())).Should().BeNull();
        }

        [Theory]
        [InlineData("biz {0}")]
        [InlineData("CONTACT-{0}")]
        [InlineData("owner-{0}")]
        [InlineData("STAFF-{0}")]
        public async Task Search_IsTranslated_AndFindsOnlyThatTenant(string pattern)
        {
            await using var db = _database.CreateContext();
            var (tenant, unique) = await AddTenantAsync(db);
            await AddTenantAsync(db);

            var found = await new PlatformAdminRepository(db).GetTenantsAsync(string.Format(pattern, unique), null);

            found.Should().ContainSingle().Which.Id.Should().Be(tenant.Id);
        }

        [Theory]
        [InlineData("%")]
        [InlineData("_")]
        public async Task Search_TreatsWildcardsAsPlainText(string term)
        {
            await using var db = _database.CreateContext();
            await AddTenantAsync(db);

            var found = await new PlatformAdminRepository(db).GetTenantsAsync(term, null);

            found.Should().NotContain(t => !t.Name.Contains(term));
        }

        [Fact]
        public async Task List_IsNewestFirst()
        {
            await using var db = _database.CreateContext();
            var (older, _) = await AddTenantAsync(db, DateTime.UtcNow.AddYears(1));
            var (newer, _) = await AddTenantAsync(db, DateTime.UtcNow.AddYears(2));

            var tenants = await new PlatformAdminRepository(db).GetTenantsAsync(null, null);

            tenants.Select(t => t.Id).Should().ContainInOrder(newer.Id, older.Id);
        }

        [Fact]
        public async Task StatusFilter_IsTranslated()
        {
            await using var db = _database.CreateContext();
            var (tenant, unique) = await AddTenantAsync(db);
            var repository = new PlatformAdminRepository(db);

            (await repository.GetTenantsAsync(unique, BusinessStatus.Active)).Should().ContainSingle()
                .Which.Id.Should().Be(tenant.Id);
            (await repository.GetTenantsAsync(unique, BusinessStatus.Suspended)).Should().BeEmpty();

            tenant.Status = BusinessStatus.Suspended;
            tenant.SuspensionReason = SuspensionReason.Other;
            tenant.SuspensionNote = "A note";
            tenant.SuspendedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            (await repository.GetTenantsAsync(unique, BusinessStatus.Active)).Should().BeEmpty();
            var found = (await repository.GetTenantsAsync(unique, BusinessStatus.Suspended)).Should().ContainSingle().Which;
            found.Status.Should().Be(BusinessStatus.Suspended);

            var details = await repository.GetTenantAsync(tenant.Id);
            details!.SuspensionReason.Should().Be(SuspensionReason.Other);
            details.SuspensionNote.Should().Be("A note");
        }

        [Fact]
        public async Task ActionLog_IsSaved_FiltersByTenant_AndOutlivesTheBusiness()
        {
            await using var db = _database.CreateContext();
            var (tenant, _) = await AddTenantAsync(db);
            var deletedId = Guid.NewGuid();
            var repository = new PlatformAdminRepository(db);

            repository.AddActionLog(new AdminActionLog
            {
                Id = Guid.NewGuid(), At = DateTime.UtcNow, Action = AdminActionType.TenantSuspended, BusinessId = tenant.Id,
                BusinessName = tenant.Name, AdminEmail = "admin@test.com", Reason = "Other", Note = "A note"
            });
            repository.AddActionLog(new AdminActionLog
            {
                Id = Guid.NewGuid(), At = DateTime.UtcNow, Action = AdminActionType.TenantEdited, BusinessId = deletedId,
                BusinessName = "No longer here", ChangedFields = "Name"
            });
            await db.SaveChangesAsync();

            var forTenant = await repository.GetActionLogAsync(tenant.Id, 10);
            forTenant.Should().ContainSingle().Which.Should().BeEquivalentTo(new
            {
                Action = AdminActionType.TenantSuspended, BusinessId = tenant.Id, AdminEmail = "admin@test.com",
                Reason = "Other", Note = "A note", BusinessExists = true
            });

            var forDeleted = await repository.GetActionLogAsync(deletedId, 10);
            forDeleted.Should().ContainSingle().Which.BusinessExists.Should().BeFalse();

            (await repository.GetActionLogAsync(null, 1)).Should().ContainSingle();
        }

        [Fact]
        public async Task UserEmails_AreOnlyThatTenants_OldestFirst()
        {
            await using var db = _database.CreateContext();
            var (tenant, unique) = await AddTenantAsync(db);
            await AddTenantAsync(db);

            var emails = await new PlatformAdminRepository(db).GetUserEmailsAsync(tenant.Id);

            emails.Should().Equal($"owner-{unique}@test.com", $"staff-{unique}@test.com");
        }

        [Fact]
        public async Task Totals_AreTranslated()
        {
            await using var db = _database.CreateContext();
            await AddTenantAsync(db);

            var since = DateTime.UtcNow.AddDays(-30);
            var totals = await new PlatformAdminRepository(db).GetTotalsAsync(since);

            totals.TotalTenants.Should().BeGreaterThan(0);
            (totals.ActiveTenants + totals.SuspendedTenants).Should().Be(totals.TotalTenants);
            totals.NewTenantsLast30Days.Should().BeInRange(1, totals.TotalTenants);
        }
    }
}
