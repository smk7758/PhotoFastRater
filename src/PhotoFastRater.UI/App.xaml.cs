using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoFastRater.Core.Cache;
using PhotoFastRater.Core.Database;
using PhotoFastRater.Core.Database.Repositories;
using PhotoFastRater.Core.Export;
using PhotoFastRater.Core.ImageProcessing;
using PhotoFastRater.Core.Services;
using PhotoFastRater.Core.UI;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.ViewModels;
using PhotoFastRater.UI.Views;

namespace PhotoFastRater.UI;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var args = e.Args;

        if (args.Length >= 2 && args[0] == "--folder")
        {
            // フォルダモードで起動
            var folderPath = args[1];
            var folderWindow = _serviceProvider.GetRequiredService<FolderModeWindow>();
            folderWindow.LoadFolder(folderPath);
            folderWindow.Show();
        }
        else
        {
            // DBモードで起動（通常）
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Load configuration from appsettings.json
        var (cacheConfig, uiConfig) = LoadConfiguration();
        services.AddSingleton(cacheConfig);
        services.AddSingleton(uiConfig);

        // Database
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoFastRater", "photos.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        services.AddDbContext<PhotoDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Apply database migrations
        using (var scope = services.BuildServiceProvider().CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PhotoDbContext>();
            db.Database.Migrate();
        }

        // Repositories
        services.AddScoped<PhotoRepository>();
        services.AddScoped<EventRepository>();
        services.AddScoped<ManagedFolderRepository>();
        services.AddScoped<FolderExclusionPatternRepository>();

        // Services
        services.AddSingleton<ExifService>();
        services.AddScoped<ImportService>();
        services.AddScoped<EventManagementService>();
        services.AddScoped<ManagedFolderService>();
        services.AddScoped<FolderSessionService>();
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

        // Export
        services.AddSingleton<SocialMediaExporter>();

        // ViewModels
        services.AddTransient<MainViewModel>();
        services.AddTransient<PhotoGridViewModel>();
        services.AddTransient<EventViewModel>();
        services.AddTransient<ExportViewModel>();
        services.AddTransient<ManagedFoldersViewModel>();
        services.AddTransient<FolderModeViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Views
        services.AddTransient<MainWindow>();
        services.AddTransient<FolderModeWindow>();
    }

    private (CacheConfiguration, UIConfiguration) LoadConfiguration()
    {
        var appSettingsPath = "appsettings.json";

        // デフォルト設定
        var cacheConfig = new CacheConfiguration
        {
            CachePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PhotoFastRater", "Cache"),
            MaxMemoryCacheSizeMB = 500,
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
        if (File.Exists(appSettingsPath))
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

        return (cacheConfig, uiConfig);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
