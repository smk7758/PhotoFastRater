using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Xml;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Database.Repositories;
using Microsoft.Extensions.Logging;

namespace PhotoFastRater.Infrastructure.Metadata;

/// <summary>Processes a bounded, deduplicated sidecar queue while the DB remains the persistent backlog.</summary>
public sealed class XmpSyncQueue : IDisposable
{
    private readonly PhotoRepository _photoRepository;
    private readonly IXmpSidecarStore _sidecarStore;
    private readonly ILogger<XmpSyncQueue> _logger;
    private readonly Channel<int> _channel;
    private readonly ConcurrentDictionary<int, byte> _scheduled = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _worker;

    /// <summary>Initializes one single-writer sidecar worker independent of the caller's UI context.</summary>
    public XmpSyncQueue(
        PhotoRepository photoRepository,
        IXmpSidecarStore sidecarStore,
        ILogger<XmpSyncQueue> logger)
    {
        _photoRepository = photoRepository;
        _sidecarStore = sidecarStore;
        _logger = logger;
        _channel = Channel.CreateBounded<int>(new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
        // Window shutdown waits synchronously for this worker, so its continuations must
        // never depend on the UI dispatcher that is performing that shutdown.
        _worker = Task.Run(() => ProcessAsync(_shutdown.Token));
    }

    /// <summary>Restores the in-memory queue from durable pending rows after startup.</summary>
    public async Task RestorePendingAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        foreach (var photoId in await _photoRepository.GetPendingSyncIdsAsync(linked.Token))
            await EnqueueAsync(photoId, linked.Token);
    }

    /// <summary>Schedules a photo once; repeated changes are coalesced and the worker reads the latest DB value.</summary>
    public async ValueTask EnqueueAsync(int photoId, CancellationToken cancellationToken = default)
    {
        if (!_scheduled.TryAdd(photoId, 0))
            return;
        try
        {
            await _channel.Writer.WriteAsync(photoId, cancellationToken);
        }
        catch
        {
            _scheduled.TryRemove(photoId, out _);
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _shutdown.Cancel();
        try { _worker.GetAwaiter().GetResult(); } catch (OperationCanceledException) { }
        _shutdown.Dispose();
    }

    private async Task ProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var photoId in _channel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    // Debounce rapid keyboard ratings and always serialize the newest committed revision.
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
                    await SynchronizeAsync(photoId, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    _logger.LogError(exception, "Unexpected XMP synchronization failure for photo {PhotoId}", photoId);
                    await TryMarkFailedAsync(photoId);
                }
                finally
                {
                    _scheduled.TryRemove(photoId, out _);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("XMP synchronization worker stopped");
        }
    }

    private async Task SynchronizeAsync(int photoId, CancellationToken cancellationToken)
    {
        var photo = await _photoRepository.GetForSyncAsync(photoId, cancellationToken);
        if (photo is null || photo.IsMissing)
            return;

        try
        {
            var sidecar = await _sidecarStore.ReadAsync(photo.FilePath, cancellationToken);
            if (HasExternalConflict(photo, sidecar))
            {
                await _photoRepository.MarkSyncStatusAsync(photo.Id, MetadataSyncStatus.Conflict, photo.SidecarModifiedUtc, cancellationToken);
                return;
            }

            var document = new XmpRatingDocument(
                new RatingState(photo.Rating, photo.IsFavorite, photo.IsRejected),
                photo.RatingModifiedUtc,
                photo.RatingRevision,
                photo.RatingSource);
            await _sidecarStore.WriteAsync(photo.FilePath, document, cancellationToken);
            var modifiedUtc = File.GetLastWriteTimeUtc(XmpSidecarStore.GetSidecarPath(photo.FilePath));
            await _photoRepository.MarkSyncStatusAsync(photo.Id, MetadataSyncStatus.Synchronized, modifiedUtc, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            await _photoRepository.MarkSyncStatusAsync(photo.Id, MetadataSyncStatus.AccessDenied, photo.SidecarModifiedUtc, cancellationToken);
        }
        catch (IOException)
        {
            await _photoRepository.MarkSyncStatusAsync(photo.Id, MetadataSyncStatus.Failed, photo.SidecarModifiedUtc, cancellationToken);
        }
        catch (XmlException)
        {
            await _photoRepository.MarkSyncStatusAsync(photo.Id, MetadataSyncStatus.Failed, photo.SidecarModifiedUtc, cancellationToken);
        }
    }

    private async Task TryMarkFailedAsync(int photoId)
    {
        try
        {
            await _photoRepository.MarkSyncStatusAsync(photoId, MetadataSyncStatus.Failed, null, CancellationToken.None);
        }
        catch (Exception exception)
        {
            // A failed status write must not terminate the single long-lived worker.
            _logger.LogError(exception, "Could not persist XMP failure status for photo {PhotoId}", photoId);
        }
    }

    private static bool HasExternalConflict(Photo photo, XmpRatingDocument? sidecar)
    {
        if (sidecar is null || sidecar.UpdateSource == "PhotoFastRater")
            return false;

        var differs = sidecar.Rating != new RatingState(photo.Rating, photo.IsFavorite, photo.IsRejected);
        if (!differs)
            return false;

        // A sidecar without PhotoFastRater timestamps is still user data and must never be overwritten silently.
        return photo.SidecarModifiedUtc is null ||
               sidecar.Revision != photo.RatingRevision ||
               sidecar.UpdatedUtc > photo.SidecarModifiedUtc.Value;
    }
}
