using PhotoFastRater.Core.Domain;

namespace PhotoFastRater.Core.Models;

public class Photo
{
    public int Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string? NormalizedPath { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime FileModifiedUtc { get; set; }

    // 日時情報
    public DateTime DateTaken { get; set; }
    public DateTime ImportDate { get; set; }
    public DateTime? ModifiedDate { get; set; }

    // レーティング
    public int Rating { get; set; }  // 0-5
    public bool IsFavorite { get; set; }
    public bool IsRejected { get; set; }
    public DateTime RatingModifiedUtc { get; set; }
    public long RatingRevision { get; set; }
    public string RatingSource { get; set; } = "legacy";
    public DateTime? SidecarModifiedUtc { get; set; }
    public MetadataSyncStatus MetadataSyncStatus { get; set; }

    // RAW+JPEG links use a stable identifier instead of repeated path scans.
    public Guid? PairId { get; set; }
    public PairLinkMode PairLinkMode { get; set; } = PairLinkMode.Linked;
    public string? NormalizedDirectory { get; set; }
    public string? NormalizedBaseName { get; set; }
    public bool IsMissing { get; set; }

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
    public List<PhotoTagMapping> Tags { get; set; } = [];
    public List<PhotoCollectionMapping> Collections { get; set; } = [];
}
