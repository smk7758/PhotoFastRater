using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Core.Services;
using PhotoFastRater.Core.UI;
using PhotoFastRater.Core.Database.Repositories;
using PhotoFastRater.UI.Services;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace PhotoFastRater.UI.ViewModels;

public partial class FolderModeViewModel : ViewModelBase
{
    private readonly FolderSessionService _sessionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ImageLoader _imageLoader;
    private readonly UIConfiguration _uiConfig;

    // フォルダモード設定
    private FolderModeSettingsViewModel _settings = new();

    private static readonly string[] RawExtensions = { ".raw", ".cr2", ".cr3", ".nef", ".arw", ".dng", ".orf", ".raf", ".rw2" };
    private static readonly string[] JpegExtensions = { ".jpg", ".jpeg" };

    [ObservableProperty] private FolderSession? _currentSession;
    [ObservableProperty] private ObservableCollection<FolderSessionPhotoViewModel> _photos = new();
    [ObservableProperty] private FolderSessionPhotoViewModel? _selectedPhoto;
    [ObservableProperty] private string _folderPath = string.Empty;
    [ObservableProperty] private string _statusText = "フォルダを選択してください";
    [ObservableProperty] private bool _isLoading = false;
    [ObservableProperty] private int _totalPhotos;
    [ObservableProperty] private int _ratedPhotos;
    [ObservableProperty] private ObservableCollection<PhotoTreeNode> _photoTree = new();
    [ObservableProperty] private bool _isTreeViewMode;

    // サムネイルサイズ
    [ObservableProperty] private int _thumbnailSize = 200;
    private double _gridWidth = 1200;
    private int _gridColumns = 6;
    public int GridColumns { get => _gridColumns; private set => SetProperty(ref _gridColumns, value); }

    // フィルター
    [ObservableProperty] private int _filterMinRating = 0;
    [ObservableProperty] private bool _filterUnratedOnly = false;
    [ObservableProperty] private string _filterNameSearch = string.Empty;
    [ObservableProperty] private string _filterFileType = "all";
    [ObservableProperty] private DateTime? _filterDateFrom = null;
    [ObservableProperty] private DateTime? _filterDateTo = null;
    [ObservableProperty] private bool _isFilterPanelOpen = false;

    // 写真アイテムのEXIF表示設定
    [ObservableProperty] private bool _showExifInItem = false;
    [ObservableProperty] private bool _exifItemShowLens = true;
    [ObservableProperty] private bool _exifItemShowSettings = true;
    [ObservableProperty] private bool _exifItemShowDate = false;
    [ObservableProperty] private bool _exifItemShowCamera = false;

    // フィルター適用済み表示コレクション
    public ObservableCollection<FolderSessionPhotoViewModel> DisplayPhotos { get; } = new();

    partial void OnIsTreeViewModeChanged(bool value)
    {
        if (value) BuildPhotoTree();
    }

    partial void OnThumbnailSizeChanged(int value) => UpdateGridColumns();
    partial void OnFilterMinRatingChanged(int value) => RefreshDisplayPhotos();
    partial void OnFilterUnratedOnlyChanged(bool value) => RefreshDisplayPhotos();
    partial void OnFilterNameSearchChanged(string value) => RefreshDisplayPhotos();
    partial void OnFilterFileTypeChanged(string value) => RefreshDisplayPhotos();
    partial void OnFilterDateFromChanged(DateTime? value) => RefreshDisplayPhotos();
    partial void OnFilterDateToChanged(DateTime? value) => RefreshDisplayPhotos();

    public void NotifyGridWidth(double width)
    {
        _gridWidth = width;
        UpdateGridColumns();
    }

    private void UpdateGridColumns() =>
        GridColumns = Math.Max(1, (int)(_gridWidth / (ThumbnailSize + 8)));

    public FolderModeViewModel(
        FolderSessionService sessionService,
        IServiceProvider serviceProvider,
        ImageLoader imageLoader,
        UIConfiguration uiConfig)
    {
        _sessionService = sessionService;
        _serviceProvider = serviceProvider;
        _imageLoader = imageLoader;
        _uiConfig = uiConfig;

        _settings.Load();
        ApplySettingsToViewModel();
    }

    private void ApplySettingsToViewModel()
    {
        ThumbnailSize = _settings.DefaultThumbnailSize;
        ShowExifInItem = _settings.ShowExifInItem;
        ExifItemShowLens = _settings.ExifShowLens;
        ExifItemShowSettings = _settings.ExifShowSettings;
        ExifItemShowDate = _settings.ExifShowDate;
        ExifItemShowCamera = _settings.ExifShowCamera;
    }

    private void RefreshDisplayPhotos()
    {
        var filtered = Photos.AsEnumerable();

        if (!string.IsNullOrEmpty(FilterNameSearch))
            filtered = filtered.Where(p => p.FileName.Contains(FilterNameSearch, StringComparison.OrdinalIgnoreCase));

        if (FilterMinRating > 0)
            filtered = filtered.Where(p => p.Rating >= FilterMinRating);

        if (FilterUnratedOnly)
            filtered = filtered.Where(p => p.Rating == 0 && !p.IsRejected);

        if (FilterFileType == "raw")
            filtered = filtered.Where(p => RawExtensions.Contains(Path.GetExtension(p.FilePath).ToLowerInvariant()));
        else if (FilterFileType == "jpeg")
            filtered = filtered.Where(p => JpegExtensions.Contains(Path.GetExtension(p.FilePath).ToLowerInvariant()));

        if (FilterDateFrom.HasValue)
            filtered = filtered.Where(p => p.DateTaken >= FilterDateFrom.Value);

        if (FilterDateTo.HasValue)
            filtered = filtered.Where(p => p.DateTaken < FilterDateTo.Value.AddDays(1));

        // RAW+JPEGグループ化: RAWは非表示、JPEGのIsExpanded=trueの場合のみ追加
        var result = new List<FolderSessionPhotoViewModel>();
        foreach (var photo in filtered)
        {
            if (_settings.GroupRawJpeg && photo.HasPair && photo.IsRawFile)
            {
                var primary = Photos.FirstOrDefault(p => p.FilePath == photo.PairedFilePath);
                if (primary?.IsExpanded == true)
                    result.Add(photo);
            }
            else
            {
                result.Add(photo);
            }
        }

        DisplayPhotos.Clear();
        foreach (var p in result)
            DisplayPhotos.Add(p);
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "写真フォルダを選択してください"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            await LoadFolderAsync(dialog.SelectedPath);
        }
    }

    public async Task LoadFolderAsync(string folderPath)
    {
        IsLoading = true;
        StatusText = "読み込み中...";

        try
        {
            FolderPath = folderPath;
            CurrentSession = await _sessionService.CreateSessionAsync(folderPath);

            if (CurrentSession.Photos.Count == 0)
            {
                var progress = new Progress<int>(count =>
                {
                    StatusText = $"写真を読み込み中: {count}枚";
                });
                var photos = await _sessionService.LoadPhotosAsync(folderPath, progress);
                CurrentSession.Photos = photos;
            }

            Photos.Clear();
            foreach (var photo in CurrentSession.Photos)
            {
                var photoVm = new FolderSessionPhotoViewModel(photo);
                Photos.Add(photoVm);
                _ = LoadThumbnailAsync(photoVm);
            }

            RefreshDisplayPhotos();
            BuildPhotoTree();
            UpdateStatistics();
            StatusText = $"{TotalPhotos}枚の写真を読み込みました";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "エラーが発生しました";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectPhoto(FolderSessionPhotoViewModel photo)
    {
        if (SelectedPhoto != null)
            SelectedPhoto.IsSelected = false;

        SelectedPhoto = photo;
        if (photo != null)
        {
            photo.IsSelected = true;

            // JPEG primaryをクリックで展開トグル（グループ化有効時のみ）
            if (_settings.GroupRawJpeg && photo.HasPair && !photo.IsRawFile)
            {
                photo.IsExpanded = !photo.IsExpanded;
                RefreshDisplayPhotos();
            }
        }
    }

    [RelayCommand]
    private void OpenPhoto(FolderSessionPhotoViewModel photo)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(photo.FilePath)
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void OpenTreePhoto(PhotoViewModel photo)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(photo.FilePath)
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task SetRatingAsync(string? ratingStr)
    {
        if (SelectedPhoto == null) return;
        if (!int.TryParse(ratingStr, out var rating)) return;

        SelectedPhoto.Rating = rating;
        SelectedPhoto.UpdateModel();

        UpdateStatistics();
        await SaveSessionAsync();
    }

    [RelayCommand]
    private void OpenPhotoFolder()
    {
        if (SelectedPhoto == null) return;
        System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{SelectedPhoto.FilePath}\"");
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.IsFavorite = !SelectedPhoto.IsFavorite;
        SelectedPhoto.UpdateModel();
        await SaveSessionAsync();
    }

    [RelayCommand]
    private async Task ToggleRejectAsync()
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.IsRejected = !SelectedPhoto.IsRejected;
        SelectedPhoto.UpdateModel();
        await SaveSessionAsync();
    }

    [RelayCommand]
    private async Task SaveSessionAsync()
    {
        if (CurrentSession == null) return;
        try
        {
            foreach (var photoVm in Photos)
                photoVm.UpdateModel();
            await _sessionService.SaveSessionAsync(CurrentSession);
            StatusText = "セッションを保存しました";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存エラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task ExportToDbAsync()
    {
        if (CurrentSession == null) return;

        var result = MessageBox.Show(
            $"このセッションの写真をDBに追加しますか?\n合計: {TotalPhotos}枚",
            "確認", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        StatusText = "DBにエクスポート中...";

        try
        {
            var photoRepository = _serviceProvider.GetRequiredService<PhotoRepository>();
            int importedCount = 0, updatedCount = 0, skippedCount = 0;

            foreach (var sessionPhoto in CurrentSession.Photos)
            {
                var existing = await photoRepository.GetByFilePathAsync(sessionPhoto.FilePath);
                if (existing != null)
                {
                    if (sessionPhoto.Rating > existing.Rating)
                    {
                        existing.Rating = sessionPhoto.Rating;
                        existing.IsFavorite = sessionPhoto.IsFavorite;
                        existing.IsRejected = sessionPhoto.IsRejected;
                        await photoRepository.UpdateAsync(existing);
                        updatedCount++;
                    }
                    else skippedCount++;
                }
                else
                {
                    var photo = new Photo
                    {
                        FilePath = sessionPhoto.FilePath,
                        FileName = sessionPhoto.FileName,
                        FileSize = sessionPhoto.FileSize,
                        DateTaken = sessionPhoto.DateTaken,
                        ImportDate = DateTime.Now,
                        Rating = sessionPhoto.Rating,
                        IsFavorite = sessionPhoto.IsFavorite,
                        IsRejected = sessionPhoto.IsRejected,
                        Width = sessionPhoto.Width,
                        Height = sessionPhoto.Height,
                        CameraModel = sessionPhoto.CameraModel,
                        Aperture = sessionPhoto.Aperture,
                        ShutterSpeed = sessionPhoto.ShutterSpeed,
                        ISO = sessionPhoto.ISO,
                        FocalLength = sessionPhoto.FocalLength
                    };
                    await photoRepository.AddAsync(photo);
                    importedCount++;
                }
            }

            MessageBox.Show(
                $"エクスポート完了\n新規: {importedCount}枚\n更新: {updatedCount}枚\nスキップ: {skippedCount}枚",
                "完了", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText = "エクスポート完了";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"エクスポートエラー: {ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// フィルターをリセット
    /// </summary>
    [RelayCommand]
    private void ResetFilter()
    {
        FilterMinRating = 0;
        FilterUnratedOnly = false;
        FilterNameSearch = string.Empty;
        FilterFileType = "all";
        FilterDateFrom = null;
        FilterDateTo = null;
    }

    /// <summary>
    /// フォルダモード設定画面を開く
    /// </summary>
    [RelayCommand]
    private void OpenSettings()
    {
        var settingsVm = _serviceProvider.GetRequiredService<FolderModeSettingsViewModel>();
        settingsVm.DefaultThumbnailSize = ThumbnailSize;
        settingsVm.ShowExifInItem = ShowExifInItem;
        settingsVm.ExifShowLens = ExifItemShowLens;
        settingsVm.ExifShowSettings = ExifItemShowSettings;
        settingsVm.ExifShowDate = ExifItemShowDate;
        settingsVm.ExifShowCamera = ExifItemShowCamera;
        settingsVm.GroupRawJpeg = _settings.GroupRawJpeg;

        var window = _serviceProvider.GetRequiredService<Views.FolderModeSettingsWindow>();
        // DataContextをsettingsVmに差し替え
        window.DataContext = settingsVm;

        if (window.ShowDialog() == true)
        {
            // 設定をViewModelに反映
            _settings = settingsVm;
            ThumbnailSize = settingsVm.DefaultThumbnailSize;
            ShowExifInItem = settingsVm.ShowExifInItem;
            ExifItemShowLens = settingsVm.ExifShowLens;
            ExifItemShowSettings = settingsVm.ExifShowSettings;
            ExifItemShowDate = settingsVm.ExifShowDate;
            ExifItemShowCamera = settingsVm.ExifShowCamera;

            // RAW+JPEGグループ化設定変更時は表示を更新
            RefreshDisplayPhotos();
        }
    }

    private void UpdateStatistics()
    {
        if (CurrentSession != null)
        {
            TotalPhotos = CurrentSession.TotalPhotos;
            RatedPhotos = CurrentSession.RatedPhotos;
        }
    }

    [RelayCommand]
    private void ShowPairedFile()
    {
        if (SelectedPhoto == null || !SelectedPhoto.HasPair) return;
        var pairedPhoto = Photos.FirstOrDefault(p => p.FilePath == SelectedPhoto.PairedFilePath);
        if (pairedPhoto != null)
            SelectPhoto(pairedPhoto);
    }

    [RelayCommand]
    private void NavigateUp()
    {
        if (!CanNavigate()) return;
        var currentIndex = DisplayPhotos.IndexOf(SelectedPhoto!);
        var targetIndex = currentIndex - GridColumns;
        if (targetIndex >= 0)
            SelectPhoto(DisplayPhotos[targetIndex]);
    }

    [RelayCommand]
    private void NavigateDown()
    {
        if (!CanNavigate()) return;
        var currentIndex = DisplayPhotos.IndexOf(SelectedPhoto!);
        var targetIndex = currentIndex + GridColumns;
        if (targetIndex < DisplayPhotos.Count)
            SelectPhoto(DisplayPhotos[targetIndex]);
    }

    [RelayCommand]
    private void NavigateLeft()
    {
        if (!CanNavigate()) return;
        var currentIndex = DisplayPhotos.IndexOf(SelectedPhoto!);
        if (currentIndex > 0)
            SelectPhoto(DisplayPhotos[currentIndex - 1]);
    }

    [RelayCommand]
    private void NavigateRight()
    {
        if (!CanNavigate()) return;
        var currentIndex = DisplayPhotos.IndexOf(SelectedPhoto!);
        if (currentIndex < DisplayPhotos.Count - 1)
            SelectPhoto(DisplayPhotos[currentIndex + 1]);
    }

    private bool CanNavigate()
    {
        if (_uiConfig.ArrowKeyNavigationMode == "SelectionOnly")
            return SelectedPhoto != null;

        if (SelectedPhoto == null && DisplayPhotos.Count > 0)
            SelectPhoto(DisplayPhotos[0]);

        return SelectedPhoto != null;
    }

    private async Task LoadThumbnailAsync(FolderSessionPhotoViewModel photoVm)
    {
        try
        {
            var thumbnail = await _imageLoader.LoadAsync(photoVm.FilePath);
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                photoVm.Thumbnail = thumbnail;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FolderMode] サムネイル読み込みエラー: {photoVm.FilePath} - {ex.Message}");
        }
    }

    private async Task LoadTreeThumbnailAsync(PhotoViewModel photoVm)
    {
        try
        {
            var thumbnail = await _imageLoader.LoadAsync(photoVm.FilePath);
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                photoVm.Thumbnail = thumbnail;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FolderMode.Tree] サムネイル読み込みエラー: {photoVm.FilePath} - {ex.Message}");
        }
    }

    public void BuildPhotoTree()
    {
        PhotoTree.Clear();

        var photosByDate = Photos
            .GroupBy(p => p.GetModel().DateTaken.Date)
            .OrderByDescending(g => g.Key)
            .ToList();

        foreach (var dateGroup in photosByDate)
        {
            var date = dateGroup.Key;

            var yearNode = PhotoTree.FirstOrDefault(n => n.Year == date.Year);
            if (yearNode == null)
            {
                yearNode = new PhotoTreeNode
                {
                    NodeType = TreeNodeType.Year,
                    Year = date.Year,
                    DisplayName = $"{date.Year}年"
                };
                PhotoTree.Add(yearNode);
            }

            var monthNode = yearNode.Children.FirstOrDefault(n => n.Month == date.Month);
            if (monthNode == null)
            {
                monthNode = new PhotoTreeNode
                {
                    NodeType = TreeNodeType.Month,
                    Year = date.Year,
                    Month = date.Month,
                    DisplayName = $"{date.Month}月"
                };
                yearNode.Children.Add(monthNode);
            }

            var dayNode = new PhotoTreeNode
            {
                NodeType = TreeNodeType.Day,
                Year = date.Year,
                Month = date.Month,
                Day = date.Day,
                DisplayName = $"{date.Day}日"
            };
            monthNode.Children.Add(dayNode);

            var photosByFolder = dateGroup
                .GroupBy(p => Path.GetDirectoryName(p.FilePath) ?? "")
                .ToList();

            foreach (var folderGroup in photosByFolder)
            {
                var folderPath = folderGroup.Key;
                var folderName = !string.IsNullOrEmpty(folderPath) ? Path.GetFileName(folderPath) : "(フォルダなし)";

                var folderNode = new PhotoTreeNode
                {
                    NodeType = TreeNodeType.Folder,
                    Year = date.Year,
                    Month = date.Month,
                    Day = date.Day,
                    FolderPath = folderPath,
                    DisplayName = folderName
                };

                foreach (var photoVm in folderGroup)
                {
                    var photoModel = new Photo
                    {
                        FilePath = photoVm.FilePath,
                        FileName = photoVm.FileName,
                        DateTaken = photoVm.GetModel().DateTaken,
                        Rating = photoVm.Rating,
                        IsFavorite = photoVm.IsFavorite,
                        IsRejected = photoVm.IsRejected,
                        CameraModel = photoVm.CameraModel
                    };

                    var treePhotoVm = new PhotoViewModel(photoModel)
                    {
                        Rating = photoVm.Rating,
                        IsFavorite = photoVm.IsFavorite,
                        IsRejected = photoVm.IsRejected,
                        Thumbnail = photoVm.Thumbnail
                    };
                    folderNode.Photos.Add(treePhotoVm);
                    _ = LoadTreeThumbnailAsync(treePhotoVm);
                }

                dayNode.Children.Add(folderNode);
            }
        }
    }
}
