using System.Threading.Channels;
using System.Windows.Media.Imaging;
using PhotoFastRater.Infrastructure.Cache;

namespace PhotoFastRater.UI.Services;

/// <summary>Loads thumbnails through bounded queues that always prefer visible work.</summary>
public sealed class ImageLoader : IDisposable
{
    private const int QueueCapacity = 256;
    private const int WorkerCount = 6;
    private readonly ThumbnailCacheManager _cacheManager;
    private readonly Channel<LoadRequest> _visibleQueue = CreateQueue();
    private readonly Channel<LoadRequest> _normalQueue = CreateQueue();
    private readonly Channel<LoadRequest> _prefetchQueue = CreateQueue();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task[] _workers;
    private const int MaximumDecodedImages = 64;
    private readonly Dictionary<string, LinkedListNode<(string Key, BitmapSource Image)>> _decoded = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Key, BitmapSource Image)> _decodedLru = new();
    private readonly object _decodedGate = new();

    /// <summary>Starts a fixed number of workers; queue memory stays independent of library size.</summary>
    public ImageLoader(ThumbnailCacheManager cacheManager)
    {
        _cacheManager = cacheManager;
        _workers = Enumerable.Range(0, WorkerCount)
            .Select(_ => Task.Run(() => ProcessQueueAsync(_shutdown.Token)))
            .ToArray();
    }

    /// <summary>Queues an image. Positive priority is visible, zero is normal, and negative is prefetch.</summary>
    public async Task<BitmapSource?> LoadAsync(
        string filePath,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var completion = new TaskCompletionSource<BitmapSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new LoadRequest(filePath, completion, cancellationToken);
        await SelectWriter(priority).WriteAsync(request, cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Schedules only a bounded amount of low-priority look-ahead work.</summary>
    public void PrefetchRange(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths.Take(QueueCapacity))
        {
            // Prefetch warms bytes only; discarded WPF bitmaps would churn native decode memory on every scroll.
            _prefetchQueue.Writer.TryWrite(new LoadRequest(path, null, CancellationToken.None));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _visibleQueue.Writer.TryComplete();
        _normalQueue.Writer.TryComplete();
        _prefetchQueue.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            Task.WaitAll(_workers, TimeSpan.FromSeconds(2));
        }
        catch (AggregateException exception) when (exception.InnerExceptions.All(inner => inner is OperationCanceledException))
        {
        }
        _shutdown.Dispose();
        GC.SuppressFinalize(this);
    }

    private static Channel<LoadRequest> CreateQueue() => Channel.CreateBounded<LoadRequest>(new BoundedChannelOptions(QueueCapacity)
    {
        FullMode = BoundedChannelFullMode.Wait,
        SingleReader = false,
        SingleWriter = false
    });

    private ChannelWriter<LoadRequest> SelectWriter(int priority) => priority switch
    {
        > 0 => _visibleQueue.Writer,
        < 0 => _prefetchQueue.Writer,
        _ => _normalQueue.Writer
    };

    private async Task ProcessQueueAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var request = await ReadNextAsync(cancellationToken);
            if (request.CancellationToken.IsCancellationRequested)
            {
                request.Completion?.TrySetCanceled(request.CancellationToken);
                continue;
            }

            try
            {
                var bytes = await _cacheManager.GetThumbnailAsync(request.FilePath, request.CancellationToken);
                if (request.Completion is { } completion)
                {
                    request.CancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(bytes.Length == 0 ? null : GetDecodedImage(bytes));
                }
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
            {
                request.Completion?.TrySetCanceled(request.CancellationToken);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException or InvalidOperationException or UnauthorizedAccessException)
            {
                request.Completion?.TrySetException(new InvalidOperationException(
                    $"サムネイルを読み込めませんでした: {request.FilePath}", exception));
            }
        }
    }

    private async ValueTask<LoadRequest> ReadNextAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_visibleQueue.Reader.TryRead(out var visible))
                return visible;
            if (_normalQueue.Reader.TryRead(out var normal))
                return normal;
            if (_prefetchQueue.Reader.TryRead(out var prefetch))
                return prefetch;

            // Cancel losing waits: otherwise idle channels retain a new waiter on every scroll request.
            using var readyCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var visibleReady = _visibleQueue.Reader.WaitToReadAsync(readyCancellation.Token).AsTask();
            var normalReady = _normalQueue.Reader.WaitToReadAsync(readyCancellation.Token).AsTask();
            var prefetchReady = _prefetchQueue.Reader.WaitToReadAsync(readyCancellation.Token).AsTask();
            await Task.WhenAny(visibleReady, normalReady, prefetchReady);
            await readyCancellation.CancelAsync();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static BitmapSource ConvertToImageSource(byte[] imageData)
    {
        if (imageData.Length == 0)
            throw new ArgumentException("Image data cannot be empty.", nameof(imageData));
        using var stream = new MemoryStream(imageData, writable: false);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        BitmapSource pixels = frame.ColorContexts is { Count: > 0 } contexts
            ? new ColorConvertedBitmap(frame, contexts[0], new System.Windows.Media.ColorContext(System.Windows.Media.PixelFormats.Bgra32), System.Windows.Media.PixelFormats.Bgra32)
            : new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var stride = checked(pixels.PixelWidth * 4);
        var buffer = new byte[checked(stride * pixels.PixelHeight)];
        pixels.CopyPixels(buffer, stride, 0);
        // Retain pixels, not the JPEG decoder and its native codec/stream graph, in the long-lived LRU.
        var bitmap = BitmapSource.Create(pixels.PixelWidth, pixels.PixelHeight, 96, 96,
            System.Windows.Media.PixelFormats.Bgra32, null, buffer, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private BitmapSource GetDecodedImage(byte[] bytes)
    {
        // Content identity handles file edits and lets identical thumbnails share a single frozen WPF surface.
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        lock (_decodedGate)
        {
            if (_decoded.TryGetValue(key, out var existing))
            {
                _decodedLru.Remove(existing); _decodedLru.AddFirst(existing);
                return existing.Value.Image;
            }
            var image = ConvertToImageSource(bytes);
            _decoded.Add(key, _decodedLru.AddFirst((key, image)));
            if (_decoded.Count > MaximumDecodedImages)
            {
                var last = _decodedLru.Last!; _decoded.Remove(last.Value.Key); _decodedLru.RemoveLast();
            }
            return image;
        }
    }

    private sealed record LoadRequest(
        string FilePath,
        TaskCompletionSource<BitmapSource?>? Completion,
        CancellationToken CancellationToken);
}
