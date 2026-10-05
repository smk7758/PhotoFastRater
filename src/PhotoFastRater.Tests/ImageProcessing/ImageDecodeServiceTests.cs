using System.IO;
using FluentAssertions;
using PhotoFastRater.Infrastructure.ImageProcessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PhotoFastRater.Tests.ImageProcessing;

public sealed class ImageDecodeServiceTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(), "PhotoFastRater.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DecodeBoundsLongEdgeAndLeavesSourceUnchanged()
    {
        Directory.CreateDirectory(_testDirectory);
        var sourcePath = Path.Combine(_testDirectory, "wide.png");
        using (var source = new Image<Rgba32>(1200, 600, Color.CornflowerBlue))
            await source.SaveAsPngAsync(sourcePath);
        var originalBytes = await File.ReadAllBytesAsync(sourcePath);
        var service = new ImageDecodeService(new RawThumbnailGenerator(85));

        var encoded = await service.DecodeAsync(sourcePath, 300);

        using var decoded = Image.Load(encoded.Span);
        decoded.Width.Should().Be(300);
        decoded.Height.Should().Be(150);
        (await File.ReadAllBytesAsync(sourcePath)).Should().Equal(originalBytes);
    }

    [Theory]
    [InlineData(255)]
    [InlineData(4097)]
    public async Task DecodeRejectsUnboundedDimensions(int dimension)
    {
        var service = new ImageDecodeService(new RawThumbnailGenerator(85));
        var action = () => service.DecodeAsync("missing.jpg", dimension);
        await action.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
