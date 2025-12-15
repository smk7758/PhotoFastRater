using CommunityToolkit.Mvvm.ComponentModel;
using PhotoFastRater.Core.Models;

namespace PhotoFastRater.UI.ViewModels;

/// <summary>
/// 管理フォルダの項目ViewModel
/// </summary>
public partial class ManagedFolderItemViewModel : ViewModelBase
{
    private readonly ManagedFolder _folder;

    [ObservableProperty]
    private int _id;

    [ObservableProperty]
    private string _folderPath = string.Empty;

    [ObservableProperty]
    private bool _isRecursive;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private int _photoCount;

    [ObservableProperty]
    private DateTime _addedDate;

    [ObservableProperty]
    private DateTime? _lastScanDate;

    [ObservableProperty]
    private string _lastScanDateDisplay = "-";

    public ManagedFolderItemViewModel(ManagedFolder folder)
    {
        _folder = folder;
        Id = folder.Id;
        FolderPath = folder.FolderPath;
        IsRecursive = folder.IsRecursive;
        IsActive = folder.IsActive;
        PhotoCount = folder.PhotoCount;
        AddedDate = folder.AddedDate;
        LastScanDate = folder.LastScanDate;
        UpdateLastScanDateDisplay();
    }

    partial void OnLastScanDateChanged(DateTime? value)
    {
        UpdateLastScanDateDisplay();
    }

    private void UpdateLastScanDateDisplay()
    {
        LastScanDateDisplay = LastScanDate?.ToString("yyyy/MM/dd HH:mm") ?? "-";
    }

    /// <summary>
    /// モデルに変更を反映
    /// </summary>
    public void UpdateModel()
    {
        _folder.FolderPath = FolderPath;
        _folder.IsRecursive = IsRecursive;
        _folder.IsActive = IsActive;
        _folder.PhotoCount = PhotoCount;
        _folder.LastScanDate = LastScanDate;
    }

    /// <summary>
    /// 元のモデルを取得
    /// </summary>
    public ManagedFolder GetModel() => _folder;
}
