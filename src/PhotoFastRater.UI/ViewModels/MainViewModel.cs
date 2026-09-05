using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Infrastructure.Database.Repositories;
using PhotoFastRater.Infrastructure.Services;

namespace PhotoFastRater.UI.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private readonly PhotoRepository _photoRepository;
    private readonly ImportService _importService;
    private readonly IUserInteractionService _interaction;
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _importCancellation;

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

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelImportCommand))]
    private bool _isImporting;

    [ObservableProperty]
    private int _importErrorCount;

    partial void OnSearchTextChanged(string value)
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        _ = SearchAfterDelayAsync(value, _searchCancellation.Token);
    }

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
        await Events.LoadEventsAsync();
    }

    [RelayCommand]
    private async Task ImportFolderAsync()
    {
        if (IsImporting)
            return;
        var selectedPath = await _interaction.SelectFolderAsync("写真フォルダを選択してください");
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            _importCancellation?.Dispose();
            _importCancellation = new CancellationTokenSource();
            IsImporting = true;
            ImportErrorCount = 0;
            StatusText = "インポートを開始しています…";

            var progress = new Progress<ImportProgress>(p =>
            {
                StatusText = p.TotalCount > 0
                    ? $"インポート中: {p.ProcessedCount:N0}/{p.TotalCount:N0} — {p.Status}"
                    : $"インポート中: {p.ProcessedCount:N0}件を確認 — {p.Status}";
            });

            try
            {
                var result = await _importService.ImportFromFolderAsync(
                    selectedPath,
                    true,
                    null,
                    progress,
                    _importCancellation.Token);
                ImportErrorCount = result.Errors.Count;
                await LoadPhotosAsync();
                StatusText = $"{result.ProcessedCount:N0}件を確認、エラー {result.Errors.Count:N0}件";
            }
            catch (OperationCanceledException) when (_importCancellation.IsCancellationRequested)
            {
                StatusText = "インポートをキャンセルしました。登録済みの写真は保持されています。";
            }
            finally
            {
                IsImporting = false;
            }
        }
    }

    [RelayCommand(CanExecute = nameof(CanCancelImport))]
    private void CancelImport() => _importCancellation?.Cancel();

    private bool CanCancelImport() => IsImporting;

    [RelayCommand]
    private async Task CreateEventFromSelectionAsync()
    {
        if (PhotoGrid.SelectedPhotoId is not int photoId)
        {
            await _interaction.NotifyAsync("イベント", "先に写真を1枚選択してください。", UserNotificationKind.Warning);
            return;
        }
        await Events.CreateEventAsync([photoId]);
    }

    [RelayCommand]
    private async Task ExportSelectedPhotoAsync()
    {
        if (PhotoGrid.SelectedPhoto is not { } selected)
        {
            await _interaction.NotifyAsync("エクスポート", "先に写真を1枚選択してください。", UserNotificationKind.Warning);
            return;
        }

        var outputFolder = await _interaction.SelectFolderAsync("エクスポート先を選択してください");
        if (string.IsNullOrWhiteSpace(outputFolder))
            return;
        var outputPath = Path.Combine(outputFolder, $"{Path.GetFileNameWithoutExtension(selected.FileName)}-export.jpg");
        try
        {
            var exportedPath = await Export.ExportPhotoAsync(selected.GetModel(), outputPath);
            StatusText = $"エクスポート完了: {exportedPath}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            await _interaction.NotifyAsync("エクスポートエラー", exception.Message, UserNotificationKind.Error);
        }
    }

    private async Task SearchAfterDelayAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken);
            StatusText = string.IsNullOrWhiteSpace(text) ? "全写真を表示中…" : $"「{text.Trim()}」を検索中…";
            await PhotoGrid.SearchAsync(text, cancellationToken);
            StatusText = $"{PhotoGrid.TotalPhotoCount:N0}件中 {PhotoGrid.Photos.Count:N0}件を表示";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _importCancellation?.Cancel();
        _importCancellation?.Dispose();
        PhotoGrid.Dispose();
        GC.SuppressFinalize(this);
    }
}
