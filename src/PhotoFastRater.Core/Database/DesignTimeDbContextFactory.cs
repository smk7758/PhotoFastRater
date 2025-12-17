using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PhotoFastRater.Core.Database;

/// <summary>
/// Design-time factory for PhotoDbContext (for EF Core migrations)
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PhotoDbContext>
{
    public PhotoDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PhotoDbContext>();

        // Use a temporary SQLite database for migrations
        optionsBuilder.UseSqlite("Data Source=temp_migrations.db");

        return new PhotoDbContext(optionsBuilder.Options);
    }
}
