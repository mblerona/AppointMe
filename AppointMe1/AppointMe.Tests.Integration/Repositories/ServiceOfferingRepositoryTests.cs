using AppointMe.Domain.DomainModels;
using AppointMe.Repository.Data;
using AppointMe.Repository.Implementation;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AppointMe.Tests.Integration.Repositories
{
    public class ServiceOfferingRepositoryTests
    {
        // ----------------- seed helpers -----------------

        private static Business SeedBusiness(ApplicationDbContext db, Guid businessId, string name = "Biz")
        {
            var biz = new Business
            {
                Id = businessId,
                Name = name,
                Address = "Skopje",
                EnableServices = true,
                EnableInvoices = true
            };
            db.Businesses.Add(biz);
            return biz;
        }

        private static ServiceCategory SeedCategory(ApplicationDbContext db, Guid businessId, string name = "Category")
        {
            var cat = new ServiceCategory
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                Name = name,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.ServiceCategories.Add(cat);
            return cat;
        }

        private static ServiceOffering SeedService(ApplicationDbContext db, Guid businessId, Guid categoryId, string name, decimal price, bool active)
        {
            var svc = new ServiceOffering
            {
                Id = Guid.NewGuid(),
                BusinessId = businessId,
                CategoryId = categoryId,
                Name = name,
                Price = price,
                IsActive = active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.ServiceOfferings.Add(svc);
            return svc;
        }

        // ----------------- tests -----------------

        [Fact]
        public async Task GetAllByBusinessAsync_ReturnsOnlyBusinessServices()
        {
            var db = DbContextFactory.Create();
            var repo = new ServiceOfferingRepository(db);

            var bizA = Guid.NewGuid();
            var bizB = Guid.NewGuid();

            SeedBusiness(db, bizA, "A");
            SeedBusiness(db, bizB, "B");

            var catA = SeedCategory(db, bizA, "A-cat");
            var catB = SeedCategory(db, bizB, "B-cat");

            SeedService(db, bizA, catA.Id, "A-1", 100, true);
            SeedService(db, bizA, catA.Id, "A-2", 200, false);
            SeedService(db, bizB, catB.Id, "B-1", 999, true);

            await db.SaveChangesAsync();

            var result = (await repo.GetAllByBusinessAsync(bizA)).ToList();

            result.Should().HaveCount(2);
            result.All(s => s.BusinessId == bizA).Should().BeTrue();
        }

        [Fact]
        public async Task GetActiveByBusinessAsync_ReturnsOnlyActiveServices()
        {
            var db = DbContextFactory.Create();
            var repo = new ServiceOfferingRepository(db);

            var biz = Guid.NewGuid();
            SeedBusiness(db, biz);
            var cat = SeedCategory(db, biz);

            var active = SeedService(db, biz, cat.Id, "Active", 100, true);
            SeedService(db, biz, cat.Id, "Inactive", 200, false);

            await db.SaveChangesAsync();

            var result = (await repo.GetActiveByBusinessAsync(biz)).ToList();

            result.Should().HaveCount(1);
            result[0].Id.Should().Be(active.Id);
            result[0].IsActive.Should().BeTrue();
        }

        [Fact]
        public async Task GetByIdAsync_WhenServiceBelongsToBusiness_ReturnsService()
        {
            var db = DbContextFactory.Create();
            var repo = new ServiceOfferingRepository(db);

            var biz = Guid.NewGuid();
            SeedBusiness(db, biz);
            var cat = SeedCategory(db, biz);

            var svc = SeedService(db, biz, cat.Id, "S1", 100, true);
            await db.SaveChangesAsync();

            var loaded = await repo.GetByIdAsync(svc.Id, biz);

            loaded.Should().NotBeNull();
            loaded!.Id.Should().Be(svc.Id);
            loaded.BusinessId.Should().Be(biz);
        }

        [Fact]
        public async Task GetByIdAsync_WhenServiceInOtherBusiness_ReturnsNull()
        {
            var db = DbContextFactory.Create();
            var repo = new ServiceOfferingRepository(db);

            var bizA = Guid.NewGuid();
            var bizB = Guid.NewGuid();

            SeedBusiness(db, bizA, "A");
            SeedBusiness(db, bizB, "B");

            var catA = SeedCategory(db, bizA);
            var svcA = SeedService(db, bizA, catA.Id, "A-Only", 100, true);

            await db.SaveChangesAsync();

            var loaded = await repo.GetByIdAsync(svcA.Id, bizB);

            loaded.Should().BeNull();
        }

        [Fact]
        public async Task GetByIdsForBusinessAsync_ReturnsOnlyMatchingIds_AndOnlyForBusiness()
        {
            var db = DbContextFactory.Create();
            var repo = new ServiceOfferingRepository(db);

            var bizA = Guid.NewGuid();
            var bizB = Guid.NewGuid();

            SeedBusiness(db, bizA, "A");
            SeedBusiness(db, bizB, "B");

            var catA = SeedCategory(db, bizA);
            var catB = SeedCategory(db, bizB);

            var s1 = SeedService(db, bizA, catA.Id, "A1", 100, true);
            var s2 = SeedService(db, bizA, catA.Id, "A2", 200, true);
            var sB = SeedService(db, bizB, catB.Id, "B1", 999, true);

            await db.SaveChangesAsync();

            var ids = new List<Guid> { s1.Id, s2.Id, sB.Id, Guid.Empty, s1.Id }; // includes noise + duplicates

            var result = (await repo.GetByIdsForBusinessAsync(ids, bizA)).ToList();

            result.Should().HaveCount(2);
            result.Select(x => x.Id).Should().BeEquivalentTo(new[] { s1.Id, s2.Id });
            result.All(x => x.BusinessId == bizA).Should().BeTrue();
        }

        [Fact]
        public async Task AddAsync_OverridesBusinessId_ToEnforceOwnership()
        {
            var db = DbContextFactory.Create();
            var repo = new ServiceOfferingRepository(db);

            var realBiz = Guid.NewGuid();
            var otherBiz = Guid.NewGuid();

            SeedBusiness(db, realBiz, "Real");
            SeedBusiness(db, otherBiz, "Other");

            var cat = SeedCategory(db, realBiz);
            await db.SaveChangesAsync();

            var svc = new ServiceOffering
            {
                Id = Guid.NewGuid(),
                BusinessId = otherBiz,   // intentionally wrong
                CategoryId = cat.Id,
                Name = "Injected",
                Price = 123,
                IsActive = true
            };

            await repo.AddAsync(svc, realBiz);
            await repo.SaveChangesAsync();

            // detatch & reload from DB
            db.ChangeTracker.Clear();

            var loaded = await db.ServiceOfferings.FirstOrDefaultAsync(x => x.Id == svc.Id);

            loaded.Should().NotBeNull();
            loaded!.BusinessId.Should().Be(realBiz); // 
        }

       
    }
}
