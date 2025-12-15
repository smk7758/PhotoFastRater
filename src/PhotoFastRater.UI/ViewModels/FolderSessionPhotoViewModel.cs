using CommunityToolkit.Mvvm.ComponentModel;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.UI.ViewModels;

/// <summary>
/// フォルダセッション写真のViewModel
/// </summary>
public partial class FolderSessionPhotoViewModel : ViewModelBase
{
    private readonly FolderSessionPhoto _photo;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private int _rating;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isRejected;

    [ObservableProperty]
    private string? _cameraModel;

    [ObservableProperty]
    private object? _thumbnail;

    public FolderSessionPhotoViewModel(FolderSessionPhoto photo)
    {
        _photo = photo;
        FilePath = photo.FilePath;
        FileName = photo.FileName;
        Rating = photo.Rating;
        IsFavorite = photo.IsFavorite;
        IsRejected = photo.IsRejected;
        CameraModel = photo.CameraModel;
    }

    /// <summary>
    /// モデルに変更を反映
    /// </summary>
    public void UpdateModel()
    {
        _photo.Rating = Rating;
        _photo.IsFavorite = IsFavorite;
        _photo.IsRejected = IsRejected;
    }

    /// <summary>
    /// 元のモデルを取得
    /// </summary>
    public FolderSessionPhoto GetModel() => _photo;
}
