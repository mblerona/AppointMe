using AppointMe.Repository.Data;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace AppointMe.Tests.Integration.SqlServer
{
    /// <summary>
    /// Fails when the EF model (ApplicationDbContext) was changed without adding a migration, i.e. when
    /// "dotnet ef migrations add" would produce a non-empty migration. Needs no database connection.
    /// </summary>
    public class ModelSnapshotTests
    {
        [Fact]
        public void Model_MatchesTheLatestMigrationSnapshot()
        {
            // The SQL Server provider builds the relational model; the connection is never opened.
            using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=NotUsed;Trusted_Connection=True")
                .Options);

#pragma warning disable EF1001 // Internal EF Core API: the documented way to compare a snapshot with the model.
            var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot;
            snapshot.Should().NotBeNull();

            IModel snapshotModel = snapshot!.Model;
            if (snapshotModel is IMutableModel mutable)
                snapshotModel = mutable.FinalizeModel();
            snapshotModel = db.GetService<IModelRuntimeInitializer>().Initialize(snapshotModel, designTime: true, validationLogger: null);

            var differences = db.GetService<IMigrationsModelDiffer>().GetDifferences(
                snapshotModel.GetRelationalModel(),
                db.GetService<IDesignTimeModel>().Model.GetRelationalModel());
#pragma warning restore EF1001

            differences.Select(d => d.GetType().Name).Should().BeEmpty(
                "every model change needs a migration (dotnet ef migrations add <Name> --project AppointMe.Repository --startup-project AppointMe.Web)");
        }
    }
}
