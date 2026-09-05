using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Infrastructure.Database.Repositories;
using PhotoFastRater.Infrastructure.Services;

namespace PhotoFastRater.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly PhotoRepository _photoRepository;
    private readonly ImportService _importService;
    private readonly IUserInteractionService _interaction;

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
        SettingsViewModel settings,
        IUserInteractionService interaction)
    {
        _photoRepository = photoRepository;
        _importService = importService;
        _photoGrid = photoGrid;
        _events = events;
        _export = export;
        _settings = settings;
        _interaction = interaction;
    }

    [RelayCommand]
    private async Task LoadPhotosAsync()
    {
        StatusText = "写真を読み込み中...";
        await PhotoGrid.LoadAllPhotosAsync();
        StatusText = $"{PhotoGrid.TotalPhotoCount:N0}枚中 {PhotoGrid.Photos.Count:N0}枚を表示";
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        var selectedPath = await _interaction.SelectFolderAsync("写真フォルダを選択してください");
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            StatusText = "インポート中...";

            var progress = new Progress<ImportProgress>(p =>
            {
                StatusText = $"インポート中: {p.ProcessedCount}/{p.TotalCount} - {p.Status}";
            });

            await _importService.ImportFromFolderAsync(selectedPath, true, null, progress);
            await LoadPhotosAsync();
        }
    }
}
