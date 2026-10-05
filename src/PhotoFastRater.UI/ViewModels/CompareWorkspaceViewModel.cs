using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;

namespace PhotoFastRater.UI.ViewModels;

/// <summary>Coordinates two to four decoded comparison panes with optional viewport synchronization.</summary>
public partial class CompareWorkspaceViewModel(
    IImageDecodeService decoder,
    IRatingCoordinator ratingCoordinator) : ViewModelBase, IDisposable
{
    private const int DecodeDimension = 2048;
    private readonly CancellationTokenSource _lifetime = new();
    private IReadOnlyList<PhotoViewModel> _photoPool = [];
    private int _nextPoolIndex;

    /// <summary>Gets the active panes; the collection is always bounded to four items.</summary>
    public ObservableCollection<ComparePaneViewModel> Panes { get; } = [];

    [ObservableProperty]
    private bool _synchronizeViewport = true;

    [ObservableProperty]
    private bool _autoAdvance;

    [ObservableProperty]
    private ComparePaneViewModel? _activePane;

    /// <summary>Gets a serializable snapshot using stable photo identifiers.</summary>
    public CompareWorkspaceState State => new(
        Panes.Select(pane => pane.Photo.Id).ToArray(),
        SynchronizeViewport,
        AutoAdvance);

    /// <summary>Initializes the workspace with two to four photos and begins bounded decode work.</summary>
    public Task InitializeAsync(IReadOnlyList<PhotoViewModel> photos, CancellationToken cancellationToken = default) =>
        InitializeAsync(photos, 0, photos.Count, cancellationToken);

    /// <summary>Initializes from a bounded resident pool so next-group navigation does not query or retain the full library.</summary>
    public async Task InitializeAsync(
        IReadOnlyList<PhotoViewModel> loadedPhotos,
        int selectedIndex,
        int paneCount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(loadedPhotos);
        if (paneCount is < 2 or > 4 || loadedPhotos.Count < paneCount)
            throw new ArgumentOutOfRangeException(nameof(paneCount), "比較写真は2～4枚必要です。");
        if (selectedIndex < 0 || selectedIndex >= loadedPhotos.Count)
            throw new ArgumentOutOfRangeException(nameof(selectedIndex));

        _photoPool = loadedPhotos;
        var start = Math.Min(selectedIndex, Math.Max(0, loadedPhotos.Count - paneCount));
        var photos = loadedPhotos.Skip(start).Take(paneCount).ToArray();
        _nextPoolIndex = start + paneCount;

        Panes.Clear();
        foreach (var photo in photos)
            Panes.Add(new ComparePaneViewModel(photo));
        ActivePane = Panes[0];

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await Task.WhenAll(Panes.Select(pane => LoadPaneAsync(pane, linked.Token)));
    }

    /// <summary>Changes zoom for all panes when synchronized, otherwise only for the active pane.</summary>
    public void AdjustZoom(ComparePaneViewModel pane, double delta)
    {
        ActivePane = pane;
        var next = Math.Clamp(pane.Zoom + delta, 0.25, 8);
        if (SynchronizeViewport)
        {
            foreach (var candidate in Panes)
                candidate.Zoom = next;
        }
        else
        {
            pane.Zoom = next;
        }
    }

    [RelayCommand]
    private void TogglePin(ComparePaneViewModel pane) => pane.IsPinned = !pane.IsPinned;

    [RelayCommand]
    private void SwapRight(ComparePaneViewModel pane)
    {
        var index = Panes.IndexOf(pane);
        if (index < 0 || index == Panes.Count - 1)
            return;
        Panes.Move(index, index + 1);
    }

    [RelayCommand]
    private void ResetViewport()
    {
        foreach (var pane in Panes)
            pane.Zoom = 1;
    }

    [RelayCommand]
    private async Task AdvanceAsync()
    {
        var replacements = new List<(int Index, ComparePaneViewModel Pane)>();
        var usedIds = Panes.Select(pane => pane.Photo.Id).ToHashSet();
        for (var paneIndex = 0; paneIndex < Panes.Count; paneIndex++)
        {
            if (Panes[paneIndex].IsPinned)
                continue;
            while (_nextPoolIndex < _photoPool.Count && usedIds.Contains(_photoPool[_nextPoolIndex].Id))
                _nextPoolIndex++;
            if (_nextPoolIndex >= _photoPool.Count)
                break;
            var replacement = new ComparePaneViewModel(_photoPool[_nextPoolIndex++]);
            usedIds.Add(replacement.Photo.Id);
            Panes[paneIndex] = replacement;
            replacements.Add((paneIndex, replacement));
        }
        await Task.WhenAll(replacements.Select(item => LoadPaneAsync(item.Pane, _lifetime.Token)));
    }

    /// <summary>Durably rates one pane and advances unpinned panes only when auto-advance is enabled.</summary>
    public async Task SetRatingAsync(ComparePaneViewModel pane, int rating)
    {
        if (rating is < 0 or > 5)
            throw new ArgumentOutOfRangeException(nameof(rating));
        pane.Photo.Rating = rating;
        await ratingCoordinator.SetRatingAsync(
            pane.Photo.Id,
            new RatingState(rating, pane.Photo.IsFavorite, pane.Photo.IsRejected),
            _lifetime.Token);
        if (AutoAdvance)
            await AdvanceAsync();
    }

    private async Task LoadPaneAsync(ComparePaneViewModel pane, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await decoder.DecodeAsync(pane.Photo.FilePath, DecodeDimension, cancellationToken);
            if (bytes.IsEmpty)
                throw new InvalidDataException("表示可能な埋め込み画像がありません。");
            pane.Image = CreateBitmap(bytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or NotSupportedException)
        {
            pane.ErrorMessage = exception.Message;
        }
        finally
        {
            pane.IsLoading = false;
        }
    }

    private static BitmapImage CreateBitmap(ReadOnlyMemory<byte> bytes)
    {
        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(bytes.ToArray(), writable: false);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        Panes.Clear();
        GC.SuppressFinalize(this);
    }
}
