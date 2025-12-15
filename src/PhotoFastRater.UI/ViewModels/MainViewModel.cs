using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Database.Repositories;
using PhotoFastRater.Core.Services;

namespace PhotoFastRater.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly PhotoRepository _photoRepository;
    private readonly ImportService _importService;

    [ObservableProperty]
    private PhotoGridViewModel _photoGrid;

    [ObservableProperty]
    private EventViewModel _events;

    [ObservableProperty]
    private ExportViewModel _export;

    [ObservableProperty]
    private SettingsViewModel _settings;

    [ObservableProperty]
    private string _statusText = "準備完了";

    public MainViewModel(
        PhotoRepository photoRepository,
        ImportService importService,
        PhotoGridViewModel photoGrid,
        EventViewModel events,
        ExportViewModel export,
        SettingsViewModel settings)
    {
        _photoRepository = photoRepository;
        _importService = importService;
        _photoGrid = photoGrid;
        _events = events;
        _export = export;
        _settings = settings;
    }

    [RelayCommand]
    private async Task LoadPhotosAsync()
    {
        StatusText = "写真を読み込み中...";
        await PhotoGrid.LoadAllPhotosAsync();
        StatusText = $"{PhotoGrid.Photos.Count}枚の写真を読み込みました";
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "写真フォルダを選択してください"
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            StatusText = "インポート中...";

            var progress = new Progress<ImportProgress>(p =>
            {
                StatusText = $"インポート中: {p.ProcessedCount}/{p.TotalCount} - {p.Status}";
            });

            await _importService.ImportFromFolderAsync(dialog.SelectedPath, true, null, progress);
            await LoadPhotosAsync();
        }
    }
}
