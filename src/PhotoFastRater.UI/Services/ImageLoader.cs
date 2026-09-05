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

    /// <summary>Starts a fixed number of workers; queue memory stays independent of library size.</summary>
    public ImageLoader(ThumbnailCacheManager cacheManager)
    {
        _cacheManager = cacheManager;
        _workers = Enumerable.Range(0, WorkerCount)
            .Select(_ => Task.Run(() => ProcessQueueAsync(_shutdown.Token)))
            .ToArray();
    }

    /// <summary>Queues an image. Positive priority is visible, zero is normal, and negative is prefetch.</summary>
    public async Task<BitmapImage?> LoadAsync(
        string filePath,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var completion = new TaskCompletionSource<BitmapImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new LoadRequest(filePath, completion, cancellationToken);
        await SelectWriter(priority).WriteAsync(request, cancellationToken);
        return await completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Schedules only a bounded amount of low-priority look-ahead work.</summary>
    public void PrefetchRange(IEnumerable<string> filePaths)
    {
        foreach (var path in filePaths.Take(QueueCapacity))
        {
            var completion = new TaskCompletionSource<BitmapImage?>(TaskCreationOptions.RunContinuationsAsynchronously);
            _prefetchQueue.Writer.TryWrite(new LoadRequest(path, completion, CancellationToken.None));
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
                request.Completion.TrySetCanceled(request.CancellationToken);
                continue;
            }

            try
            {
                var bytes = await _cacheManager.GetThumbnailAsync(request.FilePath, request.CancellationToken);
                request.Completion.TrySetResult(bytes.Length == 0 ? null : ConvertToImageSource(bytes));
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
            {
                request.Completion.TrySetCanceled(request.CancellationToken);
            }
            catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
            {
                request.Completion.TrySetException(new InvalidOperationException(
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

            var visibleReady = _visibleQueue.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var normalReady = _normalQueue.Reader.WaitToReadAsync(cancellationToken).AsTask();
            var prefetchReady = _prefetchQueue.Reader.WaitToReadAsync(cancellationToken).AsTask();
            await Task.WhenAny(visibleReady, normalReady, prefetchReady);
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static BitmapImage ConvertToImageSource(byte[] imageData)
    {
        if (imageData.Length == 0)
            throw new ArgumentException("Image data cannot be empty.", nameof(imageData));
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(imageData, writable: false);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private sealed record LoadRequest(
        string FilePath,
        TaskCompletionSource<BitmapImage?> Completion,
        CancellationToken CancellationToken);
}
