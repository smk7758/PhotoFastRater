using System.IO;
using FluentAssertions;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Export;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace PhotoFastRater.Tests.Export;

/// <summary>Protects originals and previous exports across every single-image entry point.</summary>
public sealed class SocialMediaSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PhotoFastRater.Tests", Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData(SocialMediaPlatform.Instagram)]
    [InlineData(SocialMediaPlatform.Twitter)]
    [InlineData(SocialMediaPlatform.Facebook)]
    public async Task ExistingDestinationAndOriginalRemainUntouched(SocialMediaPlatform platform)
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "source.jpg");
        using (var image = new Image<Rgb24>(80, 60)) await image.SaveAsJpegAsync(source);
        var original = await File.ReadAllBytesAsync(source);
        var output = Path.Combine(_root, "export.jpg");
        await File.WriteAllTextAsync(output, "existing user file");
        var exporter = new SocialMediaExporter();
        var result = await exporter.ExportAsync(new Photo { FilePath = source }, new ExportTemplate
        {
            TargetPlatform = platform,
            EnableFrame = false,
            EnableExifOverlay = false
        }, output);
        result.Should().NotBe(output);
        (await File.ReadAllTextAsync(output)).Should().Be("existing user file");
        (await File.ReadAllBytesAsync(source)).Should().Equal(original);
        using var verification = await Image.LoadAsync(result);
        verification.Width.Should().BeGreaterThan(0);
        Directory.GetFiles(_root, "*.tmp").Should().BeEmpty();
    }

    [Fact]
    public async Task OriginalAsDestinationIsRejectedBeforeAnyWrite()
    {
        Directory.CreateDirectory(_root);
        var source = Path.Combine(_root, "original.jpg");
        await File.WriteAllTextAsync(source, "original source bytes");
        await FluentActions.Awaiting(() => new SocialMediaExporter().ExportAsync(
            new Photo { FilePath = source }, new ExportTemplate(), source)).Should().ThrowAsync<ArgumentException>();
        (await File.ReadAllTextAsync(source)).Should().Be("original source bytes");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            var garbage = Path.Combine(Path.GetTempPath(), "PhotoFastRater.Tests", "_GARBAGE"); Directory.CreateDirectory(garbage);
            Directory.Move(_root, Path.Combine(garbage, Path.GetFileName(_root)));
        }
        GC.SuppressFinalize(this);
    }
}
