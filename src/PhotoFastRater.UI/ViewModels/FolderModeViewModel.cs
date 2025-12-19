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
using PhotoFastRater.UI.Views;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace PhotoFastRater.UI.ViewModels;

/// <summary>
/// フォルダモードのViewModel
/// </summary>
public partial class FolderModeViewModel : ViewModelBase
{
    private readonly FolderSessionService _sessionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ImageLoader _imageLoader;
    private readonly UIConfiguration _uiConfig;

    [ObservableProperty]
    private FolderSession? _currentSession;

    [ObservableProperty]
    private ObservableCollection<FolderSessionPhotoViewModel> _photos = new();

    [ObservableProperty]
    private FolderSessionPhotoViewModel? _selectedPhoto;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    private string _statusText = "フォルダを選択してください";

    [ObservableProperty]
    private bool _isLoading = false;

    [ObservableProperty]
    private int _totalPhotos;

    [ObservableProperty]
    private int _ratedPhotos;

    [ObservableProperty]
    private ObservableCollection<PhotoTreeNode> _photoTree = new();

    [ObservableProperty]
    private bool _isTreeViewMode;

    /// <summary>
    /// IsTreeViewMode が変更されたときの処理
    /// </summary>
    partial void OnIsTreeViewModeChanged(bool value)
    {
        if (value)
        {
            System.Diagnostics.Debug.WriteLine($"[FolderMode] IsTreeViewMode changed to true, calling BuildPhotoTree");
            BuildPhotoTree();
        }
    }

    // グリッドの列数（WrapPanelの列数）
    private const int GridColumns = 6;

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
    }

    /// <summary>
    /// フォルダを開く
    /// </summary>
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

    /// <summary>
    /// フォルダを読み込み
    /// </summary>
    public async Task LoadFolderAsync(string folderPath)
    {
        IsLoading = true;
        StatusText = "読み込み中...";

        try
        {
            FolderPath = folderPath;

            // セッションを作成または読み込み
            CurrentSession = await _sessionService.CreateSessionAsync(folderPath);

            if (CurrentSession.Photos.Count == 0)
            {
                // 新規セッション：写真を読み込み
                var progress = new Progress<int>(count =>
                {
                    StatusText = $"写真を読み込み中: {count}枚";
                });

                var photos = await _sessionService.LoadPhotosAsync(folderPath, progress);
                CurrentSession.Photos = photos;
            }

            // ViewModelに変換
            Photos.Clear();
            System.Diagnostics.Debug.WriteLine($"[FolderMode] 写真の読み込み開始: {CurrentSession.Photos.Count}枚");
            foreach (var photo in CurrentSession.Photos)
            {
                var photoVm = new FolderSessionPhotoViewModel(photo);
                Photos.Add(photoVm);
                System.Diagnostics.Debug.WriteLine($"[FolderMode] PhotosコレクションにViewModel追加: {Path.GetFileName(photo.FilePath)}");
                // サムネイルをバックグラウンドで非同期読み込み（待機しない）
                _ = LoadThumbnailAsync(photoVm);
            }
            System.Diagnostics.Debug.WriteLine($"[FolderMode] Photosコレクション準備完了: {Photos.Count}枚");

            // ツリービューを構築（サムネイルは非同期で読み込まれる）
            System.Diagnostics.Debug.WriteLine($"[FolderMode] BuildPhotoTree開始");
            BuildPhotoTree();
            System.Diagnostics.Debug.WriteLine($"[FolderMode] BuildPhotoTree完了");

            UpdateStatistics();
            StatusText = $"{TotalPhotos}枚の写真を読み込みました";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"エラー: {ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText = "エラーが発生しました";
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 写真を選択
    /// </summary>
    [RelayCommand]
    private void SelectPhoto(FolderSessionPhotoViewModel photo)
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

    /// <summary>
    /// 写真をビューアーで開く（グリッド表示用）
    /// </summary>
    [RelayCommand]
    private void OpenPhoto(FolderSessionPhotoViewModel photo)
    {
        // PhotoViewModelに変換してビューアーを開く
        var model = photo.GetModel();
        var photoModel = new Photo
        {
            FilePath = photo.FilePath,
            FileName = photo.FileName,
            DateTaken = model.DateTaken,
            Rating = photo.Rating,
            IsFavorite = photo.IsFavorite,
            IsRejected = photo.IsRejected,
            CameraModel = model.CameraModel,
            LensModel = model.LensModel,
            ISO = model.ISO,
            Aperture = model.Aperture,
            ShutterSpeed = model.ShutterSpeed,
            FocalLength = model.FocalLength
        };
        var photoVm = new PhotoViewModel(photoModel)
        {
            Rating = photo.Rating,
            IsFavorite = photo.IsFavorite,
            IsRejected = photo.IsRejected,
            Thumbnail = photo.Thumbnail
        };
        var viewer = new PhotoViewerWindow(photoVm);
        viewer.ShowDialog();
    }

    /// <summary>
    /// 写真をビューアーで開く（ツリー表示用）
    /// </summary>
    [RelayCommand]
    private void OpenTreePhoto(PhotoViewModel photo)
    {
        var viewer = new PhotoViewerWindow(photo);
        viewer.ShowDialog();
    }

    /// <summary>
    /// レーティングを設定
    /// </summary>
    [RelayCommand]
    private async Task SetRatingAsync(int rating)
    {
        if (SelectedPhoto == null) return;

        SelectedPhoto.Rating = rating;
        SelectedPhoto.UpdateModel();

        UpdateStatistics();
        await SaveSessionAsync();
    }

    /// <summary>
    /// お気に入りを切り替え
    /// </summary>
    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedPhoto == null) return;

        SelectedPhoto.IsFavorite = !SelectedPhoto.IsFavorite;
        SelectedPhoto.UpdateModel();

        await SaveSessionAsync();
    }

    /// <summary>
    /// リジェクトを切り替え
    /// </summary>
    [RelayCommand]
    private async Task ToggleRejectAsync()
    {
        if (SelectedPhoto == null) return;

        SelectedPhoto.IsRejected = !SelectedPhoto.IsRejected;
        SelectedPhoto.UpdateModel();

        await SaveSessionAsync();
    }

    /// <summary>
    /// セッションを保存
    /// </summary>
    [RelayCommand]
    private async Task SaveSessionAsync()
    {
        if (CurrentSession == null) return;

        try
        {
            // ViewModelの変更をモデルに反映
            foreach (var photoVm in Photos)
            {
                photoVm.UpdateModel();
            }

            await _sessionService.SaveSessionAsync(CurrentSession);
            StatusText = "セッションを保存しました";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"保存エラー: {ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// DBにエクスポート
    /// </summary>
    [RelayCommand]
    private async Task ExportToDbAsync()
    {
        if (CurrentSession == null) return;

        var result = MessageBox.Show(
            $"このセッションの写真をDBに追加しますか?\n合計: {TotalPhotos}枚",
            "確認",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        StatusText = "DBにエクスポート中...";

        try
        {
            // PhotoRepositoryをサービスプロバイダーから取得
            var photoRepository = _serviceProvider.GetRequiredService<PhotoRepository>();

            int importedCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;

            foreach (var sessionPhoto in CurrentSession.Photos)
            {
                var existing = await photoRepository.GetByFilePathAsync(sessionPhoto.FilePath);

                if (existing != null)
                {
                    // 既存写真のレーティング更新（より高いレーティングのみ）
                    if (sessionPhoto.Rating > existing.Rating)
                    {
                        existing.Rating = sessionPhoto.Rating;
                        existing.IsFavorite = sessionPhoto.IsFavorite;
                        existing.IsRejected = sessionPhoto.IsRejected;
                        await photoRepository.UpdateAsync(existing);
                        updatedCount++;
                    }
                    else
                    {
                        skippedCount++;
                    }
                }
                else
                {
                    // 新規追加（EXIF情報も含めて）
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
                "完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            StatusText = "エクスポート完了";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"エクスポートエラー: {ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 統計情報を更新
    /// </summary>
    private void UpdateStatistics()
    {
        if (CurrentSession != null)
        {
            TotalPhotos = CurrentSession.TotalPhotos;
            RatedPhotos = CurrentSession.RatedPhotos;
        }
    }

    /// <summary>
    /// ペアファイルを表示
    /// </summary>
    [RelayCommand]
    private void ShowPairedFile()
    {
        if (SelectedPhoto == null || !SelectedPhoto.HasPair) return;

        // ペアファイルを探して選択
        var pairedPhoto = Photos.FirstOrDefault(p => p.FilePath == SelectedPhoto.PairedFilePath);
        if (pairedPhoto != null)
        {
            SelectedPhoto = pairedPhoto;
        }
    }

    /// <summary>
    /// 上に移動
    /// </summary>
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

    /// <summary>
    /// 下に移動
    /// </summary>
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

    /// <summary>
    /// 左に移動
    /// </summary>
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

    /// <summary>
    /// 右に移動
    /// </summary>
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

    /// <summary>
    /// ナビゲーションが可能かチェック
    /// </summary>
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

    /// <summary>
    /// サムネイルを非同期で読み込み
    /// </summary>
    private async Task LoadThumbnailAsync(FolderSessionPhotoViewModel photoVm)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[FolderMode] LoadThumbnailAsync 開始: {Path.GetFileName(photoVm.FilePath)}");

            var thumbnail = await _imageLoader.LoadAsync(photoVm.FilePath);

            System.Diagnostics.Debug.WriteLine($"[FolderMode] サムネイル取得完了: {Path.GetFileName(photoVm.FilePath)}, IsNull={thumbnail == null}");

            // UI スレッドでプロパティを更新
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Diagnostics.Debug.WriteLine($"[FolderMode] UIスレッドでThumbnail設定: {Path.GetFileName(photoVm.FilePath)}");
                photoVm.Thumbnail = thumbnail;
                System.Diagnostics.Debug.WriteLine($"[FolderMode] Thumbnail設定完了: {Path.GetFileName(photoVm.FilePath)}, photoVm.Thumbnail IsNull={photoVm.Thumbnail == null}");
            });
        }
        catch (Exception ex)
        {
            // サムネイル読み込みエラーは無視（写真は表示される）
            System.Diagnostics.Debug.WriteLine($"[FolderMode] サムネイル読み込みエラー: {photoVm.FilePath} - {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[FolderMode] スタックトレース: {ex.StackTrace}");
        }
    }

    /// <summary>
    /// ツリー用PhotoViewModelのサムネイルを非同期で読み込み
    /// </summary>
    private async Task LoadTreeThumbnailAsync(PhotoViewModel photoVm)
    {
        try
        {
            var thumbnail = await _imageLoader.LoadAsync(photoVm.FilePath);

            // UI スレッドでプロパティを更新
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                photoVm.Thumbnail = thumbnail;
            });
        }
        catch (Exception ex)
        {
            // サムネイル読み込みエラーは無視
            System.Diagnostics.Debug.WriteLine($"[FolderMode.Tree] サムネイル読み込みエラー: {photoVm.FilePath} - {ex.Message}");
        }
    }

    /// <summary>
    /// 写真ツリーを構築
    /// </summary>
    public void BuildPhotoTree()
    {
        PhotoTree.Clear();

        // 日付でグループ化（降順）
        var photosByDate = Photos
            .GroupBy(p => p.GetModel().DateTaken.Date)
            .OrderByDescending(g => g.Key)
            .ToList();

        foreach (var dateGroup in photosByDate)
        {
            var date = dateGroup.Key;

            // 年ノードを探すまたは作成
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

            // 月ノードを探すまたは作成
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

            // 日ノードを作成
            var dayNode = new PhotoTreeNode
            {
                NodeType = TreeNodeType.Day,
                Year = date.Year,
                Month = date.Month,
                Day = date.Day,
                DisplayName = $"{date.Day}日"
            };
            monthNode.Children.Add(dayNode);

            // 日内でフォルダパスごとにグループ化
            var photosByFolder = dateGroup
                .GroupBy(p => Path.GetDirectoryName(p.FilePath) ?? "")
                .ToList();

            foreach (var folderGroup in photosByFolder)
            {
                var folderPath = folderGroup.Key;
                var folderName = !string.IsNullOrEmpty(folderPath)
                    ? Path.GetFileName(folderPath)
                    : "(フォルダなし)";

                // フォルダノードを作成
                var folderNode = new PhotoTreeNode
                {
                    NodeType = TreeNodeType.Folder,
                    Year = date.Year,
                    Month = date.Month,
                    Day = date.Day,
                    FolderPath = folderPath,
                    DisplayName = folderName
                };

                // 写真を追加（Photo modelから PhotoViewModelを作成）
                foreach (var photoVm in folderGroup)
                {
                    // Photo modelを作成してPhotoViewModelを生成
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

                    // ツリー用PhotoViewModelにもサムネイルを非同期で読み込む
                    _ = LoadTreeThumbnailAsync(treePhotoVm);
                }

                dayNode.Children.Add(folderNode);
            }
        }
    }
}
