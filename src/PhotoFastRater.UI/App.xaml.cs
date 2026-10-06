using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.UI;
using PhotoFastRater.Infrastructure.Cache;
using PhotoFastRater.Infrastructure.Database;
using PhotoFastRater.Infrastructure.Database.Repositories;
using PhotoFastRater.Infrastructure.Export;
using PhotoFastRater.Infrastructure.ImageProcessing;
using PhotoFastRater.Infrastructure.Metadata;
using PhotoFastRater.Infrastructure.Services;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.ViewModels;
using PhotoFastRater.UI.Views;

namespace PhotoFastRater.UI;

public partial class App : System.Windows.Application
{
    private static readonly Action<ILogger, Exception?> LogUnhandledException =
        LoggerMessage.Define(LogLevel.Critical, new EventId(1000, "UnhandledUiException"), "Unhandled UI exception");
    private ServiceProvider? _serviceProvider;

    /// <summary>Initializes the selected profile without blocking dispatcher continuations.</summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var options = StartupOptions.Parse(e.Args);
            var services = new ServiceCollection();
            ConfigureServices(services, options.Paths);
            _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var ui = _serviceProvider.GetRequiredService<UIConfiguration>();
            System.Windows.Media.RenderOptions.ProcessRenderMode = ui.EnableGPUAcceleration
                ? System.Windows.Interop.RenderMode.Default
                : System.Windows.Interop.RenderMode.SoftwareOnly;
            await _serviceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
            await _serviceProvider.GetRequiredService<XmpSyncQueue>().RestorePendingAsync();
            var windows = _serviceProvider.GetRequiredService<WindowManager>();
            if (options.FolderMode)
                windows.ShowFolderWindow(options.FolderPath, openDialogWhenEmpty: true);
            else
                windows.ShowMainWindow();
        }
        catch (Exception exception)
        {
            if (_serviceProvider?.GetService<ILogger<App>>() is { } logger)
                LogUnhandledException(logger, exception);
            System.Windows.MessageBox.Show(exception.Message, "起動できませんでした", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
    private void ConfigureServices(IServiceCollection services, ApplicationPaths paths)
    {
        // Load configuration from appsettings.json
        var (cacheConfig, uiConfig) = LoadConfiguration(paths);
        var settingsStore = new UserSettingsStore(paths.Settings);
        settingsStore.TryLoad(cacheConfig, uiConfig);
        // Isolated profiles may contain copied settings, but never inherit an external cache.
        if (paths.IsIsolated)
            cacheConfig.CachePath = paths.Cache;
        services.AddSingleton(paths);
        services.AddSingleton(cacheConfig);
        services.AddSingleton(uiConfig);
        services.AddSingleton(settingsStore);

        // Database
        var dbPath = paths.Database;

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        services.AddPooledDbContextFactory<PhotoDbContext>(options =>
            options.UseSqlite(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = dbPath, Cache = Microsoft.Data.Sqlite.SqliteCacheMode.Shared, DefaultTimeout = 5 }.ToString()));
        services.AddSingleton<DatabaseInitializer>();
        var logPath = Path.Combine(paths.Logs, $"PhotoFastRater-{DateTime.UtcNow:yyyyMMdd}.log");
        services.AddLogging(builder => builder.AddDebug().AddProvider(new LocalFileLoggerProvider(logPath)));

        // Repositories
        services.AddSingleton<PhotoRepository>();
        services.AddSingleton<IPhotoCatalog>(serviceProvider => serviceProvider.GetRequiredService<PhotoRepository>());
        services.AddSingleton<EventRepository>();
        services.AddSingleton<ManagedFolderRepository>();
        services.AddSingleton<FolderExclusionPatternRepository>();
        services.AddSingleton<LibraryOrganizationRepository>();
        services.AddSingleton<IPhotoChangeNotifier, PhotoChangeNotifier>();
        services.AddSingleton<IXmpSidecarStore, XmpSidecarStore>();
        services.AddSingleton<XmpSyncQueue>();
        services.AddSingleton<IRatingCoordinator, RatingCoordinator>();
        services.AddSingleton<IUserInteractionService, WpfUserInteractionService>();
        services.AddSingleton<IPlatformShell, WindowsPlatformShell>();

        // Services
        services.AddSingleton<ExifService>();
        services.AddSingleton<IFolderScanner, FolderScanner>();
        services.AddScoped<ImportService>();
        services.AddScoped<EventManagementService>();
        services.AddScoped<ManagedFolderService>();
        services.AddScoped(sp => new FolderSessionService(sp.GetRequiredService<ExifService>(), paths.Sessions, paths.LegacySessionRoot));
        services.AddScoped<DataMigrationService>();

        // Image Processing
        services.AddSingleton<JpegThumbnailGenerator>(sp =>
        {
            var config = sp.GetRequiredService<CacheConfiguration>();
            return new JpegThumbnailGenerator(config.JpegQuality);
        });
        services.AddSingleton<RawThumbnailGenerator>();
        services.AddSingleton<ThumbnailCacheManager>(sp =>
        {
            var config = sp.GetRequiredService<CacheConfiguration>();
            var jpegGenerator = sp.GetRequiredService<JpegThumbnailGenerator>();
            var rawGenerator = sp.GetRequiredService<RawThumbnailGenerator>();
            return new ThumbnailCacheManager(config, jpegGenerator, rawGenerator);
        });
        services.AddSingleton<ImageLoader>();
        services.AddSingleton<IImageDecodeService, ImageDecodeService>();

        // Export
        services.AddSingleton<SocialMediaExporter>();
        services.AddSingleton<IExportService, BatchExportService>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<PhotoGridViewModel>();
        services.AddTransient<EventViewModel>();
        services.AddTransient<ExportViewModel>();
        services.AddTransient<ManagedFoldersViewModel>();
        services.AddTransient<FolderModeViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<FolderModeSettingsViewModel>();
        services.AddScoped<CompareWorkspaceViewModel>();
        services.AddScoped<LibraryOrganizationViewModel>();

        // Metadata write-back
        services.AddSingleton<IEmbeddedMetadataWriter, EmbeddedMetadataWriter>();

        // Keyboard Shortcuts
        services.AddSingleton(new ShortcutService(paths));
        services.AddTransient<KeyboardShortcutsViewModel>();
        services.AddTransient<KeyboardShortcutsWindow>();
        services.AddSingleton<WindowManager>();

        // Views
        services.AddTransient<MainWindow>();
        services.AddTransient<FolderModeWindow>();
        services.AddTransient<FolderModeSettingsWindow>();
        services.AddTransient<PhotoPreviewWindow>();
        services.AddTransient<CompareWindow>();
    }

    private (CacheConfiguration, UIConfiguration) LoadConfiguration(ApplicationPaths paths)
    {
        var appSettingsPath = "appsettings.json";

        // デフォルト設定
        var cacheConfig = new CacheConfiguration
        {
            CachePath = paths.Cache,
            MaxMemoryCacheSizeMB = 500,
            MaxDiskCacheSizeGB = 10,
            ThumbnailSize = 512,
            JpegQuality = 85,
            MaxParallelGenerations = 4,
            EnableRAWSupport = true
        };

        var uiConfig = new UIConfiguration
        {
            GridThumbnailSize = 256,
            EnableGPUAcceleration = true,
            ArrowKeyNavigationMode = "GridFocus"
        };

        // appsettings.jsonから設定を読み込む
        if (!paths.IsIsolated && File.Exists(appSettingsPath))
        {
            try
            {
                var json = File.ReadAllText(appSettingsPath);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;

                // Cache設定を読み込み
                if (root.TryGetProperty("Cache", out var cacheElement))
                {
                    if (cacheElement.TryGetProperty("CachePath", out var cachePath))
                        cacheConfig.CachePath = cachePath.GetString() ?? cacheConfig.CachePath;
                    if (cacheElement.TryGetProperty("MaxMemoryCacheSizeMB", out var maxMemory))
                        cacheConfig.MaxMemoryCacheSizeMB = maxMemory.GetInt32();
                    if (cacheElement.TryGetProperty("MaxDiskCacheSizeGB", out var maxDisk))
                        cacheConfig.MaxDiskCacheSizeGB = Math.Clamp(maxDisk.GetInt32(), 1, 100);
                    if (cacheElement.TryGetProperty("ThumbnailSize", out var thumbnailSize))
                        cacheConfig.ThumbnailSize = thumbnailSize.GetInt32();
                    if (cacheElement.TryGetProperty("JpegQuality", out var jpegQuality))
                        cacheConfig.JpegQuality = jpegQuality.GetInt32();
                    if (cacheElement.TryGetProperty("MaxParallelGenerations", out var maxParallel))
                        cacheConfig.MaxParallelGenerations = maxParallel.GetInt32();
                    if (cacheElement.TryGetProperty("EnableRAWSupport", out var enableRAW))
                        cacheConfig.EnableRAWSupport = enableRAW.GetBoolean();
                }

                // UI設定を読み込み
                if (root.TryGetProperty("UI", out var uiElement))
                {
                    if (uiElement.TryGetProperty("GridThumbnailSize", out var gridThumbnailSize))
                        uiConfig.GridThumbnailSize = gridThumbnailSize.GetInt32();
                    if (uiElement.TryGetProperty("EnableGPUAcceleration", out var enableGPU))
                        uiConfig.EnableGPUAcceleration = enableGPU.GetBoolean();
                    if (uiElement.TryGetProperty("ArrowKeyNavigationMode", out var arrowKeyMode))
                        uiConfig.ArrowKeyNavigationMode = arrowKeyMode.GetString() ?? uiConfig.ArrowKeyNavigationMode;
                }
            }
            catch
            {
                // 設定ファイルの読み込みに失敗した場合はデフォルト設定を使用
            }
        }

        // 利用可能な物理RAMを確認し、キャッシュ上限を動的設定
        var gcInfo = GC.GetGCMemoryInfo();
        long availableRamMB = gcInfo.TotalAvailableMemoryBytes / (1024 * 1024);
        int dynamicLimit = (int)Math.Min(availableRamMB / 2, cacheConfig.MaxMemoryCacheSizeMB);
        cacheConfig.MaxMemoryCacheSizeMB = Math.Max(dynamicLimit, 128);

        return (cacheConfig, uiConfig);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        if (_serviceProvider?.GetService<ILogger<App>>() is { } logger)
            LogUnhandledException(logger, e.Exception);
        System.Windows.MessageBox.Show(
            $"予期しないエラーが発生しました:\n\n{e.Exception.Message}\n\n{e.Exception.GetType().Name}",
            "エラー", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
