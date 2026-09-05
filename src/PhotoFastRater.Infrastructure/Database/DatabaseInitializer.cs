using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace PhotoFastRater.Infrastructure.Database;

/// <summary>Initializes the catalog once after the final dependency container has been built.</summary>
public sealed class DatabaseInitializer
{
    private readonly IDbContextFactory<PhotoDbContext> _contextFactory;
    private readonly ILogger<DatabaseInitializer> _logger;

    /// <summary>Initializes the database startup operation.</summary>
    public DatabaseInitializer(
        IDbContextFactory<PhotoDbContext> contextFactory,
        ILogger<DatabaseInitializer> logger)
    {
        _contextFactory = contextFactory;
        _logger = logger;
    }

    /// <summary>Applies pending migrations with a short-lived context.</summary>
    /// <remarks>This method performs database I/O proportional to the pending migrations.</remarks>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Initializing photo catalog database");
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await context.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("Photo catalog database is ready");
    }
}
