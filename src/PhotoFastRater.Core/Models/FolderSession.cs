namespace PhotoFastRater.Core.Models;

/// <summary>
/// フォルダモードのセッション情報
/// </summary>
public class FolderSession
{
    /// <summary>
    /// セッションID
    /// </summary>
    public Guid SessionId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// フォルダパス
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// セッション作成日時
    /// </summary>
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime? LastModifiedDate { get; set; }

    /// <summary>
    /// 写真リスト
    /// </summary>
    public List<FolderSessionPhoto> Photos { get; set; } = new();

    /// <summary>
    /// 合計写真数
    /// </summary>
    public int TotalPhotos => Photos.Count;

    /// <summary>
    /// レーティング済み写真数
    /// </summary>
    public int RatedPhotos => Photos.Count(p => p.Rating > 0);
}
