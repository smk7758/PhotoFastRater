using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoFastRater.Core.Cache;
using PhotoFastRater.Core.Database;
using PhotoFastRater.Core.Database.Repositories;
using PhotoFastRater.Core.Export;
using PhotoFastRater.Core.ImageProcessing;
using PhotoFastRater.Core.Services;
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
        // Configuration
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
        services.AddSingleton(cacheConfig);

        // Database
        var dbPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PhotoFastRater", "photos.db");

        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        services.AddDbContext<PhotoDbContext>(options =>
            options.UseSqlite($"Data Source={dbPath}"));

        // Ensure database is created
        using (var scope = services.BuildServiceProvider().CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PhotoDbContext>();
            db.Database.EnsureCreated();
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

        // Image Processing
        services.AddSingleton<IThumbnailGenerator, JpegThumbnailGenerator>();
        services.AddSingleton<ThumbnailCacheManager>();
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

    protected override void OnExit(ExitEventArgs e)
    {
        _serviceProvider?.Dispose();
        base.OnExit(e);
    }
}
