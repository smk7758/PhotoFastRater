using System.Windows.Media.Imaging;
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

    public string? LensModel => _photo.LensModel;
    public double? Aperture => _photo.Aperture;
    public string? ShutterSpeed => _photo.ShutterSpeed;
    public int? ISO => _photo.ISO;
    public double? FocalLength => _photo.FocalLength;
    public double? ExposureCompensation => _photo.ExposureCompensation;
    public DateTime DateTaken => _photo.DateTaken;
    public int Width => _photo.Width;
    public int Height => _photo.Height;

    private BitmapImage? _thumbnail;

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
                OnPropertyChanged(nameof(ThumbnailStatus)); // テスト用
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
    public string PairBadgeText => HasPair ? (IsRawFile ? "RAW+JPG" : "JPG+RAW") : string.Empty;
}
