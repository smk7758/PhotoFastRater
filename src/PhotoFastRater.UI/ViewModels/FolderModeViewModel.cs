using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Core.Services;
using PhotoFastRater.Core.Database.Repositories;
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
    private readonly PhotoRepository _photoRepository;

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

    public FolderModeViewModel(
        FolderSessionService sessionService,
        PhotoRepository photoRepository)
    {
        _sessionService = sessionService;
        _photoRepository = photoRepository;
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
            foreach (var photo in CurrentSession.Photos)
            {
                Photos.Add(new FolderSessionPhotoViewModel(photo));
            }

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
            int importedCount = 0;
            int updatedCount = 0;
            int skippedCount = 0;

            foreach (var sessionPhoto in CurrentSession.Photos)
            {
                var existing = await _photoRepository.GetByFilePathAsync(sessionPhoto.FilePath);

                if (existing != null)
                {
                    // 既存写真のレーティング更新（より高いレーティングのみ）
                    if (sessionPhoto.Rating > existing.Rating)
                    {
                        existing.Rating = sessionPhoto.Rating;
                        existing.IsFavorite = sessionPhoto.IsFavorite;
                        existing.IsRejected = sessionPhoto.IsRejected;
                        await _photoRepository.UpdateAsync(existing);
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

                    await _photoRepository.AddAsync(photo);
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
}
