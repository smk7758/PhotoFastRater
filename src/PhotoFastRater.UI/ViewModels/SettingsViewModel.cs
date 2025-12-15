using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.Core.Cache;

namespace PhotoFastRater.UI.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly CacheConfiguration _cacheConfig;

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
    private long _currentCacheSize = 0;

    public SettingsViewModel(
        CacheConfiguration cacheConfig,
        ManagedFoldersViewModel managedFoldersViewModel)
    {
        _cacheConfig = cacheConfig;
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
    }

    [RelayCommand]
    private void SelectCachePath()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "キャッシュフォルダ（SSD推奨）を選択してください",
            SelectedPath = CachePath
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            CachePath = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void SaveSettings()
    {
        _cacheConfig.CachePath = CachePath;
        _cacheConfig.MaxMemoryCacheSizeMB = MaxMemoryCacheSizeMB;
        _cacheConfig.ThumbnailSize = ThumbnailSize;
        _cacheConfig.JpegQuality = JpegQuality;
        _cacheConfig.MaxParallelGenerations = MaxParallelGenerations;
        _cacheConfig.EnableRAWSupport = EnableRAWSupport;

        // 設定を保存（後で実装）
        SaveToFile();
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        // キャッシュクリア処理
        if (Directory.Exists(CachePath))
        {
            await Task.Run(() =>
            {
                var files = Directory.GetFiles(CachePath, "*.jpg");
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            });
        }

        await UpdateCacheSizeAsync();
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
            var files = Directory.GetFiles(CachePath, "*.jpg");
            return files.Sum(f => new FileInfo(f).Length);
        });
    }

    private void SaveToFile()
    {
        // JSON設定ファイルに保存（後で実装）
    }
}
