using System.IO;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Database;
using PhotoFastRater.Infrastructure.Database.Repositories;
using Xunit;

namespace PhotoFastRater.Tests.Database;

public sealed class RatingPersistenceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(), "PhotoFastRater.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MigrationAndLinkedRatingCommitPreserveOneTransactionalState()
    {
        Directory.CreateDirectory(_testDirectory);
        var options = new DbContextOptionsBuilder<PhotoDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_testDirectory, "catalog.db")}")
            .Options;
        var factory = new TestDbContextFactory(options);

        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();

            var pairId = Guid.NewGuid();
            context.Photos.AddRange(
                CreatePhoto(Path.Combine(_testDirectory, "shot.raw"), pairId),
                CreatePhoto(Path.Combine(_testDirectory, "shot.jpg"), pairId));
            await context.SaveChangesAsync();
        }

        var repository = new PhotoRepository(factory);
        await repository.CommitRatingAsync(1, new RatingState(5, true, false));

        await using var verification = factory.CreateDbContext();
        var pair = await verification.Photos.AsNoTracking().OrderBy(photo => photo.Id).ToListAsync();
        pair.Should().HaveCount(2);
        pair.Should().OnlyContain(photo =>
            photo.Rating == 5 &&
            photo.IsFavorite &&
            photo.RatingRevision == 1 &&
            photo.MetadataSyncStatus == MetadataSyncStatus.Pending);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static Photo CreatePhoto(string path, Guid pairId) => new()
    {
        FilePath = path,
        FileName = Path.GetFileName(path),
        FolderPath = Path.GetDirectoryName(path)!,
        FolderName = Path.GetFileName(Path.GetDirectoryName(path)) ?? string.Empty,
        DateTaken = DateTime.UtcNow,
        ImportDate = DateTime.UtcNow,
        PairId = pairId,
        PairLinkMode = PairLinkMode.Linked
    };

    private sealed class TestDbContextFactory(DbContextOptions<PhotoDbContext> options)
        : IDbContextFactory<PhotoDbContext>
    {
        public PhotoDbContext CreateDbContext() => new(options);
    }
}
