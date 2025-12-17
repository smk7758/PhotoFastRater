using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Database.Repositories;
using PhotoFastRater.Core.Export;
using PhotoFastRater.Core.UI;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.Views;

namespace PhotoFastRater.UI.ViewModels;

public partial class PhotoGridViewModel : ViewModelBase
{
    private readonly PhotoRepository _photoRepository;
    private readonly ImageLoader _imageLoader;
    private readonly SocialMediaExporter _socialMediaExporter;
    private readonly UIConfiguration _uiConfig;

    [ObservableProperty]
    private ObservableCollection<PhotoViewModel> _photos = new();

    [ObservableProperty]
    private ObservableCollection<PhotoTreeNode> _photoTree = new();

    [ObservableProperty]
    private PhotoViewModel? _selectedPhoto;

    [ObservableProperty]
    private string _sortBy = "DateTaken";

    [ObservableProperty]
    private int _filterRating = 0;

    [ObservableProperty]
    private string? _filterCamera;

    [ObservableProperty]
    private bool _isTreeViewMode;

    // グリッドの列数（WrapPanelの列数）
    private const int GridColumns = 6;

    public PhotoGridViewModel(PhotoRepository photoRepository, ImageLoader imageLoader, SocialMediaExporter socialMediaExporter, UIConfiguration uiConfig)
    {
        _photoRepository = photoRepository;
        _imageLoader = imageLoader;
        _socialMediaExporter = socialMediaExporter;
        _uiConfig = uiConfig;
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
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] LoadThumbnailAsync 開始: {Path.GetFileName(photo.FilePath)}, Priority={priority}");

            var thumbnail = await _imageLoader.LoadAsync(photo.FilePath, priority);

            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] サムネイル取得完了: {Path.GetFileName(photo.FilePath)}, IsNull={thumbnail == null}");

            // UI スレッドでプロパティを更新
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"[PhotoGrid] UIスレッドでThumbnail設定: {Path.GetFileName(photo.FilePath)}");
                photo.Thumbnail = thumbnail;
                System.Diagnostics.Debug.WriteLine($"[PhotoGrid] Thumbnail設定完了: {Path.GetFileName(photo.FilePath)}, photo.Thumbnail IsNull={photo.Thumbnail == null}");
            });
        }
        catch (Exception ex)
        {
            // エラーは無視（サムネイル表示失敗）
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] サムネイル読み込みエラー: {photo.FilePath} - {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] スタックトレース: {ex.StackTrace}");
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
    private void SelectPhoto(PhotoViewModel photo)
    {
        // 既存の選択を解除
        if (SelectedPhoto != null)
        {
            SelectedPhoto.IsSelected = false;
        }

        // 新しい写真を選択
        SelectedPhoto = photo;
        if (photo != null)
        {
            photo.IsSelected = true;
        }
    }

    [RelayCommand]
    private void OpenPhoto(PhotoViewModel photo)
    {
        var viewer = new PhotoViewerWindow(photo);
        viewer.ShowDialog();
    }

    [RelayCommand]
    private void NavigateUp()
    {
        if (!CanNavigate()) return;

        var currentIndex = Photos.IndexOf(SelectedPhoto!);
        var targetIndex = currentIndex - GridColumns;

        if (targetIndex >= 0)
        {
            SelectPhoto(Photos[targetIndex]);
        }
    }

    [RelayCommand]
    private void NavigateDown()
    {
        if (!CanNavigate()) return;

        var currentIndex = Photos.IndexOf(SelectedPhoto!);
        var targetIndex = currentIndex + GridColumns;

        if (targetIndex < Photos.Count)
        {
            SelectPhoto(Photos[targetIndex]);
        }
    }

    [RelayCommand]
    private void NavigateLeft()
    {
        if (!CanNavigate()) return;

        var currentIndex = Photos.IndexOf(SelectedPhoto!);
        if (currentIndex > 0)
        {
            SelectPhoto(Photos[currentIndex - 1]);
        }
    }

    [RelayCommand]
    private void NavigateRight()
    {
        if (!CanNavigate()) return;

        var currentIndex = Photos.IndexOf(SelectedPhoto!);
        if (currentIndex < Photos.Count - 1)
        {
            SelectPhoto(Photos[currentIndex + 1]);
        }
    }

    private bool CanNavigate()
    {
        // SelectionOnlyモードの場合は、写真が選択されている必要がある
        if (_uiConfig.ArrowKeyNavigationMode == "SelectionOnly")
        {
            return SelectedPhoto != null;
        }

        // GridFocusモードの場合
        // 写真が選択されていない場合は、最初の写真を選択
        if (SelectedPhoto == null && Photos.Count > 0)
        {
            SelectPhoto(Photos[0]);
        }

        return SelectedPhoto != null;
    }

    // Context menu public methods
    public async void SetRating(PhotoViewModel photo, int rating)
    {
        photo.Rating = rating;
        var model = photo.GetModel();
        model.Rating = rating;
        await _photoRepository.UpdateAsync(model);
    }

    public async void ToggleFavorite(PhotoViewModel photo)
    {
        photo.IsFavorite = !photo.IsFavorite;
        var model = photo.GetModel();
        model.IsFavorite = photo.IsFavorite;
        await _photoRepository.UpdateAsync(model);
    }

    public async void ToggleReject(PhotoViewModel photo)
    {
        photo.IsRejected = !photo.IsRejected;
        var model = photo.GetModel();
        model.IsRejected = photo.IsRejected;
        await _photoRepository.UpdateAsync(model);
    }

    public async void ExportToSocialMedia(PhotoViewModel photo)
    {
        try
        {
            var model = photo.GetModel();
            var template = new Core.Models.ExportTemplate
            {
                Name = "SNS Export",
                TargetPlatform = Core.Models.SocialMediaPlatform.Instagram,
                OutputWidth = 1080,
                OutputHeight = 1080,
                MaintainAspectRatio = true,
                EnableExifOverlay = true,
                EnableFrame = true,
                FrameWidth = 20,
                FrameColor = "#FFFFFF"
            };

            var outputDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "PhotoFastRater_Export");

            Directory.CreateDirectory(outputDir);

            var outputPath = Path.Combine(outputDir, $"export_{Path.GetFileNameWithoutExtension(photo.FileName)}.jpg");

            await _socialMediaExporter.ExportAsync(model, template, outputPath);

            System.Windows.MessageBox.Show($"エクスポートしました:\n{outputPath}", "完了",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"エクスポートエラー: {ex.Message}", "エラー",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    public async void DeleteFromDatabase(PhotoViewModel photo)
    {
        try
        {
            var model = photo.GetModel();
            await _photoRepository.DeleteAsync(model.Id);
            Photos.Remove(photo);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"削除エラー: {ex.Message}", "エラー",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    public async void DeleteFile(PhotoViewModel photo)
    {
        try
        {
            var model = photo.GetModel();

            // DBから削除
            await _photoRepository.DeleteAsync(model.Id);

            // ファイルを削除
            if (File.Exists(photo.FilePath))
            {
                File.Delete(photo.FilePath);
            }

            // UIから削除
            Photos.Remove(photo);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"ファイル削除エラー: {ex.Message}", "エラー",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void ToggleViewMode()
    {
        IsTreeViewMode = !IsTreeViewMode;
        if (IsTreeViewMode)
        {
            BuildPhotoTree();
        }
    }

    /// <summary>
    /// 写真から階層ツリーを構築（年→月→日→フォルダ）
    /// </summary>
    public void BuildPhotoTree()
    {
        PhotoTree.Clear();

        // 日付でグループ化
        var photosByDate = Photos
            .GroupBy(p => p.DateTaken.Date)
            .OrderByDescending(g => g.Key)
            .ToList();

        foreach (var dateGroup in photosByDate)
        {
            var date = dateGroup.Key;
            var year = date.Year;
            var month = date.Month;
            var day = date.Day;

            // 年ノードを取得または作成
            var yearNode = PhotoTree.FirstOrDefault(n => n.Year == year);
            if (yearNode == null)
            {
                yearNode = new PhotoTreeNode
                {
                    DisplayName = $"{year}年",
                    NodeType = TreeNodeType.Year,
                    Year = year,
                    IsExpanded = true
                };
                PhotoTree.Add(yearNode);
            }

            // 月ノードを取得または作成
            var monthNode = yearNode.Children.FirstOrDefault(n => n.Month == month);
            if (monthNode == null)
            {
                monthNode = new PhotoTreeNode
                {
                    DisplayName = $"{month}月",
                    NodeType = TreeNodeType.Month,
                    Year = year,
                    Month = month
                };
                yearNode.Children.Add(monthNode);
            }

            // 日ノードを取得または作成
            var dayNode = monthNode.Children.FirstOrDefault(n => n.Day == day);
            if (dayNode == null)
            {
                dayNode = new PhotoTreeNode
                {
                    DisplayName = $"{day}日",
                    NodeType = TreeNodeType.Day,
                    Year = year,
                    Month = month,
                    Day = day
                };
                monthNode.Children.Add(dayNode);
            }

            // フォルダでさらにグループ化
            var photosByFolder = dateGroup
                .GroupBy(p => p.GetModel().FolderPath ?? "未分類")
                .ToList();

            foreach (var folderGroup in photosByFolder)
            {
                var folderPath = folderGroup.Key;
                var folderName = string.IsNullOrEmpty(folderPath) || folderPath == "未分類"
                    ? "未分類"
                    : Path.GetFileName(folderPath);

                var folderNode = new PhotoTreeNode
                {
                    DisplayName = folderName,
                    NodeType = TreeNodeType.Folder,
                    FolderPath = folderPath
                };

                foreach (var photo in folderGroup)
                {
                    folderNode.Photos.Add(photo);
                }

                dayNode.Children.Add(folderNode);
            }
        }
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
        System.Diagnostics.Debug.WriteLine($"[PhotoGrid] 写真の読み込み開始: {photos.Count()}枚");
        foreach (var photo in photos)
        {
            var vm = new PhotoViewModel(photo);
            Photos.Add(vm);
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] PhotosコレクションにViewModel追加: {Path.GetFileName(photo.FilePath)}");
            // サムネイルをバックグラウンドで非同期読み込み（待機しない）
            _ = LoadThumbnailAsync(vm);
        }
        System.Diagnostics.Debug.WriteLine($"[PhotoGrid] Photosコレクション準備完了: {Photos.Count}枚");

        // TreeViewモードの場合はツリーも更新（サムネイルは非同期で読み込まれる）
        if (IsTreeViewMode)
        {
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] BuildPhotoTree開始");
            BuildPhotoTree();
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] BuildPhotoTree完了");
        }
    }
}
