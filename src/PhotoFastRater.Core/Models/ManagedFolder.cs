namespace PhotoFastRater.Core.Models;

/// <summary>
/// DBモードで管理する対象フォルダ
/// </summary>
public class ManagedFolder
{
    /// <summary>
    /// フォルダID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// フォルダの絶対パス
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>
    /// サブフォルダを再帰的にスキャンするか
    /// </summary>
    public bool IsRecursive { get; set; } = true;

    /// <summary>
    /// フォルダが追加された日時
    /// </summary>
    public DateTime AddedDate { get; set; } = DateTime.Now;

    /// <summary>
    /// 最後にスキャンした日時
    /// </summary>
    public DateTime? LastScanDate { get; set; }

    /// <summary>
    /// このフォルダに含まれる写真の数
    /// </summary>
    public int PhotoCount { get; set; }

    /// <summary>
    /// フォルダが有効か（一時的に無効化可能）
    /// </summary>
    public bool IsActive { get; set; } = true;
}
