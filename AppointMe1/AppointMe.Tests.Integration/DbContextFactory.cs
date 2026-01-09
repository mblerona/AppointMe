using AppointMe.Repository.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AppointMe.Tests.Integration
{
    public static class DbContextFactory
    {
        public static ApplicationDbContext Create()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"integration-db-{System.Guid.NewGuid()}")
                .Options;

            return new ApplicationDbContext(options);
        }
    }
}