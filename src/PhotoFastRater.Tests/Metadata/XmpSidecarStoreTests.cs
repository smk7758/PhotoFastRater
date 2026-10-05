using System.IO;
using System.Text;
using FluentAssertions;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Infrastructure.Metadata;
using Xunit;

namespace PhotoFastRater.Tests.Metadata;

public sealed class XmpSidecarStoreTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(Path.GetTempPath(), "PhotoFastRater.Tests", Guid.NewGuid().ToString("N"));

    public XmpSidecarStoreTests() => Directory.CreateDirectory(_testDirectory);

    [Fact]
    public async Task WriteAsync_PreservesUnknownMetadataAndUsesFullFileName()
    {
        var photoPath = Path.Combine(_testDirectory, "portrait.raw");
        var sidecarPath = photoPath + ".xmp";
        await File.WriteAllTextAsync(sidecarPath,
            "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description xmlns:custom=\"urn:test\" custom:Keep=\"yes\" /></rdf:RDF></x:xmpmeta>");
        var store = new XmpSidecarStore();
        var expected = new XmpRatingDocument(new RatingState(4, true, false), DateTime.UtcNow, 7, "PhotoFastRater");

        await store.WriteAsync(photoPath, expected);
        var actualXml = await File.ReadAllTextAsync(sidecarPath);
        var actual = await store.ReadAsync(photoPath);

        actualXml.Should().Contain("custom:Keep=\"yes\"");
        actual.Should().NotBeNull();
        actual!.Rating.Should().Be(expected.Rating);
        actual.Revision.Should().Be(7);
        XmpSidecarStore.GetSidecarPath(photoPath).Should().EndWith("portrait.raw.xmp");
    }

    [Fact]
    public async Task EmbeddedWriter_InvalidJpegDoesNotChangeSourceBytes()
    {
        var photoPath = Path.Combine(_testDirectory, "invalid.jpg");
        var original = Encoding.UTF8.GetBytes("not a jpeg");
        await File.WriteAllBytesAsync(photoPath, original);
        var writer = new EmbeddedMetadataWriter();

        var result = await writer.WriteRatingAsync(photoPath, new RatingState(3, false, false));

        result.Succeeded.Should().BeFalse();
        (await File.ReadAllBytesAsync(photoPath)).Should().Equal(original);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
