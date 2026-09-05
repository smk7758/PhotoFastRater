using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Infrastructure.Database.Repositories;
using PhotoFastRater.Infrastructure.Metadata;

namespace PhotoFastRater.Infrastructure.Services;

/// <summary>Commits ratings before requesting eventually consistent XMP persistence.</summary>
public sealed class RatingCoordinator : IRatingCoordinator
{
    private readonly PhotoRepository _photoRepository;
    private readonly XmpSyncQueue _syncQueue;
    private readonly IPhotoChangeNotifier _notifier;

    /// <summary>Initializes the rating use case.</summary>
    public RatingCoordinator(PhotoRepository photoRepository, XmpSyncQueue syncQueue, IPhotoChangeNotifier notifier)
    {
        _photoRepository = photoRepository;
        _syncQueue = syncQueue;
        _notifier = notifier;
    }

    /// <inheritdoc />
    public async Task SetRatingAsync(int photoId, RatingState state, CancellationToken cancellationToken = default)
    {
        // DB is committed first so a sidecar or permission failure cannot lose the user's rating.
        var changedIds = await _photoRepository.CommitRatingAsync(photoId, state, cancellationToken);
        foreach (var changedId in changedIds)
        {
            await _syncQueue.EnqueueAsync(changedId, cancellationToken);
            _notifier.Publish(new PhotoChangedEventArgs(changedId, "rating"));
        }
    }
}
