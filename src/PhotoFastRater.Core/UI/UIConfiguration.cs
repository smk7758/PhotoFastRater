namespace PhotoFastRater.Core.UI;

/// <summary>
/// UI設定
/// </summary>
public class UIConfiguration
{
    /// <summary>
    /// グリッドサムネイルサイズ
    /// </summary>
    public int GridThumbnailSize { get; set; } = 256;

    /// <summary>
    /// GPU アクセラレーション有効化
    /// </summary>
    public bool EnableGPUAcceleration { get; set; } = true;

    /// <summary>
    /// 矢印キーナビゲーションモード
    /// "GridFocus": グリッドにフォーカスがある時は常に動作
    /// "SelectionOnly": 写真が選択されている時のみ動作
    /// </summary>
    public string ArrowKeyNavigationMode { get; set; } = "GridFocus";
}
