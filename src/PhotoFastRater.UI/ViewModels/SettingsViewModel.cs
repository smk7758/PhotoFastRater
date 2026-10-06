using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Infrastructure.Cache;
using PhotoFastRater.Core.UI;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.UI.Services;

namespace PhotoFastRater.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly CacheConfiguration _cacheConfig;
    private readonly PhotoFastRater.UI.Services.ApplicationPaths _paths;
    public bool IsCachePathReadOnly => _paths.IsIsolated;
    private readonly UIConfiguration _uiConfig;
    private readonly UserSettingsStore _settingsStore;
    private readonly ThumbnailCacheManager _cacheManager;
    private readonly IUserInteractionService _interaction;

    [ObservableProperty]
    private ManagedFoldersViewModel? _managedFolders;

    [ObservableProperty]
    private string _cachePath = @"D:\PhotoCache";

    [ObservableProperty]
    private int _maxMemoryCacheSizeMB = 500;

    [ObservableProperty]
    private int _thumbnailSize = 512;

    [ObservableProperty]
    private int _jpegQuality = 85;

    [ObservableProperty]
    private int _maxParallelGenerations = 4;

    [ObservableProperty]
    private bool _enableRAWSupport = true;

    [ObservableProperty]
    private bool _enableGPUAcceleration = true;

    [ObservableProperty]
    private long _currentCacheSize = 0;

    [ObservableProperty]
    private string _saveStatus = string.Empty;

    [ObservableProperty]
    private string _arrowKeyNavigationMode = "GridFocus";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveSettingsCommand))]
    private bool _isInputValid = true;

    private bool CanSaveInputs() => IsInputValid;

    public SettingsViewModel(
        CacheConfiguration cacheConfig,
        PhotoFastRater.UI.Services.ApplicationPaths paths,
        UIConfiguration uiConfig,
        ManagedFoldersViewModel managedFoldersViewModel,
        UserSettingsStore settingsStore,
        ThumbnailCacheManager cacheManager,
        IUserInteractionService interaction)
    {
        _cacheConfig = cacheConfig;
        _paths = paths;
        _uiConfig = uiConfig;
        _settingsStore = settingsStore;
        _cacheManager = cacheManager;
        _interaction = interaction;
        ManagedFolders = managedFoldersViewModel;
        LoadSettings();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        if (ManagedFolders != null)
        {
            await ManagedFolders.LoadAsync();
        }
    }

    private void LoadSettings()
    {
        CachePath = _cacheConfig.CachePath;
        MaxMemoryCacheSizeMB = _cacheConfig.MaxMemoryCacheSizeMB;
        ThumbnailSize = _cacheConfig.ThumbnailSize;
        JpegQuality = _cacheConfig.JpegQuality;
        MaxParallelGenerations = _cacheConfig.MaxParallelGenerations;
        EnableRAWSupport = _cacheConfig.EnableRAWSupport;
        ArrowKeyNavigationMode = _uiConfig.ArrowKeyNavigationMode;
        EnableGPUAcceleration = _uiConfig.EnableGPUAcceleration;
    }

    private bool CanSelectCachePath() => !_paths.IsIsolated;
    [RelayCommand(CanExecute = nameof(CanSelectCachePath))]
    private async Task SelectCachePathAsync()
    {
        var selected = await _interaction.SelectFolderAsync("キャッシュフォルダ（SSD推奨）を選択してください");
        if (!string.IsNullOrWhiteSpace(selected))
            CachePath = selected;
    }

    [RelayCommand(CanExecute = nameof(CanSaveInputs))]
    private async Task SaveSettingsAsync()
    {
        var candidateCache = new CacheConfiguration
        {
            CachePath = _paths.IsIsolated ? _paths.Cache : CachePath,
            MaxMemoryCacheSizeMB = MaxMemoryCacheSizeMB,
            MaxDiskCacheSizeGB = _cacheConfig.MaxDiskCacheSizeGB,
            ThumbnailSize = ThumbnailSize,
            JpegQuality = JpegQuality,
            MaxParallelGenerations = MaxParallelGenerations,
            EnableRAWSupport = EnableRAWSupport
        };
        var candidateUi = new UIConfiguration
        {
            GridThumbnailSize = _uiConfig.GridThumbnailSize,
            EnableGPUAcceleration = EnableGPUAcceleration,
            ArrowKeyNavigationMode = ArrowKeyNavigationMode
        };
        try
        {
            await _settingsStore.SaveAsync(candidateCache, candidateUi);
            // Validate and persist first: a failed save must not poison live cache configuration.
            _cacheConfig.CachePath = candidateCache.CachePath;
            _cacheConfig.MaxMemoryCacheSizeMB = candidateCache.MaxMemoryCacheSizeMB;
            _cacheConfig.ThumbnailSize = candidateCache.ThumbnailSize;
            _cacheConfig.JpegQuality = candidateCache.JpegQuality;
            _cacheConfig.MaxParallelGenerations = candidateCache.MaxParallelGenerations;
            _cacheConfig.EnableRAWSupport = candidateCache.EnableRAWSupport;
            _uiConfig.ArrowKeyNavigationMode = candidateUi.ArrowKeyNavigationMode;
            _uiConfig.EnableGPUAcceleration = candidateUi.EnableGPUAcceleration;
            SaveStatus = "設定を保存しました。キャッシュ構成の変更は再起動後に反映されます。";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SaveStatus = "設定を保存できませんでした。";
            await _interaction.NotifyAsync("設定保存エラー", exception.Message, UserNotificationKind.Error);
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        if (!await _interaction.ConfirmAsync(
            "キャッシュをクリア",
            "生成済みサムネイルを退避します。元の写真は変更しません。続行しますか？"))
        {
            return;
        }

        await _cacheManager.ClearAsync();
        await UpdateCacheSizeAsync();
        SaveStatus = "サムネイルキャッシュを退避しました。";
    }

    private async Task UpdateCacheSizeAsync()
    {
        if (!Directory.Exists(CachePath))
        {
            CurrentCacheSize = 0;
            return;
        }

        CurrentCacheSize = await Task.Run(() =>
        {
            var files = Directory.EnumerateFiles(CachePath, "*.jpg", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}_GARBAGE{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
            return files.Sum(f => new FileInfo(f).Length);
        });
    }
}
