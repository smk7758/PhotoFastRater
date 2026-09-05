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

    [Fact]
    public async Task CatalogUsesFtsAndStableCursorWithoutOffset()
    {
        Directory.CreateDirectory(_testDirectory);
        var options = new DbContextOptionsBuilder<PhotoDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_testDirectory, "search.db")}")
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var context = factory.CreateDbContext())
            await context.Database.MigrateAsync();

        var repository = new PhotoRepository(factory);
        var photos = Enumerable.Range(0, 300)
            .Select(index => CreateSearchPhoto(index))
            .ToArray();
        await repository.UpsertBatchAsync(photos[..150]);
        await repository.UpsertBatchAsync(photos[150..]);

        var first = await repository.SearchAsync(new PhotoSearchQuery("mountain"), null, 256);
        var second = await repository.SearchAsync(new PhotoSearchQuery("mountain"), first.NextCursor, 256);

        first.TotalCount.Should().Be(300);
        first.Items.Should().HaveCount(256);
        second.Items.Should().HaveCount(44);
        first.Items.Select(photo => photo.Id).Should().NotIntersectWith(second.Items.Select(photo => photo.Id));
        var all = first.Items.Concat(second.Items).ToArray();
        all.Select(photo => photo.Id).Should().Equal(
            all.OrderByDescending(photo => photo.DateTakenUtc).ThenByDescending(photo => photo.Id).Select(photo => photo.Id));
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

    private Photo CreateSearchPhoto(int index)
    {
        var path = Path.Combine(_testDirectory, $"mountain-{index:D4}.jpg");
        return new Photo
        {
            FilePath = path,
            FileName = Path.GetFileName(path),
            FolderPath = _testDirectory,
            FolderName = Path.GetFileName(_testDirectory),
            DateTaken = DateTime.UtcNow.AddSeconds(-index / 2),
            ImportDate = DateTime.UtcNow
        };
    }

    private sealed class TestDbContextFactory(DbContextOptions<PhotoDbContext> options)
        : IDbContextFactory<PhotoDbContext>
    {
        public PhotoDbContext CreateDbContext() => new(options);
    }
}
