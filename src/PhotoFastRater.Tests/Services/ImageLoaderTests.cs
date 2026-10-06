using System.IO;
using FluentAssertions;
using PhotoFastRater.Infrastructure.Cache;
using PhotoFastRater.Infrastructure.ImageProcessing;
using PhotoFastRater.UI.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PhotoFastRater.Tests.Services;

/// <summary>Malformed or missing photos must not permanently consume the bounded image workers.</summary>
public sealed class ImageLoaderTests
{
    [Fact]
    public async Task MissingAndCorruptRequestsFailIndividuallyAndAllWorkersRemainAvailable()
    {
        var root = Path.Combine(Path.GetTempPath(), "PhotoFastRater.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var valid = Path.Combine(root, "valid.jpg");
            using (var image = new Image<Rgba32>(16, 16, Color.CornflowerBlue))
                await image.SaveAsJpegAsync(valid);
            var corrupt = Path.Combine(root, "broken.jpg");
            await File.WriteAllTextAsync(corrupt, "not an image");
            using var cache = new ThumbnailCacheManager(new CacheConfiguration
            {
                CachePath = Path.Combine(root, "cache"),
                ThumbnailSize = 8,
                JpegQuality = 80,
                MaxMemoryCacheSizeMB = 64,
                MaxDiskCacheSizeGB = 1
            }, new JpegThumbnailGenerator(80), new RawThumbnailGenerator(80));
            using var loader = new ImageLoader(cache);
            for (var index = 0; index < 8; index++)
            {
                var source = index % 2 == 0 ? Path.Combine(root, "missing.jpg") : corrupt;
                await FluentActions.Awaiting(() => loader.LoadAsync(source).WaitAsync(TimeSpan.FromSeconds(5)))
                    .Should().ThrowAsync<InvalidOperationException>();
            }
            var thumbnail = await loader.LoadAsync(valid).WaitAsync(TimeSpan.FromSeconds(5));
            thumbnail.Should().NotBeNull();
            thumbnail!.IsFrozen.Should().BeTrue();
            thumbnail.PixelWidth.Should().Be(8);
            var pixels = new byte[8 * 8 * 4];
            thumbnail.CopyPixels(pixels, 8 * 4, 0);
            pixels[0].Should().BeInRange(230, 242); // B,G,R,A survive detached BGRA caching and JPEG quantization.
            pixels[1].Should().BeInRange(142, 156);
            pixels[2].Should().BeInRange(93, 107);
            pixels[3].Should().Be(255);
        }
        finally
        {
            var garbage = Path.Combine(Path.GetTempPath(), "PhotoFastRater.Tests", "_GARBAGE");
            Directory.CreateDirectory(garbage);
            Directory.Move(root, Path.Combine(garbage, Path.GetFileName(root)));
        }
    }
}
