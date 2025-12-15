namespace PhotoFastRater.Core.Models;

/// <summary>
/// フォルダ除外パターン
/// </summary>
public class FolderExclusionPattern
{
    /// <summary>
    /// パターンID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 除外パターン（例: "*/temp/*", "*/backup/*"）
    /// </summary>
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// パターンのタイプ
    /// </summary>
    public PatternType Type { get; set; } = PatternType.Wildcard;

    /// <summary>
    /// パターンが有効か
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// パターン作成日時
    /// </summary>
    public DateTime CreatedDate { get; set; } = DateTime.Now;

    /// <summary>
    /// パターンの説明（オプション）
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// パターンマッチングのタイプ
/// </summary>
public enum PatternType
{
    /// <summary>
    /// ワイルドカード（例: */temp/*）
    /// </summary>
    Wildcard,

    /// <summary>
    /// 正規表現（例: ^.*\\temp\\.*$）
    /// </summary>
    Regex,

    /// <summary>
    /// 完全一致（例: D:\Photos\backup）
    /// </summary>
    Exact
}
