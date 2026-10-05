using System.Runtime.CompilerServices;
using System.Threading.Channels;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.Infrastructure.Services;

/// <summary>Enumerates folders incrementally and bounds metadata work with backpressure.</summary>
public sealed class FolderScanner : IFolderScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff",
        ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf", ".rw2"
    };

    private readonly ExifService _exifService;

    /// <summary>Creates a scanner whose EXIF decoder is isolated behind bounded workers.</summary>
    public FolderScanner(ExifService exifService) => _exifService = exifService;

    /// <inheritdoc />
    public async IAsyncEnumerable<FolderScanItem> ScanAsync(
        string rootPath,
        ScanOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (!Directory.Exists(rootPath))
            throw new DirectoryNotFoundException(rootPath);
        if (options.BufferCapacity is < 1 or > 8192)
            throw new ArgumentOutOfRangeException(nameof(options), "Buffer capacity must be between 1 and 8192.");
        if (options.MetadataParallelism is < 1 or > 32)
            throw new ArgumentOutOfRangeException(nameof(options), "Metadata parallelism must be between 1 and 32.");

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var paths = Channel.CreateBounded<string>(CreateChannelOptions(options.BufferCapacity, singleWriter: true));
        var results = Channel.CreateBounded<FolderScanItem>(CreateChannelOptions(options.BufferCapacity, singleWriter: false));
        var producer = ProducePathsAsync(Path.GetFullPath(rootPath), options.RecurseSubdirectories, paths.Writer, results.Writer, linkedCancellation.Token);
        var workers = Enumerable.Range(0, options.MetadataParallelism)
            .Select(_ => DecodeAsync(paths.Reader, results.Writer, linkedCancellation.Token))
            .ToArray();
        var completion = CompleteAsync(producer, workers, results.Writer);

        try
        {
            await foreach (var result in results.Reader.ReadAllAsync(cancellationToken))
                yield return result;
        }
        finally
        {
            linkedCancellation.Cancel();
            await completion.ConfigureAwait(false);
        }
    }

    private static BoundedChannelOptions CreateChannelOptions(int capacity, bool singleWriter) => new(capacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = singleWriter
    };

    private static async Task ProducePathsAsync(
        string rootPath,
        bool recurse,
        ChannelWriter<string> paths,
        ChannelWriter<FolderScanItem> results,
        CancellationToken cancellationToken)
    {
        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(rootPath);
        try
        {
            while (pendingDirectories.TryPop(out var directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    foreach (var filePath in Directory.EnumerateFiles(directory))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (SupportedExtensions.Contains(Path.GetExtension(filePath)))
                            await paths.WriteAsync(filePath, cancellationToken);
                    }

                    if (recurse)
                    {
                        foreach (var child in Directory.EnumerateDirectories(directory))
                            pendingDirectories.Push(child);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    await results.WriteAsync(new FolderScanItem(null, directory, exception), cancellationToken);
                }
            }
        }
        finally
        {
            paths.TryComplete();
        }
    }

    private async Task DecodeAsync(
        ChannelReader<string> paths,
        ChannelWriter<FolderScanItem> results,
        CancellationToken cancellationToken)
    {
        await foreach (var filePath in paths.ReadAllAsync(cancellationToken))
        {
            try
            {
                var photo = _exifService.ExtractExifData(filePath);
                var file = new FileInfo(filePath);
                photo.FilePath = file.FullName;
                photo.FileName = file.Name;
                photo.FolderPath = file.DirectoryName ?? string.Empty;
                photo.FolderName = file.Directory?.Name ?? string.Empty;
                photo.FileSize = file.Length;
                photo.FileModifiedUtc = file.LastWriteTimeUtc;
                await results.WriteAsync(new FolderScanItem(photo, filePath, null), cancellationToken);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                await results.WriteAsync(new FolderScanItem(null, filePath, exception), cancellationToken);
            }
        }
    }

    private static async Task CompleteAsync(Task producer, Task[] workers, ChannelWriter<FolderScanItem> results)
    {
        Exception? failure = null;
        try
        {
            await producer.ConfigureAwait(false);
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation is represented by completion; the consumer token remains authoritative.
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        finally
        {
            results.TryComplete(failure);
        }
    }
}
