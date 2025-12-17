using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoFastRater.UI.ViewModels;

/// <summary>
/// TreeViewのノード（年→月→日→フォルダの階層）
/// </summary>
public partial class PhotoTreeNode : ViewModelBase
{
    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<PhotoTreeNode> _children = new();

    [ObservableProperty]
    private ObservableCollection<PhotoViewModel> _photos = new();

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// ノードの種類
    /// </summary>
    public TreeNodeType NodeType { get; set; }

    /// <summary>
    /// 年（Yearノードの場合）
    /// </summary>
    public int? Year { get; set; }

    /// <summary>
    /// 月（Monthノードの場合）
    /// </summary>
    public int? Month { get; set; }

    /// <summary>
    /// 日（Dayノードの場合）
    /// </summary>
    public int? Day { get; set; }

    /// <summary>
    /// フォルダパス（Folderノードの場合）
    /// </summary>
    public string? FolderPath { get; set; }

    /// <summary>
    /// 写真の枚数（サマリー表示用）
    /// </summary>
    public int PhotoCount => Photos.Count + Children.Sum(c => c.PhotoCount);

    /// <summary>
    /// 表示名（写真枚数付き）
    /// </summary>
    public string DisplayNameWithCount => $"{DisplayName} ({PhotoCount}枚)";
}

/// <summary>
/// TreeNodeの種類
/// </summary>
public enum TreeNodeType
{
    /// <summary>
    /// 年ノード
    /// </summary>
    Year,

    /// <summary>
    /// 月ノード
    /// </summary>
    Month,

    /// <summary>
    /// 日ノード
    /// </summary>
    Day,

    /// <summary>
    /// フォルダノード
    /// </summary>
    Folder
}
