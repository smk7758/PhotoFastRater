using System.IO;
using FluentAssertions;
using PhotoFastRater.Infrastructure.Cache;
using PhotoFastRater.Infrastructure.ImageProcessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PhotoFastRater.Tests.Cache;

public sealed class ThumbnailCacheManagerTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(), "PhotoFastRater.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConcurrentRequestsCoalesceAndSourceStampInvalidatesDiskKey()
    {
        Directory.CreateDirectory(_testDirectory);
        var sourcePath = Path.Combine(_testDirectory, "source.bmp");
        using (var image = new Image<Rgba32>(16, 16, Color.CornflowerBlue))
            await image.SaveAsBmpAsync(sourcePath);
        var cachePath = Path.Combine(_testDirectory, "cache");
        using var cache = new ThumbnailCacheManager(
            new CacheConfiguration
            {
                CachePath = cachePath,
                ThumbnailSize = 8,
                JpegQuality = 80,
                MaxMemoryCacheSizeMB = 64,
                MaxDiskCacheSizeGB = 1
            },
            new JpegThumbnailGenerator(80),
            new RawThumbnailGenerator(80));

        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => cache.GetThumbnailAsync(sourcePath)));

        results.Should().OnlyContain(bytes => bytes.Length > 0);
        results.Skip(1).Should().OnlyContain(bytes => bytes.SequenceEqual(results[0]));
        var firstFiles = Directory.GetFiles(cachePath, "*.jpg", SearchOption.AllDirectories);
        firstFiles.Should().ContainSingle();
        Path.GetRelativePath(cachePath, firstFiles[0]).Split(Path.DirectorySeparatorChar).Should().HaveCount(3);

        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddSeconds(5));
        (await cache.GetThumbnailAsync(sourcePath)).Should().NotBeEmpty();
        Directory.GetFiles(cachePath, "*.jpg", SearchOption.AllDirectories).Should().HaveCount(2);
    }

    [Fact]
    public async Task ClearMovesOnlyGeneratedThumbnailsToRecoverableGarbage()
    {
        Directory.CreateDirectory(_testDirectory);
        var sourcePath = Path.Combine(_testDirectory, "source.bmp");
        using (var image = new Image<Rgba32>(16, 16, Color.CornflowerBlue))
            await image.SaveAsBmpAsync(sourcePath);
        var cachePath = Path.Combine(_testDirectory, "cache");
        using var cache = new ThumbnailCacheManager(
            new CacheConfiguration
            {
                CachePath = cachePath,
                ThumbnailSize = 8,
                JpegQuality = 80,
                MaxMemoryCacheSizeMB = 64,
                MaxDiskCacheSizeGB = 1
            },
            new JpegThumbnailGenerator(80),
            new RawThumbnailGenerator(80));
        await cache.GetThumbnailAsync(sourcePath);

        (await cache.ClearAsync()).Should().Be(1);

        Directory.GetFiles(cachePath, "*.jpg", SearchOption.AllDirectories).Should().BeEmpty();
        Directory.GetFiles(Path.Combine(_testDirectory, "_GARBAGE"), "*.jpg", SearchOption.AllDirectories)
            .Should().ContainSingle();
        File.Exists(sourcePath).Should().BeTrue();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
