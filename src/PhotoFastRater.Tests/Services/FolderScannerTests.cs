using System.IO;
using FluentAssertions;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Infrastructure.Services;
using Xunit;

namespace PhotoFastRater.Tests.Services;

public sealed class FolderScannerTests : IDisposable
{
    private readonly string _testDirectory = Path.Combine(
        Path.GetTempPath(), "PhotoFastRater.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ScanStreamsSupportedNestedFilesThroughSmallBoundedBuffer()
    {
        var nested = Path.Combine(_testDirectory, "nested");
        Directory.CreateDirectory(nested);
        for (var index = 0; index < 25; index++)
            await File.WriteAllBytesAsync(Path.Combine(nested, $"photo-{index:D2}.jpg"), []);
        await File.WriteAllTextAsync(Path.Combine(nested, "ignored.txt"), "not an image");
        var scanner = new FolderScanner(new ExifService());

        var results = new List<FolderScanItem>();
        await foreach (var item in scanner.ScanAsync(
            _testDirectory,
            new ScanOptions(RecurseSubdirectories: true, MetadataParallelism: 4, BufferCapacity: 2)))
        {
            results.Add(item);
        }

        results.Should().HaveCount(25);
        results.All(item => item.Photo is not null && item.Path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue();
    }

    [Fact]
    public async Task ScanObservesCancellation()
    {
        Directory.CreateDirectory(_testDirectory);
        for (var index = 0; index < 100; index++)
            await File.WriteAllBytesAsync(Path.Combine(_testDirectory, $"photo-{index:D3}.jpg"), []);
        var scanner = new FolderScanner(new ExifService());
        using var cancellation = new CancellationTokenSource();

        var action = async () =>
        {
            await foreach (var _ in scanner.ScanAsync(_testDirectory, new ScanOptions(BufferCapacity: 1), cancellation.Token))
                cancellation.Cancel();
        };

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
            Directory.Delete(_testDirectory, recursive: true);
        GC.SuppressFinalize(this);
    }
}
