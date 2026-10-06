using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Infrastructure.ImageProcessing;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Core.Domain;
using PhotoFastRater.Infrastructure.Services;
using PhotoFastRater.Core.UI;
using PhotoFastRater.Infrastructure.Database.Repositories;
using PhotoFastRater.UI.Services;

namespace PhotoFastRater.UI.ViewModels;

/// <summary>Coordinates one isolated folder session; owns navigation feedback lifetime and releases decoded photos at scope close.</summary>
public partial class FolderModeViewModel : ViewModelBase, IDisposable
{
    private readonly FolderSessionService _sessionService;
    private readonly WindowManager _windowManager;
    private readonly PhotoRepository _photoRepository;
    private readonly IUserInteractionService _interaction;
    private readonly IPlatformShell _platformShell;
    private readonly ImageLoader _imageLoader;
    private readonly UIConfiguration _uiConfig;
    private readonly ExifService _exifService;
    private readonly RawThumbnailGenerator _rawGenerator;
    private readonly IEmbeddedMetadataWriter _embeddedMetadataWriter;

    public event Action? ShortcutsUpdated;
    public event Action<bool>? BoundaryReached;

    // フォルダモード設定
    private FolderModeSettingsViewModel _settings;

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

    // 元画像表示切替
    [ObservableProperty] private bool _showOriginalImages = false;

    // 写真アイテム表示モード
    [ObservableProperty] private bool _uniformPhotoSize = false;
    [ObservableProperty] private bool _nameLabelBelow = false;

    // インラインプレビュー
    [ObservableProperty] private bool _isInlinePreviewMode = false;

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

    partial void OnNameLabelBelowChanged(bool value) { _settings.NameLabelBelow = value; _settings.Save(); }
    partial void OnUniformPhotoSizeChanged(bool value) { _settings.UniformPhotoSize = value; _settings.Save(); }
    partial void OnShowOriginalImagesChanged(bool value) { _settings.ShowOriginalImages = value; _settings.Save(); }

    public void NotifyGridWidth(double width)
    {
        _gridWidth = width;
        UpdateGridColumns();
    }

    private void UpdateGridColumns() =>
        GridColumns = Math.Max(1, (int)(_gridWidth / (ThumbnailSize + 8)));

    public FolderModeViewModel(
        FolderSessionService sessionService,
        WindowManager windowManager,
        PhotoRepository photoRepository,
        IUserInteractionService interaction,
        IPlatformShell platformShell,
        ImageLoader imageLoader,
        UIConfiguration uiConfig,
        ExifService exifService,
        RawThumbnailGenerator rawGenerator,
        IEmbeddedMetadataWriter embeddedMetadataWriter,
        FolderModeSettingsViewModel settings)
    {
        _sessionService = sessionService;
        _windowManager = windowManager;
        _photoRepository = photoRepository;
        _interaction = interaction;
        _platformShell = platformShell;
        _imageLoader = imageLoader;
        _uiConfig = uiConfig;
        _exifService = exifService;
        _rawGenerator = rawGenerator;
        _embeddedMetadataWriter = embeddedMetadataWriter;
        _settings = settings;

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
        NameLabelBelow = _settings.NameLabelBelow;
        UniformPhotoSize = _settings.UniformPhotoSize;
        ShowOriginalImages = _settings.ShowOriginalImages;
    }

    private void RefreshDisplayPhotos()
    {
        if (FilterDateFrom.HasValue && FilterDateTo.HasValue && FilterDateFrom.Value.Date > FilterDateTo.Value.Date)
        {
            DisplayPhotos.Clear();
            StatusText = "撮影日の開始日は終了日以前にしてください。";
            return;
        }
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
            filtered = filtered.Where(p => p.DateTaken.Date <= FilterDateTo.Value.Date);

        // RAW+JPEGグループ化: ペアのJPEGがフィルター済みにあるRAWはスキップ、JPEGのIsExpanded=TrueのときRAWを直後に挿入
        var filteredList = filtered.ToList();
        var filteredSet = new HashSet<string>(filteredList.Select(p => p.FilePath));
        var result = new List<FolderSessionPhotoViewModel>();

        foreach (var photo in filteredList)
        {
            if (_settings.GroupRawJpeg && photo.HasPair && photo.IsRawFile
                && filteredSet.Contains(photo.PairedFilePath!))
            {
                photo.IsGroupedWithPair = false;
                continue;
            }

            photo.IsGroupedWithPair = false;
            result.Add(photo);

            if (_settings.GroupRawJpeg && photo.HasPair && !photo.IsRawFile && photo.IsExpanded)
            {
                var raw = Photos.FirstOrDefault(p => p.FilePath == photo.PairedFilePath);
                if (raw != null && filteredSet.Contains(raw.FilePath))
                {
                    raw.IsGroupedWithPair = true;
                    photo.IsGroupedWithPair = true;
                    result.Add(raw);
                }
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

            // キャッシュ済みセッションのレーティングを引継ぎ用に読む
            var cachedSession = await _sessionService.LoadSessionAsync(folderPath);

            // プログレッシブ表示: スキャン中に写真を逐次追加
            Photos.Clear();
            DisplayPhotos.Clear();

            var uiProgress = new Progress<FolderSessionPhoto>(photo =>
            {
                Func<string, Task<BitmapImage?>>? rawLoader = photo.IsRawFile ? LoadRawFullImageAsync : null;
                var photoVm = new FolderSessionPhotoViewModel(photo, rawLoader);
                Photos.Add(photoVm);
                DisplayPhotos.Add(photoVm);
            });

            var countProgress = new Progress<int>(count => StatusText = $"写真を読み込み中: {count}枚");

            // 常に新鮮なスキャンを実行
            var photos = await _sessionService.LoadPhotosAsync(folderPath, countProgress, uiProgress);

            // キャッシュされたレーティングを引き継ぐ
            if (cachedSession != null)
            {
                var ratingMap = cachedSession.Photos.ToDictionary(p => p.FilePath);
                foreach (var photo in photos)
                {
                    if (ratingMap.TryGetValue(photo.FilePath, out var cached))
                    {
                        photo.Rating = cached.Rating;
                        photo.IsFavorite = cached.IsFavorite;
                        photo.IsRejected = cached.IsRejected;
                    }
                }
                // VM にも反映
                foreach (var vm in Photos)
                {
                    if (ratingMap.TryGetValue(vm.FilePath, out var cached))
                    {
                        vm.Rating = cached.Rating;
                        vm.IsFavorite = cached.IsFavorite;
                        vm.IsRejected = cached.IsRejected;
                    }
                }
            }

            CurrentSession = new FolderSession
            {
                FolderPath = folderPath,
                CreatedDate = DateTime.Now,
                Photos = photos
            };

            // 全スキャン完了後にフィルター適用・統計更新
            RefreshDisplayPhotos();
            BuildPhotoTree();
            UpdateStatistics();
            StatusText = $"{TotalPhotos}枚の写真を読み込みました";
        }
        catch (Exception ex)
        {
            await _interaction.NotifyAsync("エラー", $"エラー: {ex.Message}", UserNotificationKind.Error);
            StatusText = "エラーが発生しました";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private DateTime _lastMemoryWarningTime = DateTime.MinValue;

    private bool CheckMemoryAndWarn()
    {
        long limitBytes = (long)_settings.MaxFullImageMemoryMB * 1024 * 1024;
        long usedBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        if (usedBytes < limitBytes) return true;

        if (_settings.ShowMemoryWarning && (DateTime.Now - _lastMemoryWarningTime).TotalSeconds > 60)
        {
            _lastMemoryWarningTime = DateTime.Now;
            System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                _ = _interaction.NotifyAsync(
                    "メモリ不足",
                    $"メモリ使用量が上限（{_settings.MaxFullImageMemoryMB}MB）に達しました。\n" +
                    "フルイメージの読み込みをスキップします。\n" +
                    "設定からメモリ上限を変更するか、フォルダを再読み込みしてください。",
                    UserNotificationKind.Warning);
            });
        }
        return false;
    }

    private void PrefetchNearbyFullImages(int selectedIndex)
    {
        if (!ShowOriginalImages) return;
        if (!CheckMemoryAndWarn()) return;
        for (int delta = -2; delta <= 2; delta++)
        {
            int idx = selectedIndex + delta;
            if (idx >= 0 && idx < DisplayPhotos.Count)
                DisplayPhotos[idx].TriggerFullImageLoad();
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

            // 原画像モードのとき隣接写真を先読み
            PrefetchNearbyFullImages(DisplayPhotos.IndexOf(photo));
        }
    }

    [RelayCommand]
    private void OpenPhoto(FolderSessionPhotoViewModel photo)
    {
        _platformShell.OpenFile(photo.FilePath);
    }

    [RelayCommand]
    private void OpenTreePhoto(PhotoViewModel photo)
    {
        _platformShell.OpenFile(photo.FilePath);
    }

    [RelayCommand]
    private async Task SetRatingAsync(string? ratingStr)
    {
        if (SelectedPhoto == null) return;
        if (!int.TryParse(ratingStr, out var rating) || rating is < 0 or > 5) return;

        SelectedPhoto.Rating = rating;
        SelectedPhoto.UpdateModel();
        SynchronizeGroupedRating();

        UpdateStatistics();
        await SaveSessionAsync();
    }

    [RelayCommand]
    private void OpenPhotoFolder(FolderSessionPhotoViewModel? photo)
    {
        var target = photo ?? SelectedPhoto;
        if (target == null) return;
        _platformShell.ShowInFileBrowser(target.FilePath);
    }

    [RelayCommand]
    private void CopyFileName(FolderSessionPhotoViewModel? photo)
    {
        var target = photo ?? SelectedPhoto;
        if (target != null)
            _platformShell.SetClipboardText(target.FileName);
    }

    [RelayCommand]
    private void CopyFilePath(FolderSessionPhotoViewModel? photo)
    {
        var target = photo ?? SelectedPhoto;
        if (target != null)
            _platformShell.SetClipboardText(target.FilePath);
    }

    [RelayCommand]
    private async Task ReloadFolderAsync()
    {
        if (string.IsNullOrEmpty(FolderPath)) return;
        var confirmed = await _interaction.ConfirmAsync(
            "再読み込みの確認",
            $"フォルダを再読み込みします。\n現在のセッションは上書きされます。\n\n{FolderPath}",
            CancellationToken.None);
        if (!confirmed) return;
        await LoadFolderAsync(FolderPath);
    }

    [RelayCommand]
    private async Task ReloadPhotoAsync(FolderSessionPhotoViewModel? photo)
    {
        if (photo == null) return;
        try
        {
            var exifPhoto = _exifService.ExtractExifData(photo.FilePath);
            photo.Rating = exifPhoto.Rating;
            photo.IsRejected = exifPhoto.IsRejected;
            photo.Thumbnail = null;
            await LoadThumbnailAsync(photo);
        }
        catch (Exception ex)
        {
            await _interaction.NotifyAsync("エラー", $"再読み込みエラー: {ex.Message}", UserNotificationKind.Error);
        }
    }

    [RelayCommand]
    private void OpenKeyboardShortcuts()
    {
        if (_windowManager.ShowKeyboardShortcutsDialog())
            ShortcutsUpdated?.Invoke();
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.IsFavorite = !SelectedPhoto.IsFavorite;
        SelectedPhoto.UpdateModel();
        SynchronizeGroupedRating();
        await SaveSessionAsync();
    }

    [RelayCommand]
    private async Task ToggleRejectAsync()
    {
        if (SelectedPhoto == null) return;
        SelectedPhoto.IsRejected = !SelectedPhoto.IsRejected;
        SelectedPhoto.UpdateModel();
        SynchronizeGroupedRating();
        await SaveSessionAsync();
    }

    private void SynchronizeGroupedRating()
    {
        if (!_settings.GroupRawJpeg || SelectedPhoto?.PairedFilePath is not string pairedPath) return;
        var pair = Photos.FirstOrDefault(photo => string.Equals(photo.FilePath, pairedPath, StringComparison.OrdinalIgnoreCase));
        if (pair is null) return;
        pair.Rating = SelectedPhoto.Rating; pair.IsFavorite = SelectedPhoto.IsFavorite; pair.IsRejected = SelectedPhoto.IsRejected;
        pair.UpdateModel();
    }

    [RelayCommand]
    private async Task WriteRatingToJpegAsync()
    {
        if (SelectedPhoto is null)
            return;

        if (!JpegExtensions.Contains(Path.GetExtension(SelectedPhoto.FilePath), StringComparer.OrdinalIgnoreCase))
        {
            await _interaction.NotifyAsync("JPEGメタデータ", "JPEGファイルだけが対象です。", UserNotificationKind.Warning);
            return;
        }

        var confirmed = await _interaction.ConfirmAsync(
            "JPEGメタデータへ書き込み",
            "現在の評価をJPEG内部へ明示的に書き込みます。画素の再圧縮は行いません。続行しますか？",
            CancellationToken.None);
        if (!confirmed)
            return;

        var state = new RatingState(SelectedPhoto.Rating, SelectedPhoto.IsFavorite, SelectedPhoto.IsRejected);
        var result = await _embeddedMetadataWriter.WriteRatingAsync(SelectedPhoto.FilePath, state);
        if (result.Succeeded)
        {
            StatusText = "JPEGメタデータを更新しました（画素は再圧縮していません）";
            return;
        }

        await _interaction.NotifyAsync(
            "JPEGメタデータを書き込めませんでした",
            result.ErrorMessage ?? "このJPEGは安全なインプレース更新に対応していません。元画像は変更していません。",
            UserNotificationKind.Warning);
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
            StatusText = "セッションを保存できませんでした。アクセス権と空き容量を確認して再保存してください。";
            await _interaction.NotifyAsync("エラー", $"保存エラー: {ex.Message}", UserNotificationKind.Error);
        }
    }

    [RelayCommand]
    private async Task ExportToDbAsync()
    {
        if (CurrentSession == null) return;

        var confirmed = await _interaction.ConfirmAsync(
            "確認",
            $"このセッションの写真をDBに追加しますか?\n登録済み写真の評価・お気に入り・リジェクトはセッションの値に置き換えます。\n合計: {TotalPhotos}枚",
            CancellationToken.None);

        if (!confirmed) return;

        IsLoading = true;
        StatusText = "DBにエクスポート中...";

        try
        {
            int importedCount = 0, updatedCount = 0, skippedCount = 0;

            // Snapshot values before awaiting so one confirmed transfer uses a consistent session state.
            var snapshot = CurrentSession.Photos.Select(sessionPhoto =>
            {
                return new Photo
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
            }).ToArray();
            foreach (var batch in snapshot.Chunk(256))
            {
                var result = await _photoRepository.TransferSessionBatchAsync(batch);
                importedCount += result.Added;
                updatedCount += result.Updated;
                skippedCount += result.Unchanged;
                StatusText = $"DBへ保存中: {importedCount + updatedCount + skippedCount}/{snapshot.Length}枚";
                await System.Windows.Threading.Dispatcher.Yield();
            }
            await _interaction.NotifyAsync(
                "完了",
                $"エクスポート完了\n新規: {importedCount}枚\n更新: {updatedCount}枚\nスキップ: {skippedCount}枚",
                UserNotificationKind.Information);
            StatusText = "エクスポート完了";
        }
        catch (Exception ex)
        {
            await _interaction.NotifyAsync("エラー", $"DBへの保存を完了できませんでした。完了済みの分は保持されています。再実行できます: {ex.Message}", UserNotificationKind.Error);
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
        var settingsVm = new FolderModeSettingsViewModel(new ApplicationPaths(Path.GetDirectoryName(_settings.SettingsPath)!));
        settingsVm.DefaultThumbnailSize = ThumbnailSize;
        settingsVm.ShowExifInItem = ShowExifInItem;
        settingsVm.ExifShowLens = ExifItemShowLens;
        settingsVm.ExifShowSettings = ExifItemShowSettings;
        settingsVm.ExifShowDate = ExifItemShowDate;
        settingsVm.ExifShowCamera = ExifItemShowCamera;
        settingsVm.GroupRawJpeg = _settings.GroupRawJpeg;
        settingsVm.ShowMemoryWarning = _settings.ShowMemoryWarning;
        settingsVm.MaxFullImageMemoryMB = _settings.MaxFullImageMemoryMB;
        settingsVm.NameLabelBelow = NameLabelBelow;
        settingsVm.UniformPhotoSize = UniformPhotoSize;
        settingsVm.ShowOriginalImages = ShowOriginalImages;

        var accepted = _windowManager.ShowFolderSettingsDialog(settingsVm);
        if (accepted)
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
        else
            NotifyBoundary(atStart: true);
    }

    [RelayCommand]
    private void NavigateRight()
    {
        if (!CanNavigate()) return;
        var currentIndex = DisplayPhotos.IndexOf(SelectedPhoto!);
        if (currentIndex < DisplayPhotos.Count - 1)
            SelectPhoto(DisplayPhotos[currentIndex + 1]);
        else
            NotifyBoundary(atStart: false);
    }

    [RelayCommand]
    private void NavigateHome()
    {
        if (DisplayPhotos.Count > 0)
            SelectPhoto(DisplayPhotos[0]);
    }

    [RelayCommand]
    private void NavigateEnd()
    {
        if (DisplayPhotos.Count > 0)
            SelectPhoto(DisplayPhotos[DisplayPhotos.Count - 1]);
    }

    private CancellationTokenSource? _boundaryCts;

    private async void NotifyBoundary(bool atStart)
    {
        BoundaryReached?.Invoke(atStart);
        _boundaryCts?.Cancel();
        _boundaryCts?.Dispose();
        _boundaryCts = new CancellationTokenSource();
        var token = _boundaryCts.Token;
        StatusText = atStart ? "◀ 最初の写真です" : "最後の写真です ▶";
        try
        {
            await Task.Delay(1500, token);
            if (!token.IsCancellationRequested)
                StatusText = $"{TotalPhotos}枚の写真";
        }
        catch (TaskCanceledException) { }
    }

    /// <summary>Ends transient boundary feedback and drops window-owned decoded images without touching saved session data.</summary>
    public void Dispose()
    {
        _boundaryCts?.Cancel();
        _boundaryCts?.Dispose();
        _boundaryCts = null;
        foreach (var photo in Photos)
        {
            photo.ClearFullImage();
            photo.Thumbnail = null;
        }
        GC.SuppressFinalize(this);
    }

    private bool CanNavigate()
    {
        if (_uiConfig.ArrowKeyNavigationMode == "SelectionOnly")
            return SelectedPhoto != null;

        if (SelectedPhoto == null && DisplayPhotos.Count > 0)
            SelectPhoto(DisplayPhotos[0]);

        return SelectedPhoto != null;
    }

    internal async Task LoadThumbnailAsync(FolderSessionPhotoViewModel photoVm)
    {
        try
        {
            var thumbnail = await _imageLoader.LoadAsync(photoVm.FilePath);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                photoVm.Thumbnail = thumbnail;
                if (thumbnail != null && thumbnail.PixelWidth > 0 && thumbnail.PixelHeight > 0)
                    photoVm.PhotoAspectRatio = (double)thumbnail.PixelHeight / thumbnail.PixelWidth;
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[FolderMode] サムネイル読み込みエラー: {photoVm.FilePath} - {ex.Message}");
        }
    }

    private async Task<BitmapImage?> LoadRawFullImageAsync(string filePath)
    {
        var bytes = await _rawGenerator.ExtractEmbeddedJpegBytesAsync(filePath);
        if (bytes.Length == 0) return null;
        return await Task.Run(() =>
        {
            var bmp = new BitmapImage();
            using var ms = new System.IO.MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        });
    }

    public void EvictNonVisibleIfNeeded(IReadOnlyCollection<FolderSessionPhotoViewModel> visiblePhotos)
    {
        long limitBytes = (long)_settings.MaxFullImageMemoryMB * 1024 * 1024;
        long usedBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
        if (usedBytes < limitBytes) return;

        var visibleSet = new HashSet<FolderSessionPhotoViewModel>(visiblePhotos);
        foreach (var photo in DisplayPhotos)
        {
            if (!visibleSet.Contains(photo))
                photo.ClearFullImage();
        }
    }

    public Task LoadThumbnailIfMissingAsync(FolderSessionPhotoViewModel photoVm)
    {
        if (photoVm.Thumbnail != null) return Task.CompletedTask;
        return LoadThumbnailAsync(photoVm);
    }

    internal async Task LoadTreeThumbnailAsync(PhotoViewModel photoVm)
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

            // 同一日付の写真を直接 Day ノードの Photos にフラットに格納
            foreach (var photoVm in dateGroup)
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
                dayNode.Photos.Add(treePhotoVm);
                // Tree items load on realization, rather than decoding every hidden photo at folder startup.
            }
        }
    }
}
