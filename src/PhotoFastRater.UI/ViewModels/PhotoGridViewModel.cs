using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Infrastructure.Database.Repositories;
using PhotoFastRater.Infrastructure.Export;
using PhotoFastRater.Core.UI;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.Views;
using PhotoFastRater.UI.Collections;

namespace PhotoFastRater.UI.ViewModels;

public partial class PhotoGridViewModel : ViewModelBase, IDisposable
{
    private readonly PhotoRepository _photoRepository;
    private readonly ImageLoader _imageLoader;
    private readonly SocialMediaExporter _socialMediaExporter;
    private readonly UIConfiguration _uiConfig;
    private readonly IUserInteractionService _interaction;
    private readonly IPlatformShell _platformShell;
    private readonly PhotoRatingEditor _ratingEditor;
    private readonly IImageDecodeService _decoder;
    private readonly IPhotoCatalog _photoCatalog;
    private PhotoSearchQuery _currentQuery = new();

    public AsyncVirtualizingCollection<PhotoViewModel> Photos { get; }

    public long TotalPhotoCount => Photos.TotalCount;

    partial void OnSelectedPhotoChanged(PhotoViewModel? oldValue, PhotoViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        SelectedPhotoId = newValue?.Id;
    }

    [ObservableProperty]
    private ObservableCollection<PhotoTreeNode> _photoTree = new();

    [ObservableProperty]
    private PhotoViewModel? _selectedPhoto;

    [ObservableProperty]
    private int? _selectedPhotoId;

    [ObservableProperty]
    private string _sortBy = "DateTaken";

    [ObservableProperty]
    private int _filterRating = 0;

    [ObservableProperty]
    private string? _filterCamera;

    [ObservableProperty]
    private bool _isTreeViewMode;

    [ObservableProperty]
    private bool _missingOnly;

    /// <summary>
    /// IsTreeViewMode が変更されたときの処理
    /// </summary>
    partial void OnIsTreeViewModeChanged(bool value)
    {
        if (value)
        {
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] IsTreeViewMode changed to true, calling BuildPhotoTree");
            BuildPhotoTree();
        }
    }

    [ObservableProperty] private int _thumbnailSize = 200;
    private double _gridWidth = 1200;
    private int _gridColumns = 6;
    public int GridColumns { get => _gridColumns; private set => SetProperty(ref _gridColumns, value); }

    partial void OnThumbnailSizeChanged(int value) => UpdateGridColumns();

    public void NotifyGridWidth(double width)
    {
        _gridWidth = width;
        UpdateGridColumns();
    }

    private void UpdateGridColumns() =>
        GridColumns = Math.Max(1, (int)(_gridWidth / (ThumbnailSize + 8)));

    public PhotoGridViewModel(
        PhotoRepository photoRepository,
        ImageLoader imageLoader,
        SocialMediaExporter socialMediaExporter,
        UIConfiguration uiConfig,
        IUserInteractionService interaction,
        IPlatformShell platformShell,
        PhotoRatingEditor ratingEditor,
        IImageDecodeService decoder,
        IPhotoCatalog photoCatalog)
    {
        _photoRepository = photoRepository;
        _imageLoader = imageLoader;
        _socialMediaExporter = socialMediaExporter;
        _uiConfig = uiConfig;
        _interaction = interaction;
        _platformShell = platformShell;
        _ratingEditor = ratingEditor;
        _decoder = decoder;
        _photoCatalog = photoCatalog;
        Photos = new AsyncVirtualizingCollection<PhotoViewModel>(LoadPageAsync);
        ((System.ComponentModel.INotifyPropertyChanged)Photos).PropertyChanged += (_, eventArgs) =>
        {
            if (eventArgs.PropertyName == nameof(Photos.TotalCount))
                OnPropertyChanged(nameof(TotalPhotoCount));
        };
    }

    public async Task LoadAllPhotosAsync(CancellationToken cancellationToken = default)
    {
        _currentQuery = new PhotoSearchQuery();
        await Photos.ResetAsync(cancellationToken);
        await LoadVisiblePhotosAsync(0, Math.Min(Photos.Count, 50), cancellationToken);
    }

    /// <summary>Applies indexed text search and cancels stale page and thumbnail work.</summary>
    public async Task SearchAsync(string? text, CancellationToken cancellationToken = default)
    {
        _currentQuery = _currentQuery with { Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim() };
        await Photos.ResetAsync(cancellationToken);
        await LoadVisiblePhotosAsync(0, Math.Min(Photos.Count, 50), cancellationToken);
        if (IsTreeViewMode)
            BuildPhotoTree();
    }

    /// <summary>Loads the next keyset page while retaining no more than 1,280 view models.</summary>
    public async Task LoadNextPageAsync(CancellationToken cancellationToken = default)
    {
        var previousCount = Photos.Count;
        await Photos.LoadNextAsync(cancellationToken);
        var added = Math.Max(0, Photos.Count - previousCount);
        if (added > 0)
            await LoadVisiblePhotosAsync(
                Math.Max(0, Photos.Count - added),
                Math.Min(added, 50),
                cancellationToken);
    }

    /// <summary>Returns the selected photo followed by nearby loaded photos, without fetching an unbounded range.</summary>
    public IReadOnlyList<PhotoViewModel> GetCompareCandidates(int count)
    {
        if (count is < 2 or > 4)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (SelectedPhoto is null)
            return [];

        var selectedIndex = Photos.IndexOf(SelectedPhoto);
        if (selectedIndex < 0)
            return [];
        var start = Math.Min(selectedIndex, Math.Max(0, Photos.Count - count));
        return Photos.Skip(start).Take(count).ToArray();
    }

    /// <summary>Returns only the bounded pages already resident in the UI for compare navigation.</summary>
    public IReadOnlyList<PhotoViewModel> GetLoadedPhotosSnapshot() => Photos.ToArray();

    /// <summary>Gets the selected position within the bounded resident pages.</summary>
    public int SelectedLoadedIndex => SelectedPhoto is null ? -1 : Photos.IndexOf(SelectedPhoto);

    public async Task LoadVisiblePhotosAsync(
        int startIndex,
        int count,
        CancellationToken cancellationToken = default)
    {
        // Catalog pages hold metadata, not an ever-growing set of native WPF image surfaces.
        var retainedStart = Math.Max(0, startIndex - count);
        var retainedEnd = startIndex + count * 2;
        for (var index = 0; index < Photos.Count; index++)
            if (index < retainedStart || index >= retainedEnd) Photos[index].Thumbnail = null;
        // 表示範囲の画像を並列読み込み
        var visiblePhotos = Photos.Skip(startIndex).Take(count).ToList();
        var loadTasks = visiblePhotos
            .Where(p => p.Thumbnail == null)
            .Select(p => LoadThumbnailAsync(p, priority: 10, cancellationToken));

        await Task.WhenAll(loadTasks);

        // プリフェッチ: 次の画面分を先読み
        var nextPhotos = Photos.Skip(startIndex + count).Take(count).ToList();
        _imageLoader.PrefetchRange(nextPhotos.Select(p => p.FilePath));
    }

    private async Task LoadThumbnailAsync(
        PhotoViewModel photo,
        int priority = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] LoadThumbnailAsync 開始: {Path.GetFileName(photo.FilePath)}, Priority={priority}");

            var thumbnail = await _imageLoader.LoadAsync(photo.FilePath, priority, cancellationToken);

            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] サムネイル取得完了: {Path.GetFileName(photo.FilePath)}, IsNull={thumbnail == null}");

            // UI スレッドでプロパティを更新
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"[PhotoGrid] UIスレッドでThumbnail設定: {Path.GetFileName(photo.FilePath)}");
                photo.Thumbnail = thumbnail;
                System.Diagnostics.Debug.WriteLine($"[PhotoGrid] Thumbnail設定完了: {Path.GetFileName(photo.FilePath)}, photo.Thumbnail IsNull={photo.Thumbnail == null}");
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer query owns the visible range now.
        }
        catch (Exception ex)
        {
            // エラーは無視（サムネイル表示失敗）
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] サムネイル読み込みエラー: {photo.FilePath} - {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] スタックトレース: {ex.StackTrace}");
        }
    }

    [RelayCommand]
    private Task SetRatingAsync(int rating) => SelectedPhoto is { } photo ? SetRatingAsync(photo, rating) : Task.CompletedTask;

    [RelayCommand]
    private Task ToggleFavoriteAsync() => SelectedPhoto is { } photo ? ToggleFavoriteAsync(photo) : Task.CompletedTask;

    [RelayCommand]
    private Task ToggleRejectAsync() => SelectedPhoto is { } photo ? ToggleRejectAsync(photo) : Task.CompletedTask;
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
            SelectedPhotoId = photo.Id;
        }
    }

    [RelayCommand]
    private void OpenPhoto(PhotoViewModel photo)
    {
        var viewer = new PhotoViewerWindow(photo, _ratingEditor, _decoder);
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

    /// <summary>Uses the same durable editing boundary for card, keyboard, and viewer operations.</summary>
    public Task SetRatingAsync(PhotoViewModel photo, int rating) => _ratingEditor.SetStarsAsync(photo, rating);
    public Task ToggleFavoriteAsync(PhotoViewModel photo) => _ratingEditor.ToggleFavoriteAsync(photo);
    public Task ToggleRejectAsync(PhotoViewModel photo) => _ratingEditor.ToggleRejectedAsync(photo);
    public async Task ExportToSocialMediaAsync(PhotoViewModel photo)
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

            var outputDir = await _interaction.SelectFolderAsync("SNS画像の書き出し先を選択してください");
            if (string.IsNullOrWhiteSpace(outputDir)) return;
            var outputPath = Path.Combine(outputDir, $"export_{Path.GetFileNameWithoutExtension(photo.FileName)}.jpg");

            outputPath = await _socialMediaExporter.ExportAsync(model, template, outputPath);

            await _interaction.NotifyAsync("完了", $"エクスポートしました:\n{outputPath}", UserNotificationKind.Information);
        }
        catch (Exception ex)
        {
            await _interaction.NotifyAsync("エラー", $"エクスポートエラー: {ex.Message}", UserNotificationKind.Error);
        }
    }

    public async Task DeleteFromDatabaseAsync(PhotoViewModel photo)
    {
        try
        {
            var model = photo.GetModel();
            await _photoRepository.DeleteAsync(model.Id);
            Photos.Remove(photo);
        }
        catch (Exception ex)
        {
            await _interaction.NotifyAsync("エラー", $"削除エラー: {ex.Message}", UserNotificationKind.Error);
        }
    }

    public async Task DeleteFileAsync(PhotoViewModel photo)
    {
        try
        {
            var model = photo.GetModel();

            // ごみ箱移動に成功するまでDB記録を残し、失敗時に写真を見失わない。
            if (File.Exists(photo.FilePath))
                await _platformShell.MoveToRecycleBinAsync(photo.FilePath);

            await _photoRepository.DeleteAsync(model.Id);

            // UIから削除
            Photos.Remove(photo);
        }
        catch (Exception ex)
        {
            await _interaction.NotifyAsync("エラー", $"ごみ箱への移動に失敗しました: {ex.Message}", UserNotificationKind.Error);
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
        System.Diagnostics.Debug.WriteLine($"[PhotoGrid.BuildPhotoTree] 開始: Photos.Count={Photos.Count}");

        // 日付でグループ化
        var photosByDate = Photos
            .GroupBy(p => p.DateTaken.Date)
            .OrderByDescending(g => g.Key)
            .ToList();

        System.Diagnostics.Debug.WriteLine($"[PhotoGrid.BuildPhotoTree] 日付グループ数: {photosByDate.Count}");

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

        System.Diagnostics.Debug.WriteLine($"[PhotoGrid.BuildPhotoTree] 完了: PhotoTree.Count={PhotoTree.Count}");
        foreach (var yearNode in PhotoTree)
        {
            System.Diagnostics.Debug.WriteLine($"  年: {yearNode.DisplayName}, 子数={yearNode.Children.Count}");
        }
    }

    private async Task ApplyFiltersAsync()
    {
        _currentQuery = new PhotoSearchQuery(
            Text: _currentQuery.Text,
            MinimumRating: FilterRating > 0 ? FilterRating : null,
            CameraModel: string.IsNullOrWhiteSpace(FilterCamera) ? null : FilterCamera,
            IncludeMissing: MissingOnly,
            MissingOnly: MissingOnly);
        await Photos.ResetAsync();
        await LoadVisiblePhotosAsync(0, Math.Min(Photos.Count, 50));

        // TreeViewモードの場合はツリーも更新（サムネイルは非同期で読み込まれる）
        if (IsTreeViewMode)
        {
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] BuildPhotoTree開始");
            BuildPhotoTree();
            System.Diagnostics.Debug.WriteLine($"[PhotoGrid] BuildPhotoTree完了");
        }
    }

    [RelayCommand]
    private async Task ToggleMissingOnlyAsync()
    {
        MissingOnly = !MissingOnly;
        await ApplyFiltersAsync();
    }

    private async Task<PagedResult<PhotoViewModel>> LoadPageAsync(
        PageCursor? cursor,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var page = await _photoCatalog.SearchAsync(_currentQuery, cursor, pageSize, cancellationToken);
        var items = page.Items.Select(summary => new PhotoViewModel(new Core.Models.Photo
        {
            Id = summary.Id,
            FilePath = summary.FilePath,
            FileName = summary.FileName,
            DateTaken = summary.DateTakenUtc,
            Rating = summary.Rating.Stars,
            IsFavorite = summary.Rating.IsFavorite,
            IsRejected = summary.Rating.IsRejected,
            IsMissing = summary.IsMissing,
            PairId = summary.PairId,
            MetadataSyncStatus = summary.SyncStatus
        })).ToArray();
        return new PagedResult<PhotoViewModel>(items, page.NextCursor, page.TotalCount);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Photos.Dispose();
        GC.SuppressFinalize(this);
    }
}
