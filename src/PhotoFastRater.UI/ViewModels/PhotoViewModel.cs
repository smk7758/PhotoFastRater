using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.UI.ViewModels;

public partial class PhotoViewModel : ViewModelBase
{
    private readonly Photo _photo;

    private BitmapImage? _thumbnail;

    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        set
        {
            System.Diagnostics.Debug.WriteLine($"[PhotoVM] Thumbnail setter called for {FileName}, IsNull={value == null}");
            if (_thumbnail != value)
            {
                _thumbnail = value;
                OnPropertyChanged(nameof(Thumbnail));
                System.Diagnostics.Debug.WriteLine($"[PhotoVM] OnPropertyChanged(nameof(Thumbnail)) called for {FileName}");
            }
            System.Diagnostics.Debug.WriteLine($"[PhotoVM] Thumbnail setter completed for {FileName}");
        }
    }

    [ObservableProperty]
    private BitmapImage? _fullImageSource;

    [ObservableProperty]
    private int _rating;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isRejected;

    [ObservableProperty]
    private bool _isSelected;

    public PhotoViewModel(Photo photo)
    {
        _photo = photo;
        _rating = photo.Rating;
        _isFavorite = photo.IsFavorite;
        _isRejected = photo.IsRejected;
    }

    public int Id => _photo.Id;
    public string FilePath => _photo.FilePath;
    public string FileName => _photo.FileName;
    public DateTime DateTaken => _photo.DateTaken;
    public string? CameraModel => _photo.CameraModel;
    public string? LensModel => _photo.LensModel;
    public int? ISO => _photo.ISO;
    public double? Aperture => _photo.Aperture;
    public string? ShutterSpeed => _photo.ShutterSpeed;
    public double? FocalLength => _photo.FocalLength;

    public Photo GetModel() => _photo;
}
