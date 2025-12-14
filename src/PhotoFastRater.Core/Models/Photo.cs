namespace PhotoFastRater.Core.Models;

public class Photo
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }

    // 日時情報
    public DateTime DateTaken { get; set; }
    public DateTime ImportDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    // レーティング
    public int Rating { get; set; }  // 0-5
    public bool IsFavorite { get; set; }
    public bool IsRejected { get; set; }

    // カメラ・レンズ情報
    public string? CameraModel { get; set; }
    public string? CameraMake { get; set; }
    public string? LensModel { get; set; }

    // EXIF 情報
    public int Width { get; set; }
    public int Height { get; set; }
    public double? Aperture { get; set; }
    public string? ShutterSpeed { get; set; }
    public int? ISO { get; set; }
    public double? FocalLength { get; set; }
    public double? ExposureCompensation { get; set; }

    // GPS 情報
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? LocationName { get; set; }

    // キャッシュ情報
    public string? ThumbnailCachePath { get; set; }
    public DateTime? ThumbnailGeneratedDate { get; set; }
    public string? FileHash { get; set; }  // ファイル変更検出用

    // リレーション
    public List<PhotoEventMapping> Events { get; set; } = new();
}
