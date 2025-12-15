namespace PhotoFastRater.Core.Models;

/// <summary>
/// フォルダモード用の写真情報（DB非依存）
/// </summary>
public class FolderSessionPhoto
{
    /// <summary>
    /// ファイルの絶対パス
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// ファイル名
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// ファイルサイズ
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 撮影日時
    /// </summary>
    public DateTime DateTaken { get; set; }

    /// <summary>
    /// レーティング（0-5）
    /// </summary>
    public int Rating { get; set; }

    /// <summary>
    /// お気に入り
    /// </summary>
    public bool IsFavorite { get; set; }

    /// <summary>
    /// リジェクト
    /// </summary>
    public bool IsRejected { get; set; }

    /// <summary>
    /// 画像の幅
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// 画像の高さ
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// カメラモデル
    /// </summary>
    public string? CameraModel { get; set; }

    /// <summary>
    /// 絞り値
    /// </summary>
    public double? Aperture { get; set; }

    /// <summary>
    /// シャッタースピード
    /// </summary>
    public string? ShutterSpeed { get; set; }

    /// <summary>
    /// ISO感度
    /// </summary>
    public int? ISO { get; set; }

    /// <summary>
    /// 焦点距離
    /// </summary>
    public double? FocalLength { get; set; }

    /// <summary>
    /// サムネイルキャッシュパス
    /// </summary>
    public string? ThumbnailCachePath { get; set; }
}
