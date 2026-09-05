using PhotoFastRater.Core.Abstractions;

namespace PhotoFastRater.Infrastructure.Services;

/// <summary>Broadcasts committed changes to active window scopes.</summary>
public sealed class PhotoChangeNotifier : IPhotoChangeNotifier
{
    /// <inheritdoc />
    public event EventHandler<PhotoChangedEventArgs>? PhotoChanged;

    /// <inheritdoc />
    public void Publish(PhotoChangedEventArgs change)
    {
        ArgumentNullException.ThrowIfNull(change);
        PhotoChanged?.Invoke(this, change);
    }
}
