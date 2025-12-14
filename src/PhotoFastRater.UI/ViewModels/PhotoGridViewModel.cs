using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Database.Repositories;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.Views;

namespace PhotoFastRater.UI.ViewModels;

public partial class PhotoGridViewModel : ViewModelBase
{
    private readonly PhotoRepository _photoRepository;
    private readonly ImageLoader _imageLoader;

    [ObservableProperty]
    private ObservableCollection<PhotoViewModel> _photos = new();

    [ObservableProperty]
    private PhotoViewModel? _selectedPhoto;

    [ObservableProperty]
    private string _sortBy = "DateTaken";

    [ObservableProperty]
    private int _filterRating = 0;

    [ObservableProperty]
    private string? _filterCamera;

    public PhotoGridViewModel(PhotoRepository photoRepository, ImageLoader imageLoader)
    {
        _photoRepository = photoRepository;
        _imageLoader = imageLoader;
    }

    public async Task LoadAllPhotosAsync()
    {
        var photos = await _photoRepository.GetAllAsync();
        Photos.Clear();

        // まずViewModelを作成してUIに追加（即座に表示）
        var viewModels = photos.Select(photo => new PhotoViewModel(photo)).ToList();
        foreach (var vm in viewModels)
        {
            Photos.Add(vm);
        }

        // サムネイルを並列で読み込み（バックグラウンドスレッド）
        var maxDegreeOfParallelism = Environment.ProcessorCount;
        await Task.Run(() => Parallel.ForEach(viewModels,
            new ParallelOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism },
            vm =>
            {
                // 並列タスクとして実行（await不要）
                LoadThumbnailAsync(vm).Wait();
            }));
    }

    public async Task LoadVisiblePhotosAsync(int startIndex, int count)
    {
        // 表示範囲の画像を並列読み込み
        var visiblePhotos = Photos.Skip(startIndex).Take(count).ToList();
        var loadTasks = visiblePhotos
            .Where(p => p.Thumbnail == null)
            .Select(p => LoadThumbnailAsync(p, priority: 10));

        await Task.WhenAll(loadTasks);

        // プリフェッチ: 次の画面分を先読み
        var nextPhotos = Photos.Skip(startIndex + count).Take(count).ToList();
        _imageLoader.PrefetchRange(nextPhotos.Select(p => p.FilePath));
    }

    private async Task LoadThumbnailAsync(PhotoViewModel photo, int priority = 0)
    {
        try
        {
            var thumbnail = await _imageLoader.LoadAsync(photo.FilePath, priority);
            photo.Thumbnail = thumbnail;
        }
        catch
        {
            // エラーは無視（サムネイル表示失敗）
        }
    }

    [RelayCommand]
    private async Task SetRatingAsync(int rating)
    {
        if (SelectedPhoto == null) return;

        SelectedPhoto.Rating = rating;
        var photo = SelectedPhoto.GetModel();
        photo.Rating = rating;
        await _photoRepository.UpdateAsync(photo);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedPhoto == null) return;

        SelectedPhoto.IsFavorite = !SelectedPhoto.IsFavorite;
        var photo = SelectedPhoto.GetModel();
        photo.IsFavorite = SelectedPhoto.IsFavorite;
        await _photoRepository.UpdateAsync(photo);
    }

    [RelayCommand]
    private async Task FilterByCameraAsync(string? cameraModel)
    {
        FilterCamera = cameraModel;
        await ApplyFiltersAsync();
    }

    [RelayCommand]
    private async Task FilterByRatingAsync(int rating)
    {
        FilterRating = rating;
        await ApplyFiltersAsync();
    }

    [RelayCommand]
    private void OpenPhoto(PhotoViewModel photo)
    {
        var viewer = new PhotoViewerWindow(photo);
        viewer.ShowDialog();
    }

    private async Task ApplyFiltersAsync()
    {
        IEnumerable<Core.Models.Photo> photos;

        if (!string.IsNullOrEmpty(FilterCamera))
        {
            photos = await _photoRepository.GetByCameraAsync(FilterCamera);
        }
        else if (FilterRating > 0)
        {
            photos = await _photoRepository.GetByRatingAsync(FilterRating);
        }
        else
        {
            photos = await _photoRepository.GetAllAsync();
        }

        Photos.Clear();
        foreach (var photo in photos)
        {
            var vm = new PhotoViewModel(photo);
            Photos.Add(vm);
            _ = LoadThumbnailAsync(vm);
        }
    }
}
