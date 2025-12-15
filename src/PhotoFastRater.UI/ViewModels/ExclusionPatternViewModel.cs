using CommunityToolkit.Mvvm.ComponentModel;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.UI.ViewModels;

/// <summary>
/// 除外パターンViewModel
/// </summary>
public partial class ExclusionPatternViewModel : ViewModelBase
{
    private readonly FolderExclusionPattern _pattern;

    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _patternString = string.Empty;

    [ObservableProperty]
    private PatternType _type;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    private string _typeDisplay = string.Empty;

    public ExclusionPatternViewModel(FolderExclusionPattern pattern)
    {
        _pattern = pattern;
        Id = pattern.Id;
        PatternString = pattern.Pattern;
        Type = pattern.Type;
        IsEnabled = pattern.IsEnabled;
        Description = pattern.Description;
        UpdateTypeDisplay();
    }

    partial void OnTypeChanged(PatternType value)
    {
        UpdateTypeDisplay();
    }

    private void UpdateTypeDisplay()
    {
        TypeDisplay = Type switch
        {
            PatternType.Wildcard => "ワイルドカード",
            PatternType.Regex => "正規表現",
            PatternType.Exact => "完全一致",
            _ => "不明"
        };
    }

    /// <summary>
    /// モデルに変更を反映
    /// </summary>
    public void UpdateModel()
    {
        _pattern.Pattern = PatternString;
        _pattern.Type = Type;
        _pattern.IsEnabled = IsEnabled;
        _pattern.Description = Description;
    }

    /// <summary>
    /// 元のモデルを取得
    /// </summary>
    public FolderExclusionPattern GetModel() => _pattern;
}
