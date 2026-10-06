using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Services;

/// <summary>Serializes UI rating gestures and changes displayed/exported state only after durable commit.</summary>
public sealed class PhotoRatingEditor(IRatingCoordinator coordinator) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task SetStarsAsync(PhotoViewModel photo, int stars) => UpdateAsync(photo,
        state => new RatingState(stars, state.IsFavorite, state.IsRejected));

    public Task ToggleFavoriteAsync(PhotoViewModel photo) => UpdateAsync(photo,
        state => new RatingState(state.Stars, !state.IsFavorite, state.IsRejected));

    public Task ToggleRejectedAsync(PhotoViewModel photo) => UpdateAsync(photo,
        state => new RatingState(state.Stars < 0 ? 0 : state.Stars, state.IsFavorite, !state.IsRejected));

    private async Task UpdateAsync(PhotoViewModel photo, Func<RatingState, RatingState> change)
    {
        await _gate.WaitAsync();
        try
        {
            var next = change(new RatingState(photo.Rating, photo.IsFavorite, photo.IsRejected));
            await coordinator.SetRatingAsync(photo.Id, next);
            photo.Rating = next.Stars;
            photo.IsFavorite = next.IsFavorite;
            photo.IsRejected = next.IsRejected;
            var model = photo.GetModel();
            model.Rating = next.Stars;
            model.IsFavorite = next.IsFavorite;
            model.IsRejected = next.IsRejected;
        }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }
}
