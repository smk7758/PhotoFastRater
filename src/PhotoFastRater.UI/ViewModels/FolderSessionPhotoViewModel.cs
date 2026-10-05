using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
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

    public string? LensModel => _photo.LensModel;
    public double? Aperture => _photo.Aperture;
    public string? ShutterSpeed => _photo.ShutterSpeed;
    public int? ISO => _photo.ISO;
    public double? FocalLength => _photo.FocalLength;
    public double? ExposureCompensation => _photo.ExposureCompensation;
    public DateTime DateTaken => _photo.DateTaken;
    public int Width => _photo.Width;
    public int Height => _photo.Height;

    /// <summary>
    /// 表示用アスペクト比（高さ/幅）。サムネイルロード後にAutoOrient済み実寸で更新される。
    /// </summary>
    [ObservableProperty]
    private double _photoAspectRatio = 1.0;

    private readonly Func<string, Task<BitmapImage?>>? _rawFullImageLoader;
    private BitmapImage? _thumbnail;
    private BitmapImage? _fullImage;
    private bool _fullImageLoading;

    public bool IsLoadingFullImage => _fullImageLoading;
    public bool IsThumbnailLoading => _thumbnail == null;
    public bool HasFullImage => _fullImage != null;

    public BitmapImage? FullImage => _fullImage;

    public void TriggerFullImageLoad()
    {
        if (_fullImage == null && !_fullImageLoading)
            _ = LoadFullImageAsync();
    }

    public void ClearFullImage()
    {
        if (_fullImage == null) return;
        _fullImage = null;
        _fullImageLoading = false;
        OnPropertyChanged(nameof(FullImage));
        OnPropertyChanged(nameof(HasFullImage));
        OnPropertyChanged(nameof(IsLoadingFullImage));
    }

    private async Task LoadFullImageAsync()
    {
        if (IsRawFile)
        {
            if (_rawFullImageLoader == null) return;
            _fullImageLoading = true;
            OnPropertyChanged(nameof(IsLoadingFullImage));
            try
            {
                _fullImage = await _rawFullImageLoader(FilePath);
                OnPropertyChanged(nameof(FullImage));
                OnPropertyChanged(nameof(HasFullImage));
            }
            catch { }
            finally
            {
                _fullImageLoading = false;
                OnPropertyChanged(nameof(IsLoadingFullImage));
            }
            return;
        }

        _fullImageLoading = true;
        OnPropertyChanged(nameof(IsLoadingFullImage));
        try
        {
            var path = FilePath;
            var bmp = await Task.Run(() =>
            {
                // EXIF orientation を読んで回転を適用
                int orientation = 1;
                try
                {
                    var dirs = ImageMetadataReader.ReadMetadata(path);
                    var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
                    ifd0?.TryGetInt32(ExifDirectoryBase.TagOrientation, out orientation);
                }
                catch { }

                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path);
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.Rotation = orientation switch
                {
                    6 => Rotation.Rotate90,
                    3 => Rotation.Rotate180,
                    8 => Rotation.Rotate270,
                    _ => Rotation.Rotate0
                };
                bi.EndInit();
                bi.Freeze();
                return bi;
            });
            _fullImage = bmp;
            OnPropertyChanged(nameof(FullImage));
            OnPropertyChanged(nameof(HasFullImage));
        }
        catch { }
        finally
        {
            _fullImageLoading = false;
            OnPropertyChanged(nameof(IsLoadingFullImage));
        }
    }

    public BitmapImage? Thumbnail
    {
        get => _thumbnail;
        set
        {
            System.Diagnostics.Debug.WriteLine($"[FolderSessionPhotoVM] Thumbnail setter called for {FileName}, IsNull={value == null}");
            if (_thumbnail != value)
            {
                _thumbnail = value;
                OnPropertyChanged(nameof(Thumbnail));
                OnPropertyChanged(nameof(ThumbnailStatus));
                OnPropertyChanged(nameof(IsThumbnailLoading));
                System.Diagnostics.Debug.WriteLine($"[FolderSessionPhotoVM] OnPropertyChanged(nameof(Thumbnail)) called for {FileName}");
            }
            System.Diagnostics.Debug.WriteLine($"[FolderSessionPhotoVM] Thumbnail setter completed for {FileName}");
        }
    }

    /// <summary>
    /// テスト用: サムネイルの状態を示す文字列
    /// </summary>
    public string ThumbnailStatus => _thumbnail == null ? "読込中..." : "✓";

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// RAW+JPEGペアで展開表示中かどうか（JPEGプライマリのみ使用）
    /// </summary>
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>
    /// RAW+JPEGグループとして表示中（オレンジ枠線でグループを示す）
    /// </summary>
    [ObservableProperty]
    private bool _isGroupedWithPair;

    /// <summary>
    /// 撮影設定サマリー (例: "f/1.8  1/500s  ISO400")
    /// </summary>
    public string SettingsSummary
    {
        get
        {
            var parts = new List<string>();
            if (Aperture.HasValue) parts.Add($"f/{Aperture.Value:0.0}");
            if (!string.IsNullOrEmpty(ShutterSpeed)) parts.Add(ShutterSpeed);
            if (ISO.HasValue) parts.Add($"ISO{ISO.Value}");
            return string.Join("  ", parts);
        }
    }

    /// <summary>
    /// 撮影日時テキスト
    /// </summary>
    public string DateTakenText => DateTaken == default ? string.Empty : DateTaken.ToString("yyyy/MM/dd");

    public FolderSessionPhotoViewModel(FolderSessionPhoto photo,
        Func<string, Task<BitmapImage?>>? rawFullImageLoader = null)
    {
        _photo = photo;
        _rawFullImageLoader = rawFullImageLoader;
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

    /// <summary>
    /// ペアとなるファイルのパス
    /// </summary>
    public string? PairedFilePath => _photo.PairedFilePath;

    /// <summary>
    /// RAWファイルかどうか
    /// </summary>
    public bool IsRawFile => _photo.IsRawFile;

    /// <summary>
    /// ペアの一部かどうか
    /// </summary>
    public bool HasPair => _photo.HasPair;

    /// <summary>
    /// ペア表示用のテキスト
    /// </summary>
    public string PairBadgeText => HasPair ? (IsRawFile ? "(JPEG)+RAW" : "JPEG+(RAW)") : string.Empty;
}
