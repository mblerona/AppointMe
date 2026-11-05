using AppointMe.Domain.DomainModels;
using AppointMe.Domain.Exceptions;
using AppointMe.Domain.Identity;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using AppointMe.Service.Admin;
using AppointMe.Service.Email;
using AppointMe.Service.Implementation;
using AppointMe.Service.Interface;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>The full deletion on real SQL Server, where foreign keys, cascades and transactions are enforced.</summary>
    [Collection(SqlServerCollection.Name)]
    [Trait("Category", SqlServerCollection.Category)]
    public class TenantDeletionTests
    {
        private readonly SqlServerTestDatabase _database;

        public TenantDeletionTests(SqlServerDatabaseFixture fixture)
        {
            _database = fixture.Database;
        }

        private sealed class RecordingEmail : IEmailService
        {
            public List<EmailMessage> Sent { get; } = new();

            public Task SendEmailAsync(EmailMessage message)
            {
                Sent.Add(message);
                return Task.CompletedTask;
            }

            public Task SendEmailWithAttachmentAsync(EmailMessage message, byte[] attachmentBytes, string attachmentFileName, string contentType) =>
                SendEmailAsync(message);
        }

        private sealed class RecordingLogos : ILogoStorage
        {
            public List<Guid> Deleted { get; } = new();

            public Task<string> SaveAsync(Guid businessId, string extension, Stream content) => throw new NotSupportedException();

            public Task DeleteAllAsync(Guid businessId)
            {
                Deleted.Add(businessId);
                return Task.CompletedTask;
            }
        }

        private readonly RecordingEmail _email = new();
        private readonly RecordingLogos _logos = new();

        private PlatformAdminService Service(ApplicationDbContext db) => new(
            new PlatformAdminRepository(db), new BusinessRepository(db), _email, _logos, null!, null!,
            Options.Create(new SupportOptions()), TimeProvider.System, NullLogger<PlatformAdminService>.Instance);

        /// <summary>The ids of one tenant's rows in every table, including those without a tenant column.</summary>
        private sealed record Tenant(
            Guid Id, string Name, string UserId, string UserEmail, string StaffId, Guid CategoryId, Guid ServiceId,
            Guid CustomerId, Guid AppointmentId, Guid InvoiceId, Guid LineId);

        private static string Quote(Guid id) => $"'{id}'";

        /// <summary>One count per table, for the rows that belong to this tenant.</summary>
        private static Dictionary<string, string> CountQueries(Tenant t)
        {
            var users = $"'{t.UserId}', '{t.StaffId}'";
            return new Dictionary<string, string>
            {
                ["Businesses"] = $"Id = {Quote(t.Id)}",
                ["Customers"] = $"TenantId = {Quote(t.Id)}",
                ["Appointments"] = $"TenantId = {Quote(t.Id)}",
                ["AppointmentServices"] = $"AppointmentId = {Quote(t.AppointmentId)}",
                ["Invoices"] = $"TenantId = {Quote(t.Id)}",
                ["InvoiceLines"] = $"InvoiceId = {Quote(t.InvoiceId)}",
                ["ServiceOfferings"] = $"BusinessId = {Quote(t.Id)}",
                ["ServiceCategories"] = $"BusinessId = {Quote(t.Id)}",
                ["AspNetUsers"] = $"TenantId = {Quote(t.Id)} OR Id IN ({users})",
                ["AspNetUserRoles"] = $"UserId IN ({users})",
                ["AspNetUserClaims"] = $"UserId IN ({users})",
                ["AspNetUserLogins"] = $"UserId IN ({users})",
                ["AspNetUserTokens"] = $"UserId IN ({users})"
            };
        }

        private async Task<Dictionary<string, int>> CountsAsync(Tenant t)
        {
            var counts = new Dictionary<string, int>();
            foreach (var (table, where) in CountQueries(t))
                counts[table] = await _database.ScalarAsync($"SELECT COUNT(*) AS Value FROM [{table}] WHERE {where}");
            return counts;
        }

        /// <summary>A tenant with rows in every table: two users, each with a role, a claim, a login and a token.</summary>
        private static async Task<Tenant> AddFullTenantAsync(ApplicationDbContext db, string role)
        {
            var unique = Guid.NewGuid().ToString("N")[..12];
            var business = await SqlServerSeed.AddBusinessAsync(db);
            business.Name = $"Tenant {unique}";

            var roleId = await db.Roles.Where(r => r.Name == role).Select(r => r.Id).FirstOrDefaultAsync();
            if (roleId == null)
            {
                var newRole = new IdentityRole(role) { NormalizedName = role.ToUpperInvariant() };
                db.Roles.Add(newRole);
                roleId = newRole.Id;
            }

            var owner = NewUser(business.Id, $"owner-{unique}@test.com", DateTime.UtcNow.AddDays(-1));
            var staff = NewUser(business.Id, $"staff-{unique}@test.com", DateTime.UtcNow);
            db.Users.AddRange(owner, staff);
            foreach (var user in new[] { owner, staff })
            {
                db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleId });
                db.UserClaims.Add(new IdentityUserClaim<string> { UserId = user.Id, ClaimType = "c", ClaimValue = "v" });
                db.UserLogins.Add(new IdentityUserLogin<string> { UserId = user.Id, LoginProvider = "p", ProviderKey = user.Id });
                db.UserTokens.Add(new IdentityUserToken<string> { UserId = user.Id, LoginProvider = "p", Name = "n", Value = "v" });
            }
            await db.SaveChangesAsync();

            var category = new ServiceCategory { Id = Guid.NewGuid(), BusinessId = business.Id, Name = "Hair", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            var service = new ServiceOffering { Id = Guid.NewGuid(), BusinessId = business.Id, CategoryId = category.Id, Name = "Cut", Price = 10 };
            var customer = SqlServerSeed.NewCustomer(business.Id);
            var appointment = SqlServerSeed.NewAppointment(business.Id, customer.Id, "ORD-1");
            var invoice = SqlServerSeed.NewInvoice(business.Id, appointment.Id, customer.Id, "INV-1");
            var line = new InvoiceLine { Id = Guid.NewGuid(), InvoiceId = invoice.Id, NameSnapshot = "Cut", Qty = 1, UnitPrice = 10, LineTotal = 10 };
            db.Add(category);
            db.Add(service);
            db.Add(customer);
            db.Add(appointment);
            db.Add(new AppointmentServiceModel { AppointmentId = appointment.Id, ServiceOfferingId = service.Id, PriceAtBooking = 10 });
            db.Add(invoice);
            db.Add(line);
            await db.SaveChangesAsync();

            return new Tenant(business.Id, business.Name, owner.Id, owner.Email!, staff.Id, category.Id, service.Id,
                customer.Id, appointment.Id, invoice.Id, line.Id);
        }

        private static AppointMeAppUser NewUser(Guid? tenantId, string email, DateTime createdAt) => new()
        {
            Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
            Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true,
            FirstName = "T", LastName = "U", TenantId = tenantId, CreatedAt = createdAt, UpdatedAt = createdAt
        };

        private static void ShouldHaveARowInEveryTable(Dictionary<string, int> counts) =>
            counts.Should().OnlyContain(c => c.Value > 0, "the test must really exercise every table");

        [Fact]
        public async Task Delete_LeavesNoRowsForThatTenant_AndDoesNotTouchAnother()
        {
            Tenant doomed, other;
            await using (var seed = _database.CreateContext())
            {
                doomed = await AddFullTenantAsync(seed, "SharedRole");
                other = await AddFullTenantAsync(seed, "SharedRole");
            }

            var before = await CountsAsync(doomed);
            var otherBefore = await CountsAsync(other);
            ShouldHaveARowInEveryTable(before);
            ShouldHaveARowInEveryTable(otherBefore);

            await using (var db = _database.CreateContext())
                await Service(db).DeleteTenantAsync(doomed.Id, doomed.Name, "Fake business", "admin@test.com");

            (await CountsAsync(doomed)).Should().OnlyContain(c => c.Value == 0);
            (await CountsAsync(other)).Should().BeEquivalentTo(otherBefore);

            (await _database.ScalarAsync("SELECT COUNT(*) AS Value FROM AspNetRoles WHERE Name = 'SharedRole'"))
                .Should().Be(1, "roles aren't the tenant's");

            _logos.Deleted.Should().Equal(doomed.Id);
            _email.Sent.Select(m => m.MailTo).Should().Contain(doomed.UserEmail);
        }

        /// <summary>Fails when a table is added without deciding what deleting a tenant does to it.</summary>
        [Fact]
        public async Task EveryTableInTheModel_IsCoveredByTheDeletionTest()
        {
            await using var db = _database.CreateContext();
            var modelTables = db.Model.GetEntityTypes().Select(e => e.GetTableName()!).Distinct();
            var fake = new Tenant(Guid.Empty, "", "", "", "", Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
            var notTenantData = new[] { "AspNetRoles", "AspNetRoleClaims", "AdminActionLogs" };

            modelTables.Should().BeEquivalentTo(CountQueries(fake).Keys.Concat(notTenantData));
        }

        [Fact]
        public async Task Approve_RunsTheSameDeletion_AndTheLogKeepsWhoDidIt_ButNotTheTenantsData()
        {
            Tenant doomed;
            await using (var seed = _database.CreateContext())
            {
                doomed = await AddFullTenantAsync(seed, "SharedRole");
                var business = await seed.Businesses.SingleAsync(b => b.Id == doomed.Id);
                business.DeletionRequestedAt = DateTime.UtcNow;
                business.DeletionRequestReason = "A private reason";
                seed.AdminActionLogs.Add(new AdminActionLog
                {
                    Id = Guid.NewGuid(), At = DateTime.UtcNow, Action = AdminActionType.TenantEdited, BusinessId = doomed.Id,
                    BusinessName = doomed.Name, AdminEmail = "admin@test.com", Note = "A note", ChangedFields = "Name", Reason = "Kept"
                });
                await seed.SaveChangesAsync();
            }

            await using (var db = _database.CreateContext())
                await Service(db).ApproveDeletionRequestAsync(doomed.Id, doomed.Name, "admin@test.com");

            (await CountsAsync(doomed)).Should().OnlyContain(c => c.Value == 0);

            await using var check = _database.CreateContext();
            var log = await check.AdminActionLogs.AsNoTracking().Where(l => l.BusinessId == doomed.Id).ToListAsync();
            log.Should().HaveCount(2);
            log.Should().OnlyContain(l => l.BusinessName == doomed.Name && l.AdminEmail == "admin@test.com" && l.Note == null && l.ChangedFields == null);
            log.Should().Contain(l => l.Action == AdminActionType.TenantEdited && l.Reason == "Kept");
            log.Should().Contain(l => l.Action == AdminActionType.DeletionRequestApproved && l.Reason == PlatformAdminService.ApprovedRequestReason);
            log.Should().NotContain(l => l.Reason == "A private reason");
        }

        [Fact]
        public async Task AFailureInTheLastStep_RollsEverythingBack()
        {
            Tenant doomed;
            await using (var seed = _database.CreateContext())
            {
                doomed = await AddFullTenantAsync(seed, "SharedRole");

                // A row with a foreign key the deletion doesn't know about, as a concurrent insert would be:
                // another tenant's booking of this tenant's service stops the service from being deleted.
                var other = await AddFullTenantAsync(seed, "SharedRole");
                seed.Add(new AppointmentServiceModel { AppointmentId = other.AppointmentId, ServiceOfferingId = doomed.ServiceId, PriceAtBooking = 1 });
                await seed.SaveChangesAsync();
            }
            var before = await CountsAsync(doomed);

            await using (var db = _database.CreateContext())
            {
                // Directly on the repository, past the check in the service that would find this first.
                var act = () => new PlatformAdminRepository(db).DeleteTenantAsync(doomed.Id, new AdminActionLog
                {
                    Id = Guid.NewGuid(), At = DateTime.UtcNow, BusinessId = doomed.Id, BusinessName = doomed.Name
                });

                await act.Should().ThrowAsync<DbUpdateException>();
            }

            (await CountsAsync(doomed)).Should().BeEquivalentTo(before);
            (await _database.ScalarAsync($"SELECT COUNT(*) AS Value FROM AdminActionLogs WHERE BusinessId = {Quote(doomed.Id)}")).Should().Be(0);
        }

        [Fact]
        public async Task AnotherTenantsAppointmentOnThisTenantsCustomer_StopsTheDelete_SoTheCascadeCantTakeIt()
        {
            Tenant doomed, other;
            Guid strayAppointment;
            await using (var seed = _database.CreateContext())
            {
                doomed = await AddFullTenantAsync(seed, "SharedRole");
                other = await AddFullTenantAsync(seed, "SharedRole");
                var stray = SqlServerSeed.NewAppointment(other.Id, doomed.CustomerId, "STRAY");
                seed.Add(stray);
                await seed.SaveChangesAsync();
                strayAppointment = stray.Id;
            }
            var before = await CountsAsync(doomed);

            await using (var db = _database.CreateContext())
            {
                var act = () => Service(db).DeleteTenantAsync(doomed.Id, doomed.Name, null, "admin@test.com");
                await act.Should().ThrowAsync<BusinessRuleException>();
            }

            (await CountsAsync(doomed)).Should().BeEquivalentTo(before);
            (await _database.ScalarAsync($"SELECT COUNT(*) AS Value FROM Appointments WHERE Id = {Quote(strayAppointment)}")).Should().Be(1);
            _logos.Deleted.Should().BeEmpty();
            _email.Sent.Should().BeEmpty();
        }

        [Theory]
        [InlineData("service")]
        [InlineData("category")]
        public async Task OtherLinksFromAnotherTenant_AreFound(string kind)
        {
            await using var db = _database.CreateContext();
            var doomed = await AddFullTenantAsync(db, "SharedRole");
            var other = await AddFullTenantAsync(db, "SharedRole");

            if (kind == "service")
            {
                db.Add(new AppointmentServiceModel { AppointmentId = other.AppointmentId, ServiceOfferingId = doomed.ServiceId, PriceAtBooking = 1 });
            }
            else
            {
                var service = await db.ServiceOfferings.SingleAsync(s => s.Id == other.ServiceId);
                service.CategoryId = doomed.CategoryId;
            }
            await db.SaveChangesAsync();

            var repository = new PlatformAdminRepository(db);
            (await repository.HasCrossTenantLinksAsync(doomed.Id)).Should().BeTrue();
        }

        [Fact]
        public async Task ATenantWithNoLinks_HasNone()
        {
            await using var db = _database.CreateContext();
            var tenant = await AddFullTenantAsync(db, "SharedRole");
            await AddFullTenantAsync(db, "SharedRole");

            (await new PlatformAdminRepository(db).HasCrossTenantLinksAsync(tenant.Id)).Should().BeFalse();
        }

        [Fact]
        public async Task APlatformAdminAccount_IsNeverDeleted_EvenWithATenantIdSetByHand()
        {
            Tenant doomed;
            string adminId;
            await using (var seed = _database.CreateContext())
            {
                doomed = await AddFullTenantAsync(seed, "SharedRole");
                var role = await seed.Roles.FirstOrDefaultAsync(r => r.Name == PlatformRoles.Admin);
                if (role == null)
                {
                    role = new IdentityRole(PlatformRoles.Admin) { NormalizedName = PlatformRoles.Admin.ToUpperInvariant() };
                    seed.Roles.Add(role);
                }
                var admin = NewUser(doomed.Id, $"admin-{Guid.NewGuid():N}@test.com", DateTime.UtcNow);
                seed.Users.Add(admin);
                seed.UserRoles.Add(new IdentityUserRole<string> { UserId = admin.Id, RoleId = role.Id });
                await seed.SaveChangesAsync();
                adminId = admin.Id;
            }

            await using (var db = _database.CreateContext())
                await Service(db).DeleteTenantAsync(doomed.Id, doomed.Name, null, "admin@test.com");

            await using var check = _database.CreateContext();
            var kept = await check.Users.AsNoTracking().SingleAsync(u => u.Id == adminId);
            kept.TenantId.Should().BeNull();
            (await check.UserRoles.CountAsync(r => r.UserId == adminId)).Should().Be(1);
            (await check.Businesses.CountAsync(b => b.Id == doomed.Id)).Should().Be(0);
        }

        [Fact]
        public async Task PendingRequests_AreTranslated()
        {
            await using var db = _database.CreateContext();
            var tenant = await AddFullTenantAsync(db, "SharedRole");
            var business = await db.Businesses.SingleAsync(b => b.Id == tenant.Id);
            business.DeletionRequestedAt = DateTime.UtcNow;
            business.DeletionRequestReason = "Closing";
            await db.SaveChangesAsync();

            var requests = await new PlatformAdminRepository(db).GetPendingDeletionRequestsAsync();

            var request = requests.Should().ContainSingle(r => r.TenantId == tenant.Id).Which;
            request.OwnerEmail.Should().Be(tenant.UserEmail);
            request.Reason.Should().Be("Closing");
        }
    }
}
