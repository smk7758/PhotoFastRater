using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using PhotoFastRater.Infrastructure.Database;
using Xunit;

namespace PhotoFastRater.Tests.Database;

/// <summary>Exercises every released schema boundary rather than treating a fresh catalog as upgrade evidence.</summary>
public sealed class UpgradeCompatibilityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PhotoFastRater.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("20251216163207_AddFolderPathAndName")]
    [InlineData("20251219133912_AddCustomPositionToExportTemplate")]
    [InlineData("20260905000100_AddDurableRatingState")]
    [InlineData("20260905000200_AddPhotoSearchIndex")]
    [InlineData("20260905000300_AddLibraryOrganization")]
    public async Task UpgradeRetainsOldRowsAndBackupCanBeReopened(string target)
    {
        var factory = CreateFactory("legacy.db");
        await using (var context = factory.CreateDbContext())
        {
            await context.GetService<IMigrator>().MigrateAsync(target);
            await SeedHistoricalRowAsync(context);
        }
        var initializer = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitializeAsync();
        await initializer.InitializeAsync();
        await using (var verification = factory.CreateDbContext())
        {
            var photo = await verification.Photos.SingleAsync();
            photo.FileName.Should().Be("旧写真.jpg");
            photo.Rating.Should().Be(4);
            photo.IsFavorite.Should().BeTrue();
            (await verification.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        }
        var backups = Directory.Exists(Path.Combine(_root, "Backups"))
            ? Directory.GetFiles(Path.Combine(_root, "Backups"), "*.db") : [];
        if (target == "20260905000300_AddLibraryOrganization")
        {
            backups.Should().BeEmpty("an up-to-date DB needs no migration backup");
            return;
        }
        backups.Should().ContainSingle("repeat startup must not perform the same migration twice");
        var restored = Path.Combine(_root, "restored.db");
        File.Copy(backups[0], restored);
        await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = restored }.ToString());
        await backup.OpenAsync();
        await using var command = backup.CreateCommand();
        command.CommandText = "SELECT Rating FROM Photos WHERE FileName='旧写真.jpg'";
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(4);
        var restoredFactory = CreateFactory("restored.db");
        await new DatabaseInitializer(restoredFactory, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        await using var upgradedBackup = restoredFactory.CreateDbContext();
        (await upgradedBackup.Photos.SingleAsync()).Rating.Should().Be(4);
    }

    [Fact]
    public async Task BackupFailureStopsMigrationAndLeavesHistoricalRowsIntact()
    {
        var factory = CreateFactory("blocked.db");
        await using (var context = factory.CreateDbContext())
        {
            await context.GetService<IMigrator>().MigrateAsync("20251216163207_AddFolderPathAndName");
            await SeedHistoricalRowAsync(context);
        }
        await File.WriteAllTextAsync(Path.Combine(_root, "Backups"), "a file intentionally prevents creating the backup directory");
        var initializer = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance);
        await FluentActions.Awaiting(() => initializer.InitializeAsync()).Should().ThrowAsync<IOException>();
        await using var contextAfter = factory.CreateDbContext();
        (await contextAfter.Database.GetAppliedMigrationsAsync()).Should().ContainSingle();
        await using var command = contextAfter.Database.GetDbConnection().CreateCommand();
        await contextAfter.Database.OpenConnectionAsync();
        command.CommandText = "SELECT Rating FROM Photos";
        Convert.ToInt32(await command.ExecuteScalarAsync()).Should().Be(4);
    }

    private Factory CreateFactory(string file)
    {
        Directory.CreateDirectory(_root);
        return new Factory(new DbContextOptionsBuilder<PhotoDbContext>().UseSqlite(
            new SqliteConnectionStringBuilder { DataSource = Path.Combine(_root, file) }.ToString()).Options);
    }

    private static async Task SeedHistoricalRowAsync(PhotoDbContext context)
    {
        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();
        await using var schema = connection.CreateCommand();
        schema.CommandText = "PRAGMA table_info(Photos)";
        var required = new List<(string Name, string Type)>();
        await using (var reader = await schema.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                if (reader.GetInt32(3) == 1 && reader.GetString(1) != "Id" && reader.IsDBNull(4))
                    required.Add((reader.GetString(1), reader.GetString(2)));
        await using var insert = connection.CreateCommand();
        var names = required.Select(item => '"' + item.Name + '"');
        insert.CommandText = $"INSERT INTO Photos ({string.Join(',', names)}) VALUES ({string.Join(',', required.Select((_, index) => "$p" + index))})";
        for (var index = 0; index < required.Count; index++)
        {
            var (name, type) = required[index];
            object value = name switch
            {
                "FileName" => "旧写真.jpg",
                "FilePath" => @"C:\OldPhotos\旧写真.jpg",
                "Rating" => 4,
                "IsFavorite" => 1,
                "DateTaken" or "ImportDate" or "DateTakenUtc" or "RatingModifiedUtc" => "2025-12-16 12:00:00",
                _ => type == "TEXT" ? string.Empty : 0
            };
            var parameter = insert.CreateParameter(); parameter.ParameterName = "$p" + index; parameter.Value = value;
            insert.Parameters.Add(parameter);
        }
        await insert.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            var garbage = Path.Combine(Path.GetTempPath(), "PhotoFastRater.Tests", "_GARBAGE"); Directory.CreateDirectory(garbage);
            Directory.Move(_root, Path.Combine(garbage, Path.GetFileName(_root)));
        }
        GC.SuppressFinalize(this);
    }

    private sealed class Factory(DbContextOptions<PhotoDbContext> options) : IDbContextFactory<PhotoDbContext>
    {
        public PhotoDbContext CreateDbContext() => new(options);
    }
}
