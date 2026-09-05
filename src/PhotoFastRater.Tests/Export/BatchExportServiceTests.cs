using System.Security.Cryptography;
using System.IO;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Database;
using PhotoFastRater.Infrastructure.Database.Repositories;
using PhotoFastRater.Infrastructure.Export;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PhotoFastRater.Tests.Export;

public sealed class BatchExportServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(), "PhotoFastRater.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CropCollisionAndPartialFailureLeaveSourceUnchanged()
    {
        Directory.CreateDirectory(_testDirectory);
        var sourcePath = Path.Combine(_testDirectory, "source.png");
        using (var source = new Image<Rgba32>(200, 100, Color.CornflowerBlue))
            await source.SaveAsPngAsync(sourcePath);
        var sourceHash = await HashAsync(sourcePath);
        var factory = await CreateFactoryAsync();
        var repository = new PhotoRepository(factory);
        var valid = await repository.AddAsync(CreatePhoto(sourcePath));
        var missing = await repository.AddAsync(CreatePhoto(Path.Combine(_testDirectory, "missing.png"), true));
        var output = Path.Combine(_testDirectory, "output");
        var recipe = new ExportRecipe(output, "{name}", "PNG", 90,
            new CropRect(0.25, 0, 0.5, 1), 0, 50, 50, true);
        var service = new BatchExportService(repository);

        var first = await service.ExportAsync([valid.Id, missing.Id], recipe);
        var second = await service.ExportAsync([valid.Id], recipe);

        first.Should().HaveCount(2);
        first[0].Error.Should().BeNull();
        first[1].Error.Should().Contain("見つかりません");
        Path.GetFileName(first[0].OutputPath).Should().Be("source.png");
        Path.GetFileName(second[0].OutputPath).Should().Be("source-2.png");
        using var exported = await Image.LoadAsync(first[0].OutputPath!);
        exported.Size.Should().Be(new Size(50, 50));
        (await HashAsync(sourcePath)).Should().Equal(sourceHash);
    }

    [Fact]
    public async Task CancellationBeforeDecodeCreatesNoOutput()
    {
        Directory.CreateDirectory(_testDirectory);
        var sourcePath = Path.Combine(_testDirectory, "source.png");
        using (var source = new Image<Rgba32>(10, 10, Color.Black))
            await source.SaveAsPngAsync(sourcePath);
        var factory = await CreateFactoryAsync();
        var repository = new PhotoRepository(factory);
        var photo = await repository.AddAsync(CreatePhoto(sourcePath));
        var output = Path.Combine(_testDirectory, "cancelled-output");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => new BatchExportService(repository).ExportAsync(
            [photo.Id], new ExportRecipe(output, "{name}", "JPEG", 90, null, 0, null, null, true),
            cancellationToken: cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        Directory.Exists(output).Should().BeTrue();
        Directory.GetFiles(output).Should().BeEmpty();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, true);
        GC.SuppressFinalize(this);
    }

    private async Task<TestDbContextFactory> CreateFactoryAsync()
    {
        var options = new DbContextOptionsBuilder<PhotoDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_testDirectory, "catalog.db")}")
            .Options;
        var factory = new TestDbContextFactory(options);
        await using var context = factory.CreateDbContext();
        await context.Database.EnsureCreatedAsync();
        return factory;
    }

    private Photo CreatePhoto(string path, bool missing = false) => new()
    {
        FilePath = path,
        FileName = Path.GetFileName(path),
        FolderPath = _testDirectory,
        FolderName = Path.GetFileName(_testDirectory),
        DateTaken = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc),
        ImportDate = DateTime.UtcNow,
        IsMissing = missing
    };

    private static async Task<byte[]> HashAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream);
    }

    private sealed class TestDbContextFactory(DbContextOptions<PhotoDbContext> options)
        : IDbContextFactory<PhotoDbContext>
    {
        public PhotoDbContext CreateDbContext() => new(options);
    }
}
