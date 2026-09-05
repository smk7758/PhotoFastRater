using System.IO;
using FluentAssertions;
using PhotoFastRater.Core.UI;
using PhotoFastRater.Infrastructure.Cache;
using PhotoFastRater.UI.Services;
using Xunit;

namespace PhotoFastRater.Tests.Services;

public sealed class UserSettingsStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(), "PhotoFastRater.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveThenLoadRestoresValidatedSnapshot()
    {
        var path = Path.Combine(_testDirectory, "user-settings.json");
        var store = new UserSettingsStore(path);
        var writtenCache = CreateCache();
        writtenCache.MaxDiskCacheSizeGB = 42;
        var writtenUi = new UIConfiguration
        {
            GridThumbnailSize = 320,
            EnableGPUAcceleration = false,
            ArrowKeyNavigationMode = "SelectionOnly"
        };

        await store.SaveAsync(writtenCache, writtenUi);
        var loadedCache = CreateCache();
        var loadedUi = new UIConfiguration();

        store.TryLoad(loadedCache, loadedUi).Should().BeTrue();
        loadedCache.MaxDiskCacheSizeGB.Should().Be(42);
        loadedUi.GridThumbnailSize.Should().Be(320);
        loadedUi.EnableGPUAcceleration.Should().BeFalse();
        loadedUi.ArrowKeyNavigationMode.Should().Be("SelectionOnly");
    }

    [Fact]
    public void MalformedJsonDoesNotMutateDefaults()
    {
        Directory.CreateDirectory(_testDirectory);
        var path = Path.Combine(_testDirectory, "user-settings.json");
        File.WriteAllText(path, "{not-json");
        var cache = CreateCache();
        var ui = new UIConfiguration { GridThumbnailSize = 256 };

        new UserSettingsStore(path).TryLoad(cache, ui).Should().BeFalse();

        cache.MaxDiskCacheSizeGB.Should().Be(10);
        ui.GridThumbnailSize.Should().Be(256);
    }

    private CacheConfiguration CreateCache() => new()
    {
        CachePath = Path.Combine(_testDirectory, "cache"),
        MaxMemoryCacheSizeMB = 128,
        MaxDiskCacheSizeGB = 10,
        ThumbnailSize = 256,
        JpegQuality = 85,
        MaxParallelGenerations = 4,
        EnableRAWSupport = true
    };

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
