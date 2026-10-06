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
    public async Task RawJpegImportedInSeparateBatchesBecomeLinkedWithoutLinkingPng()
    {
        Directory.CreateDirectory(_testDirectory);
        var options = new DbContextOptionsBuilder<PhotoDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_testDirectory, "pair-import.db")}").Options;
        var factory = new TestDbContextFactory(options);
        await using (var context = factory.CreateDbContext())
            await context.Database.MigrateAsync();
        var repository = new PhotoRepository(factory);
        Photo Incoming(string extension) => new() { FilePath = Path.Combine(_testDirectory, "shot" + extension), FileName = "shot" + extension };
        await repository.UpsertBatchAsync([Incoming(".cr3")]);
        await repository.UpsertBatchAsync([Incoming(".png"), Incoming(".jpg")]);
        var rows = await repository.GetAllAsync();
        var raw = rows.Single(photo => photo.FileName.EndsWith(".cr3", StringComparison.Ordinal));
        var jpeg = rows.Single(photo => photo.FileName.EndsWith(".jpg", StringComparison.Ordinal));
        raw.PairId.Should().NotBeNull();
        jpeg.PairId.Should().Be(raw.PairId);
        rows.Single(photo => photo.FileName.EndsWith(".png", StringComparison.Ordinal)).PairId.Should().BeNull();
        await repository.CommitRatingAsync(jpeg.Id, new RatingState(2, true, false));
        (await repository.GetByIdAsync(raw.Id))!.Rating.Should().Be(2);
    }

    [Fact]
    public async Task ConfirmedSessionTransferKeepsLowerRatingsAndFlagOnlyEditsAndIsIdempotent()
    {
        Directory.CreateDirectory(_testDirectory);
        var options = new DbContextOptionsBuilder<PhotoDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_testDirectory, "transfer.db")}").Options;
        var factory = new TestDbContextFactory(options);
        await using (var context = factory.CreateDbContext())
            await context.Database.MigrateAsync();
        var repository = new PhotoRepository(factory);
        var first = CreatePhoto(Path.Combine(_testDirectory, "lower.jpg"), Guid.NewGuid());
        var second = CreatePhoto(Path.Combine(_testDirectory, "flag.jpg"), Guid.NewGuid());
        first.Rating = 5;
        await repository.AddAsync(first);
        await repository.AddAsync(second);
        first.Rating = 1;
        second.IsFavorite = true;
        second.IsRejected = true;
        var result = await repository.TransferSessionBatchAsync([first, second]);
        result.Should().Be((0, 2, 0));
        (await repository.TransferSessionBatchAsync([first, second])).Should().Be((0, 0, 2));
        var restored = await repository.GetByIdAsync(first.Id);
        restored!.Rating.Should().Be(1);
        restored.RatingRevision.Should().Be(1);
        restored.MetadataSyncStatus.Should().Be(MetadataSyncStatus.Pending);
        var flags = await repository.GetByIdAsync(second.Id);
        flags!.IsFavorite.Should().BeTrue();
        flags.IsRejected.Should().BeTrue();
    }

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

    [Fact]
    public async Task TagsAreIdempotentSearchableAndCollectionsRemainHierarchical()
    {
        Directory.CreateDirectory(_testDirectory);
        var options = new DbContextOptionsBuilder<PhotoDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_testDirectory, "organization.db")}")
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var context = factory.CreateDbContext())
            await context.Database.MigrateAsync();
        var photos = new PhotoRepository(factory);
        await photos.UpsertBatchAsync([CreateSearchPhoto(1), CreateSearchPhoto(2)]);
        var organization = new LibraryOrganizationRepository(factory);

        await organization.AddTagAsync("Landscape", [1, 2]);
        await organization.AddTagAsync("landscape", [1, 2]);
        var root = await organization.CreateCollectionAsync("Portfolio", null);
        var child = await organization.CreateCollectionAsync("Print", root.Id);
        await organization.AddToCollectionAsync(child.Id, [1, 2]);
        await organization.AddToCollectionAsync(child.Id, [1, 2]);

        (await photos.SearchAsync(new PhotoSearchQuery("landscape"), null, 256)).TotalCount.Should().Be(2);
        await using var verification = factory.CreateDbContext();
        (await verification.PhotoTags.CountAsync()).Should().Be(1);
        (await verification.PhotoTagMappings.CountAsync()).Should().Be(2);
        (await verification.PhotoCollectionMappings.CountAsync()).Should().Be(2);
        (await verification.PhotoCollections.SingleAsync(item => item.Id == child.Id)).ParentId.Should().Be(root.Id);
    }

    [Fact]
    public async Task MissingOnlyFilterExcludesPresentPhotos()
    {
        Directory.CreateDirectory(_testDirectory);
        var options = new DbContextOptionsBuilder<PhotoDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_testDirectory, "missing.db")}")
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
            var present = CreateSearchPhoto(1);
            var missing = CreateSearchPhoto(2);
            missing.IsMissing = true;
            context.Photos.AddRange(present, missing);
            await context.SaveChangesAsync();
        }

        var result = await new PhotoRepository(factory).SearchAsync(
            new PhotoSearchQuery(IncludeMissing: true, MissingOnly: true),
            null,
            256);

        result.Items.Should().ContainSingle().Which.IsMissing.Should().BeTrue();
    }

    [Fact]
    public async Task AutoEventPreviewDoesNotWriteAndConfirmationIsIdempotent()
    {
        Directory.CreateDirectory(_testDirectory);
        var options = new DbContextOptionsBuilder<PhotoDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_testDirectory, "events.db")}")
            .Options;
        var factory = new TestDbContextFactory(options);
        await using (var context = factory.CreateDbContext())
        {
            await context.Database.MigrateAsync();
            context.Photos.AddRange(CreateSearchPhoto(1), CreateSearchPhoto(2));
            await context.SaveChangesAsync();
        }
        var events = new EventRepository(factory);
        var service = new PhotoFastRater.Infrastructure.Services.EventManagementService(events, new PhotoRepository(factory));
        List<Photo> photos;
        await using (var context = factory.CreateDbContext())
            photos = await context.Photos.AsNoTracking().OrderBy(photo => photo.Id).ToListAsync();

        var candidates = service.PreviewAutoGroups(photos, TimeSpan.FromHours(2));
        (await events.GetAllAsync()).Should().BeEmpty();
        (await service.ConfirmAutoGroupsAsync(candidates)).Should().Be(1);
        (await service.ConfirmAutoGroupsAsync(candidates)).Should().Be(0);
        (await events.GetAllAsync()).Should().ContainSingle();
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
