using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Data.Sqlite;
using System.Data;

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
        var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pendingMigrations.Any())
            await BackupExistingDatabaseAsync(context, cancellationToken);

        await context.Database.MigrateAsync(cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA synchronous=NORMAL;", cancellationToken);
        await context.Database.ExecuteSqlRawAsync("PRAGMA busy_timeout=5000;", cancellationToken);
        _logger.LogInformation("Photo catalog database is ready");
    }

    private static async Task BackupExistingDatabaseAsync(PhotoDbContext context, CancellationToken cancellationToken)
    {
        var sourceConnection = (SqliteConnection)context.Database.GetDbConnection();
        var databasePath = sourceConnection.DataSource;
        if (string.IsNullOrWhiteSpace(databasePath) || !File.Exists(databasePath))
            return;

        var backupDirectory = Path.Combine(Path.GetDirectoryName(databasePath)!, "Backups");
        Directory.CreateDirectory(backupDirectory);
        var backupPath = Path.Combine(
            backupDirectory,
            $"photos-before-migration-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.db");

        if (sourceConnection.State != ConnectionState.Open)
            await sourceConnection.OpenAsync(cancellationToken);
        await using var backupConnection = new SqliteConnection($"Data Source={backupPath};Mode=ReadWriteCreate");
        await backupConnection.OpenAsync(cancellationToken);
        sourceConnection.BackupDatabase(backupConnection);
    }
}
