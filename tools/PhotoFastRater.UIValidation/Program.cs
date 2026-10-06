using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PhotoFastRater.Core.Abstractions;
using PhotoFastRater.Core.Models;
using PhotoFastRater.Infrastructure.Database;
using PhotoFastRater.Infrastructure.Database.Repositories;
using PhotoFastRater.Infrastructure.ImageProcessing;
using PhotoFastRater.UI;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.ViewModels;
using PhotoFastRater.UI.Views;
using SixLabors.ImageSharp.PixelFormats;
using ImageSharpImage = SixLabors.ImageSharp.Image;

namespace PhotoFastRater.UIValidation;

/// <summary>Runs production WPF composition with scripted decisions; keeps physical-display and dialog claims separate.</summary>
internal static class Program
{
    private static readonly List<CheckResult> Results = [];
    private static string _output = string.Empty;
    private static int _exitCode;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length < 1 || !Path.IsPathFullyQualified(args[0]))
        {
            Console.Error.WriteLine("Usage: UIValidation <absolute-artifacts-root> [scroll-seconds] [software]");
            return 2;
        }
        _output = args[0];
        Directory.CreateDirectory(_output);
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var application = new App { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.InitializeComponent();
        application.UseValidationExceptionHandler(exception => { Results.Add(new("Dispatcher", false, exception.ToString())); _exitCode = 1; });
        _ = Dispatcher.CurrentDispatcher.InvokeAsync(async () =>
        {
            try { await RunAsync(application, args); }
            catch (Exception exception) { Results.Add(new("Runner", false, exception.ToString())); _exitCode = 1; }
            finally
            {
                var suffix = args.Contains("catalog-ui") ? "catalog-ui" : args.Contains("prepare-uat") ? "uat-preparation" : args.Contains("measure") ? "measure" : args.Contains("software") ? "software" : "default";
                File.WriteAllText(Path.Combine(_output, $"wpf-results-{suffix}.json"), JsonSerializer.Serialize(Results, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine($"WPF checks: {Results.Count(result => result.Passed)} passed, {Results.Count(result => !result.Passed)} failed");
                Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
            }
        });
        Dispatcher.Run();
        return _exitCode;
    }

    private static async Task RunAsync(App application, string[] args)
    {
        var paths = new ApplicationPaths(Path.Combine(_output, "profiles", $"wpf-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}"));
        var decisions = new ScriptedInteraction();
        var shell = new ScriptedShell();
        var services = new ServiceCollection();
        application.ConfigureServices(services, paths);
        services.AddSingleton<IUserInteractionService>(decisions);
        services.AddSingleton<IPlatformShell>(shell);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        if (args.Contains("catalog-ui"))
        {
            var fixture = Directory.GetFiles(Path.Combine(_output, "data"), "benchmark.db", SearchOption.AllDirectories).First();
            Directory.CreateDirectory(paths.Root);
            File.Copy(fixture, paths.Database);
        }
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        if (args.Contains("catalog-ui"))
        {
            using var catalogScope = provider.CreateScope();
            var catalogWindow = catalogScope.ServiceProvider.GetRequiredService<MainWindow>();
            var model = (MainViewModel)catalogWindow.DataContext;
            var firstRows = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var firstRender = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
            var timer = new Stopwatch();
            double firstRowMilliseconds = 0;
            model.PhotoGrid.Photos.CollectionChanged += (_, _) =>
            {
                if (model.PhotoGrid.Photos.Count > 0 && !firstRows.Task.IsCompleted)
                {
                    firstRowMilliseconds = timer.Elapsed.TotalMilliseconds;
                    firstRows.TrySetResult();
                }
            };
            catalogWindow.IsVisibleChanged += (_, change) => { if (change.NewValue is true) timer.Start(); };
            EventHandler rendered = (_, _) =>
            {
                if (timer.IsRunning && catalogWindow.IsVisible && model.PhotoGrid.Photos.Count > 0)
                    firstRender.TrySetResult(timer.Elapsed.TotalMilliseconds);
            };
            CompositionTarget.Rendering += rendered;
            catalogWindow.Show();
            if (model.PhotoGrid.Photos.Count > 0) firstRows.TrySetResult();
            await firstRows.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var firstMilliseconds = firstRowMilliseconds;
            double firstRenderMilliseconds;
            try { firstRenderMilliseconds = await firstRender.Task.WaitAsync(TimeSpan.FromSeconds(10)); }
            finally { CompositionTarget.Rendering -= rendered; }
            await catalogWindow.Dispatcher.InvokeAsync(() => catalogWindow.UpdateLayout(), DispatcherPriority.Render);
            var renderMilliseconds = timer.Elapsed.TotalMilliseconds;
            Require(model.PhotoGrid.TotalPhotoCount == 100_000, "Synthetic catalog count mismatch");
            File.WriteAllText(Path.Combine(_output, "catalog-ui-timing.json"), JsonSerializer.Serialize(new
            {
                FirstCollectionRowMilliseconds = firstMilliseconds,
                FirstRenderingCallbackMilliseconds = firstRenderMilliseconds,
                FirstLayoutMilliseconds = renderMilliseconds,
                Count = model.PhotoGrid.TotalPhotoCount,
                Limitations = "Starts at WPF IsVisible=true; synthetic metadata, missing originals, warm catalog; layout is not physical monitor presentation."
            }, new JsonSerializerOptions { WriteIndented = true }));
            Results.Add(new("Catalog.UI.FirstList", firstRenderMilliseconds <= 500, $"First rendering callback {firstRenderMilliseconds:F2} ms; metadata fixture only"));
            catalogWindow.Close();
            return;
        }
        if (args.Contains("prepare-uat"))
        {
            var destination = Path.Combine(_output, "data", "uat-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            CreatePhotos(destination, 4);
            var generator = provider.GetRequiredService<RawThumbnailGenerator>();
            foreach (var source in Directory.GetFiles(Path.Combine(_output, "data", "raw"), "sample.*"))
            {
                var extension = Path.GetExtension(source);
                var basename = "sample-" + extension.TrimStart('.');
                File.Copy(source, Path.Combine(destination, basename + extension));
                await File.WriteAllBytesAsync(Path.Combine(destination, basename + ".jpg"), await generator.ExtractEmbeddedJpegBytesAsync(source));
            }
            File.Copy(Path.Combine(destination, "写真-000000.jpg"), Path.Combine(destination, "長い日本語の写真名・縦横と選択状態を確認するためのコピー.jpg"));
            var invalid = Path.Combine(destination, "異常系");
            Directory.CreateDirectory(invalid);
            await File.WriteAllTextAsync(Path.Combine(invalid, "破損画像.jpg"), "not a JPEG; intentionally invalid test copy");
            await File.WriteAllTextAsync(Path.Combine(_output, "uat-data-path.txt"), destination);
            Results.Add(new("Uat.Prepared", true, "Derived JPEGs are embedded previews, not independently photographed RAW/JPEG pairs. " + destination));
            return;
        }
        if (args.Contains("software")) RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var environment = new
        {
            Os = Environment.OSVersion.ToString(),
            Runtime = Environment.Version.ToString(),
            CpuCount = Environment.ProcessorCount,
            RamBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            RenderTier = RenderCapability.Tier >> 16,
            RenderMode = RenderOptions.ProcessRenderMode.ToString(),
            UiAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),
            Profile = paths.Root,
            EvidenceKind = "Production WPF windows/control peers; native decisions scripted; RenderTargetBitmap is not a physical-monitor screenshot"
        };
        File.WriteAllText(Path.Combine(_output, args.Contains("software") ? "environment-software.json" : "environment-default.json"), JsonSerializer.Serialize(environment, new JsonSerializerOptions { WriteIndented = true }));
        var photos = CreatePhotos(Path.Combine(_output, "data", "real10000-v2"), 10_000);
        decisions.Folder = photos;
        using var scope = provider.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<MainWindow>();
        var main = (MainViewModel)window.DataContext;
        window.Show();
        await Task.Delay(200);
        await CheckAsync("Main.Import.Control", async () =>
        {
            InvokeButton(window, "写真をインポート");
            await UntilAsync(() => !main.ImportFolderCommand.IsRunning, TimeSpan.FromMinutes(3));
            Require(main.PhotoGrid.TotalPhotoCount == 10_000, $"Expected 10000, got {main.PhotoGrid.TotalPhotoCount}; {main.StatusText}");
        });
        if (args.Contains("measure"))
        {
            await gridPlaceholder();
            window.Close();
            return;
        }
        async Task gridPlaceholder()
        {
            for (var index = 0; index < 5; index++) await main.PhotoGrid.LoadNextPageAsync();
            if (args.Length > 1 && int.TryParse(args[1], out var seconds)) await MeasureScrollAsync(window, main.PhotoGrid, seconds);
        }
        await CheckAsync("Main.Import.Idempotent", async () =>
        {
            await main.ImportFolderCommand.ExecuteAsync(null);
            Require(main.PhotoGrid.TotalPhotoCount == 10_000, "Duplicate import changed count");
        });
        await CheckAsync("Main.Import.Cancel.RetainsCatalog", async () =>
        {
            var importing = main.ImportFolderCommand.ExecuteAsync(null);
            await Task.Delay(20);
            var cancelTimer = Stopwatch.StartNew();
            main.CancelImportCommand.Execute(null);
            await importing.WaitAsync(TimeSpan.FromSeconds(5));
            Require(cancelTimer.ElapsedMilliseconds <= 1000, $"Import cancellation took {cancelTimer.ElapsedMilliseconds}ms");
            await using var db = provider.GetRequiredService<IDbContextFactory<PhotoDbContext>>().CreateDbContext();
            Require(await db.Photos.CountAsync() == 10_000, "Cancel removed committed records");
        });
        var grid = main.PhotoGrid;
        await CheckAsync("Main.Selection.Rating.Persistence", async () =>
        {
            Require(grid.Photos.Count > 4, "No loaded photos");
            grid.SelectPhotoCommand.Execute(grid.Photos[0]);
            for (var stars = 0; stars <= 5; stars++) await grid.SetRatingAsync(grid.SelectedPhoto!, stars);
            await grid.ToggleFavoriteAsync(grid.SelectedPhoto!);
            await grid.ToggleRejectAsync(grid.SelectedPhoto!);
            await using var db = provider.GetRequiredService<IDbContextFactory<PhotoDbContext>>().CreateDbContext();
            var saved = await db.Photos.SingleAsync(photo => photo.Id == grid.SelectedPhoto!.Id);
            Require(saved.Rating == 5 && saved.IsFavorite && saved.IsRejected, "Rating did not persist");
            Require(grid.SelectedPhoto!.GetModel().Rating == 5, "Export model stayed stale");
        });
        await CheckAsync("Main.Rating.Buttons.100Trials", async () =>
        {
            var selected = grid.SelectedPhoto!;
            var samples = new List<double>();
            for (var trial = 0; trial < 100; trial++)
            {
                var stars = trial % 6;
                var completion = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                var timer = Stopwatch.StartNew();
                System.ComponentModel.PropertyChangedEventHandler changed = (_, change) =>
                {
                    if (change.PropertyName == nameof(PhotoViewModel.Rating) && selected.Rating == stars)
                        completion.TrySetResult(timer.Elapsed.TotalMilliseconds);
                };
                selected.PropertyChanged += changed;
                try
                {
                    InvokeButton(window, stars.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    samples.Add(await completion.Task.WaitAsync(TimeSpan.FromSeconds(5)));
                }
                finally { selected.PropertyChanged -= changed; }
            }
            var p95 = samples.Order().ElementAt(94);
            File.WriteAllText(Path.Combine(_output, "rating-buttons-" + RenderOptions.ProcessRenderMode + ".json"),
                JsonSerializer.Serialize(new
                {
                    Trials = 100,
                    P95Ms = p95,
                    Samples = samples,
                    Limitations = "AutomationPeer invocation to durable display-model change; not physical keyboard/mouse to monitor presentation."
                }, new JsonSerializerOptions { WriteIndented = true }));
            Require(p95 <= 100, $"Rating button response p95 {p95:F2}ms exceeded 100ms");
        });
        await CheckAsync("Main.Navigation.Tree", async () =>
        {
            grid.NavigateRightCommand.Execute(null); grid.NavigateLeftCommand.Execute(null);
            grid.NavigateDownCommand.Execute(null); grid.NavigateUpCommand.Execute(null);
            grid.IsTreeViewMode = true; Require(grid.PhotoTree.Count > 0, "Empty tree");
            grid.IsTreeViewMode = false; await Task.CompletedTask;
        });
        await CheckAsync("Main.Paging.Bounded", async () =>
        {
            for (var page = 0; page < 6; page++) await grid.LoadNextPageAsync();
            Require(grid.Photos.Count <= 1280, $"Retained {grid.Photos.Count}");
        });
        await CheckAsync("Main.Virtualization.ViewportBounded", async () =>
        {
            window.UpdateLayout();
            var scroll = Descendants<ScrollViewer>(window).First(control => control.Name == "PhotoGridScrollViewer");
            foreach (var fraction in new[] { 0d, .5, 1d })
            {
                scroll.ScrollToVerticalOffset(scroll.ScrollableHeight * fraction);
                await Task.Delay(150);
                window.UpdateLayout();
                var realized = Descendants<ListBoxItem>(window).Count();
                Require(realized > 0 && realized < 150, $"Expected bounded viewport containers, got {realized}");
            }
        });
        await CheckAsync("Organization.Tag.Collection", async () =>
        {
            foreach (var photo in grid.Photos.Take(3)) photo.IsBatchSelected = true;
            decisions.Text = "山景-日本語";
            await main.AddTagToBatchCommand.ExecuteAsync(null);
            main.Organization.NewCollectionName = "作品";
            await main.Organization.CreateRootCollectionCommand.ExecuteAsync(null);
            main.Organization.SelectedCollection = main.Organization.Collections.Single();
            main.Organization.NewCollectionName = "印刷";
            await main.Organization.CreateChildCollectionCommand.ExecuteAsync(null);
            main.Organization.SelectedCollection = main.Organization.Collections.Single().Children.Single();
            await main.AddBatchToCollectionCommand.ExecuteAsync(null);
            await grid.SearchAsync("山景");
            Require(grid.TotalPhotoCount == 3, "Tag search mismatch");
            await grid.SearchAsync(null);
        });
        await CheckAsync("Events.Manual.Auto.Idempotent", async () =>
        {
            grid.SelectPhotoCommand.Execute(grid.Photos[0]);
            decisions.Text = "撮影会";
            main.Events.NewEventName = "撮影会";
            await main.CreateEventFromSelectionCommand.ExecuteAsync(null);
            Require(main.Events.Events.Any(occasion => occasion.Name == "撮影会"), "Manual event was not created");
            await using (var db = provider.GetRequiredService<IDbContextFactory<PhotoDbContext>>().CreateDbContext())
                Require(await db.PhotoEventMappings.AnyAsync(mapping => mapping.PhotoId == grid.SelectedPhoto!.Id && mapping.Event.Name == "撮影会"), "Manual event did not contain selected photo");
            await main.Events.AutoGroupPhotosCommand.ExecuteAsync(null);
            await main.Events.ConfirmAutoGroupsCommand.ExecuteAsync(null);
            var count = main.Events.Events.Count;
            await main.Events.ConfirmAutoGroupsCommand.ExecuteAsync(null);
            Require(main.Events.Events.Count == count, "Duplicate event confirmation");
        });
        await CheckAsync("Events.Delete.Cancel.KeepsPhotos", async () =>
        {
            var manual = main.Events.Events.Single(occasion => occasion.Name == "撮影会");
            decisions.Confirm = false;
            await main.Events.DeleteEventCommand.ExecuteAsync(manual.Id);
            Require(main.Events.Events.Any(occasion => occasion.Id == manual.Id), "Cancel deleted event");
            decisions.Confirm = true;
            await main.Events.DeleteEventCommand.ExecuteAsync(manual.Id);
            await using var db = provider.GetRequiredService<IDbContextFactory<PhotoDbContext>>().CreateDbContext();
            Require(!await db.Events.AnyAsync(occasion => occasion.Id == manual.Id), "Confirmed event remains");
            Require(await db.Photos.AnyAsync(photo => photo.Id == grid.SelectedPhoto!.Id), "Event deletion removed photo");
        });
        await CheckAsync("Export.Formats.Crops.Rotations", async () =>
        {
            decisions.Folder = Path.Combine(paths.Root, "Exports"); Directory.CreateDirectory(decisions.Folder);
            foreach (var format in new[] { "JPEG", "PNG", "TIFF" })
            {
                main.Export.OutputFormat = format;
                foreach (var preset in new[] { "1:1", "4:3", "3:2", "16:9" })
                {
                    main.Export.ApplyCropPresetCommand.Execute(preset);
                    main.Export.RotationDegrees = preset == "1:1" ? 0 : preset == "4:3" ? 90 : preset == "3:2" ? 180 : 270;
                    main.Export.MaximumWidth = 640; main.Export.MaximumHeight = 640;
                    await main.Export.ExportBatchAsync([grid.Photos[0].Id]);
                    Require(main.Export.BatchResults.Any(result => result.StartsWith("完了:", StringComparison.Ordinal)), string.Join(";", main.Export.BatchResults));
                }
            }
            Require(Directory.GetFiles(decisions.Folder).Length == 12, "Collision handling lost exports");
            decisions.Folder = photos;
        });
        await CheckAsync("Settings.Save.Reload.CancelClear", async () =>
        {
            main.Settings.JpegQuality = 88;
            main.Settings.EnableGPUAcceleration = false;
            await main.Settings.SaveSettingsCommand.ExecuteAsync(null);
            Require(File.Exists(paths.Settings), "No settings saved");
            Require(File.ReadAllText(paths.Settings).Contains("\"EnableGPUAcceleration\": false", StringComparison.Ordinal), "GPU setting was not persisted");
            var before = Directory.GetFiles(paths.Cache, "*.jpg", SearchOption.AllDirectories);
            decisions.Confirm = false; await main.Settings.ClearCacheCommand.ExecuteAsync(null);
            Require(before.All(File.Exists), "Cancel removed existing cache files");
            decisions.Confirm = true;
        });
        await CheckAsync("Export.Single.Button.AllPlatforms", async () =>
        {
            window.WorkspaceTabs.SelectedIndex = 3;
            grid.SelectPhotoCommand.Execute(grid.Photos[0]);
            decisions.Folder = Path.Combine(paths.Root, "SingleExports");
            Directory.CreateDirectory(decisions.Folder);
            foreach (var platform in new[] { SocialMediaPlatform.Instagram, SocialMediaPlatform.Twitter, SocialMediaPlatform.Facebook })
            {
                main.Export.TargetPlatform = platform;
                window.UpdateLayout();
                InvokeButton(window, "選択写真を単体出力…");
                await UntilAsync(() => !main.ExportSelectedPhotoCommand.IsRunning, TimeSpan.FromSeconds(20));
            }
            Require(Directory.GetFiles(decisions.Folder, "*.jpg").Length == 3, "Single export or collision handling failed");
            decisions.Folder = photos;
            window.WorkspaceTabs.SelectedIndex = 0;
        });
        await CheckAsync("Export.PartialFailure.Retry.Cancel", async () =>
        {
            var repository = provider.GetRequiredService<PhotoRepository>();
            var restoredSource = Path.Combine(paths.Root, "restored-after-failure.jpg");
            var record = await repository.AddAsync(new Photo { FilePath = restoredSource, FileName = Path.GetFileName(restoredSource) });
            try
            {
                decisions.Folder = Path.Combine(paths.Root, "RetryExports");
                Directory.CreateDirectory(decisions.Folder);
                await main.Export.ExportBatchAsync([grid.Photos[0].Id, record.Id]);
                Require(main.Export.BatchResults.Count(result => result.StartsWith("失敗", StringComparison.Ordinal)) == 1, "Expected one individual failure");
                Require(main.Export.BatchResults.Count(result => result.StartsWith("完了", StringComparison.Ordinal)) == 1, "Expected retained successful output");
                File.Copy(grid.Photos[0].FilePath, restoredSource);
                await main.Export.RetryFailuresAsync();
                Require(main.Export.BatchResults.Count == 1 && main.Export.BatchResults[0].StartsWith("完了", StringComparison.Ordinal), "Retry did not target only failure");
                var running = main.Export.ExportBatchAsync(grid.GetLoadedPhotosSnapshot().Take(100).Select(photo => photo.Id).ToArray());
                await Task.Delay(20);
                main.Export.CancelBatchCommand.Execute(null);
                await running.WaitAsync(TimeSpan.FromSeconds(5));
                Require(!main.Export.IsBatchExporting && Directory.GetFiles(decisions.Folder).Length >= 2, "Cancel discarded completed output or stayed busy");
            }
            finally { await repository.DeleteAsync(record.Id); decisions.Folder = photos; }
        });
        await CheckAsync("Catalog.Unregister.KeepsOriginal.RecycleFailure.KeepsRecord", async () =>
        {
            var repository = provider.GetRequiredService<PhotoRepository>();
            var copy = Path.Combine(paths.Root, "recycle-failure-copy.jpg");
            File.Copy(grid.Photos[0].FilePath, copy);
            var record = await repository.AddAsync(new Photo { FilePath = copy, FileName = Path.GetFileName(copy) });
            var photo = new PhotoViewModel(record);
            await grid.DeleteFileAsync(photo); // Scripted shell deliberately fails; this must retain both file and record.
            Require(File.Exists(copy) && await repository.GetByIdAsync(record.Id) is not null, "Failed recycle lost catalog state");
            await grid.DeleteFromDatabaseAsync(photo);
            Require(File.Exists(copy) && await repository.GetByIdAsync(record.Id) is null, "Unregister deleted original or kept record");
        });
        await CheckAsync("Native.Recycle.CopyOnly", async () =>
        {
            var disposableCopy = Path.Combine(paths.Root, "native-recycle-copy.jpg");
            File.Copy(grid.Photos[0].FilePath, disposableCopy);
            await new WindowsPlatformShell().MoveToRecycleBinAsync(disposableCopy);
            Require(!File.Exists(disposableCopy) && File.Exists(grid.Photos[0].FilePath), "Recycle moved the wrong target or did not complete");
        });
        await CheckAsync("ManagedFolders.Save.Reload.Scan", async () =>
        {
            var managed = main.Settings.ManagedFolders!;
            await managed.AddFolderCommand.ExecuteAsync(null);
            managed.SelectedFolder = managed.Folders.Single();
            managed.SelectedFolder.IsRecursive = false;
            decisions.Text = "*000001*";
            await managed.AddPatternCommand.ExecuteAsync(null);
            managed.ExclusionPatterns.Single().Description = "日本語の説明";
            await managed.SaveEditsCommand.ExecuteAsync(null);
            await managed.LoadAsync();
            Require(!managed.Folders.Single().IsRecursive, "Recursive setting not persisted");
            Require(managed.ExclusionPatterns.Single().Description == "日本語の説明", "Pattern edit not persisted");
            managed.SelectedFolder = managed.Folders.Single();
            managed.IsScanning = true;
            Require(!managed.ScanFolderCommand.CanExecute(null) && !managed.ScanAllFoldersCommand.CanExecute(null)
                && !managed.SaveEditsCommand.CanExecute(null) && !managed.RemoveFolderCommand.CanExecute(null), "Scan did not gate conflicting operations");
            managed.IsScanning = false;
            await managed.ScanFolderCommand.ExecuteAsync(null);
        });
        await CheckAsync("Compare.AllPaneCounts", async () =>
        {
            for (var count = 2; count <= 4; count++)
            {
                using var compareScope = provider.CreateScope();
                var workspace = compareScope.ServiceProvider.GetRequiredService<CompareWorkspaceViewModel>();
                await workspace.InitializeAsync(grid.GetLoadedPhotosSnapshot(), 0, count);
                var compareWindow = compareScope.ServiceProvider.GetRequiredService<CompareWindow>();
                compareWindow.Show();
                Require(workspace.Panes.All(pane => pane.Image is not null), "Compare image failed");
                workspace.AdjustZoom(workspace.Panes[0], .25);
                Require(workspace.Panes.All(pane => pane.Zoom == 1.25), "Sync zoom failed");
                workspace.SynchronizeViewport = false; workspace.AdjustZoom(workspace.Panes[0], .25);
                Require(workspace.Panes[1].Zoom == 1.25, "Independent zoom failed");
                var pin = Descendants<ToggleButton>(compareWindow).First(button => Equals(button.Content, "固定"));
                var pinPeer = new ToggleButtonAutomationPeer(pin);
                ((IToggleProvider)pinPeer.GetPattern(PatternInterface.Toggle)).Toggle();
                await Task.Delay(50);
                Require(workspace.Panes[0].IsPinned, "Pin control toggled twice or did not persist");
                workspace.SwapRightCommand.Execute(workspace.Panes[0]);
                workspace.ResetViewportCommand.Execute(null);
                await workspace.SetRatingAsync(workspace.Panes[0], 4);
                await workspace.AdvanceCommand.ExecuteAsync(null);
                await RenderAsync(compareWindow, $"compare-{count}");
                compareWindow.Close();
            }
        });
        await CheckAsync("Viewer.DurableRating.Rendering", async () =>
        {
            var viewer = new PhotoViewerWindow(grid.Photos[0], provider.GetRequiredService<PhotoRatingEditor>(), provider.GetRequiredService<IImageDecodeService>());
            viewer.Show(); await Task.Delay(300);
            InvokeButton(viewer, "3"); await Task.Delay(400);
            await using var db = provider.GetRequiredService<IDbContextFactory<PhotoDbContext>>().CreateDbContext();
            Require((await db.Photos.SingleAsync(photo => photo.Id == grid.Photos[0].Id)).Rating == 3, "Viewer rating did not persist");
            foreach (var position in Enumerable.Range(0, 5))
            {
                viewer.OverlayPositionComboBox.SelectedIndex = position;
                viewer.UpdateLayout();
                Require(viewer.ExifPreviewBorder.Margin.Left >= 0 && viewer.ExifPreviewBorder.Margin.Top >= 0, "Invalid preview position");
            }
            viewer.OverlayPositionComboBox.SelectedIndex = 3;
            viewer.UpdateLayout();
            Require(viewer.ExifPreviewBorder.Margin.Left > 0 && viewer.ExifPreviewBorder.Margin.Top > 0, "Bottom-right preset did not move preview");
            await RenderAsync(viewer, "viewer"); viewer.Close();
            Require(viewer.PhotoImage.Source is null && viewer.PreviewImage.Source is null, "Closed viewer retains full images");
        });
        await CheckAsync("Raw.AllDownloadedFormats.Decode", async () =>
        {
            var raw = provider.GetRequiredService<RawThumbnailGenerator>();
            foreach (var file in Directory.GetFiles(Path.Combine(_output, "data", "raw"), "sample.*"))
            {
                await CheckAsync("Raw." + Path.GetExtension(file), async () =>
                {
                    var thumbnail = await raw.GenerateAsync(file, 512);
                    Require(thumbnail.Length > 0, "Embedded JPEG thumbnail unavailable");
                    var surface = await provider.GetRequiredService<ImageLoader>().LoadAsync(file);
                    Require(surface is { IsFrozen: true } && surface.PixelWidth > 0, "RAW thumbnail surface failed");
                    var bytes = await raw.ExtractEmbeddedJpegBytesAsync(file);
                    Require(bytes.Length > 0, "Embedded JPEG preview unavailable");
                    using var image = ImageSharpImage.Load(bytes);
                    Require(image.Width > 0 && image.Height > 0, "Invalid dimensions");
                });
            }
        });
        await CheckAsync("Folder.Filter.Save.Preview", async () =>
        {
            using var folderScope = provider.CreateScope();
            var folder = folderScope.ServiceProvider.GetRequiredService<FolderModeViewModel>();
            var folderWindow = folderScope.ServiceProvider.GetRequiredService<FolderModeWindow>();
            folderWindow.Show(); await folder.LoadFolderAsync(photos);
            Require(folder.DisplayPhotos.Count == 10_000, "Folder count mismatch");
            folder.SelectPhotoCommand.Execute(folder.DisplayPhotos[0]);
            await folder.SetRatingCommand.ExecuteAsync("5"); await folder.ToggleFavoriteCommand.ExecuteAsync(null);
            await folder.ToggleRejectCommand.ExecuteAsync(null); await folder.SaveSessionCommand.ExecuteAsync(null);
            folder.FilterMinRating = 5; Require(folder.DisplayPhotos.Count == 1, "Rating filter mismatch");
            folder.ResetFilterCommand.Execute(null); folder.FilterNameSearch = "000001";
            Require(folder.DisplayPhotos.Count == 1, "Name filter mismatch");
            folder.ResetFilterCommand.Execute(null);
            folder.NavigateEndCommand.Execute(null); folder.NavigateHomeCommand.Execute(null);
            var preview = new PhotoPreviewWindow(folder); preview.Show();
            await RenderAsync(preview, "preview"); preview.Close();
            await RenderAsync(folderWindow, "folder"); folderWindow.Close();
        });
        for (var index = 0; index < 5; index++)
        {
            window.WorkspaceTabs.SelectedIndex = index;
            await RenderAsync(window, "main-tab-" + index);
        }
        window.WorkspaceTabs.SelectedIndex = 0;
        foreach (var size in new[] { (1366d, 768d), (1280d, 720d), (960d, 680d) })
        {
            window.Width = size.Item1; window.Height = size.Item2;
            for (var tab = 0; tab < 5; tab++)
            {
                window.WorkspaceTabs.SelectedIndex = tab;
                await RenderAsync(window, $"main-{size.Item1}-{size.Item2}-dip-tab-{tab}");
            }
        }
        await CheckAsync("SettingsDialogs.Render", async () =>
        {
            var settings = scope.ServiceProvider.GetRequiredService<FolderModeSettingsWindow>(); settings.Show();
            await RenderAsync(settings, "folder-settings"); settings.Close();
            var shortcuts = scope.ServiceProvider.GetRequiredService<KeyboardShortcutsWindow>(); shortcuts.Show();
            await RenderAsync(shortcuts, "shortcuts"); shortcuts.Close();
        });
        await CheckAsync("Main.Input.ValidationGate", async () =>
        {
            window.WorkspaceTabs.SelectedIndex = 3; window.UpdateLayout();
            var box = Descendants<TextBox>(window).First(control => AutomationProperties.GetAutomationId(control) == "MainWindow.Export.CropX");
            var original = box.Text; box.Text = "invalid";
            box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Require(Validation.GetHasError(box), "Numeric conversion failure was not visible");
            Require(!main.ExportBatchSelectionCommand.CanExecute(null) && !main.Settings.SaveSettingsCommand.CanExecute(null), "Invalid input still allowed submission");
            box.Text = original; box.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Require(!main.HasInputErrors, "Corrected input remained blocked");
            var rotation = Descendants<ComboBox>(window).First(control => AutomationProperties.GetAutomationId(control) == "MainWindow.Export.RotationDegrees");
            foreach (var degrees in new[] { 0, 90, 180, 270 })
            {
                main.Export.RotationDegrees = degrees;
                Require(rotation.SelectedItem is not null, "Typed rotation tag did not select its item");
            }
            await Task.CompletedTask;
        });
        await CheckAsync("Main.Input.KeysDoNotRateWhileTyping", async () =>
        {
            window.WorkspaceTabs.SelectedIndex = 0; window.UpdateLayout();
            var search = Descendants<TextBox>(window).First(control => AutomationProperties.GetAutomationId(control) == "MainWindow.SearchText");
            grid.SelectPhotoCommand.Execute(grid.Photos[0]);
            var favorite = grid.SelectedPhoto!.IsFavorite;
            search.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(window)!, 0, System.Windows.Input.Key.F)
            { RoutedEvent = System.Windows.Input.Keyboard.KeyDownEvent });
            await Task.Delay(50);
            Require(grid.SelectedPhoto.IsFavorite == favorite, "Typing F changed favorite state");
        });
        await CheckAsync("Dialogs.Save.Cancel.ShortcutConflict", async () =>
        {
            var folderSettings = new FolderModeSettingsViewModel(paths) { DefaultThumbnailSize = 280 };
            var save = new FolderModeSettingsWindow(folderSettings);
            _ = save.Dispatcher.InvokeAsync(() => InvokeButton(save, "保存して閉じる"), DispatcherPriority.ApplicationIdle);
            Require(save.ShowDialog() == true && File.Exists(paths.FolderSettings), "Settings modal save failed");
            var before = File.ReadAllBytes(paths.FolderSettings);
            folderSettings.DefaultThumbnailSize = 360;
            var cancel = new FolderModeSettingsWindow(folderSettings);
            _ = cancel.Dispatcher.InvokeAsync(() => InvokeButton(cancel, "キャンセル"), DispatcherPriority.ApplicationIdle);
            Require(cancel.ShowDialog() == false && File.ReadAllBytes(paths.FolderSettings).SequenceEqual(before), "Cancel persisted changes");
            var keys = new KeyboardShortcutsViewModel(new ShortcutService(paths));
            var entry = keys.Shortcuts.Single(item => item.CommandName == "ToggleReject");
            keys.StartCaptureCommand.Execute(entry); keys.CaptureKey(System.Windows.Input.Key.Z, System.Windows.Input.ModifierKeys.None);
            Require(entry.KeyDisplay == "Z", "Captured key did not update display");
            keys.Save();
            keys.StartCaptureCommand.Execute(entry); keys.CaptureKey(System.Windows.Input.Key.F, System.Windows.Input.ModifierKeys.None);
            try { keys.Save(); throw new InvalidOperationException("Duplicate key was accepted"); } catch (ArgumentException) { }
            Require(new ShortcutService(paths).Load().Single(item => item.CommandName == "ToggleReject").Key == System.Windows.Input.Key.Z, "Rejected key overwrote preferences");
            await Task.CompletedTask;
        });
        if (args.Length > 1 && int.TryParse(args[1], out var duration) && duration > 0)
            await MeasureScrollAsync(window, grid, duration);
        window.Close();
    }

    private static string CreatePhotos(string directory, int count)
    {
        Directory.CreateDirectory(directory);
        var patterns = new byte[128][];
        for (var color = 0; color < patterns.Length; color++)
        {
            using var image = new SixLabors.ImageSharp.Image<Rgb24>(640, 480, new Rgb24((byte)(35 + color), (byte)(60 + color), (byte)(90 + color)));
            using var stream = new MemoryStream();
            image.Save(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder());
            patterns[color] = stream.ToArray();
        }
        for (var index = 0; index < count; index++)
        {
            var path = Path.Combine(directory, $"写真-{index:D6}.jpg");
            if (!File.Exists(path)) File.WriteAllBytes(path, patterns[index % patterns.Length]);
        }
        return directory;
    }

    private static async Task MeasureScrollAsync(MainWindow window, PhotoGridViewModel grid, int duration)
    {
        var watch = Stopwatch.StartNew();
        var samples = new List<object>();
        var latency = new List<double>();
        var viewer = Descendants<ScrollViewer>(window).First(control => control.Name == "PhotoGridScrollViewer");
        var frames = 0;
        EventHandler handler = (_, _) => frames++;
        CompositionTarget.Rendering += handler;
        try
        {
            while (watch.Elapsed.TotalSeconds < duration)
            {
                viewer.ScrollToVerticalOffset(viewer.ScrollableHeight * ((watch.Elapsed.TotalSeconds % 20) / 20));
                var start = Stopwatch.GetTimestamp();
                await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.Input);
                latency.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                if (latency.Count % 100 == 0)
                {
                    using var process = Process.GetCurrentProcess(); process.Refresh();
                    samples.Add(new
                    {
                        Seconds = watch.Elapsed.TotalSeconds,
                        WorkingSet = process.WorkingSet64,
                        PrivateBytes = process.PrivateMemorySize64,
                        Handles = process.HandleCount,
                        Managed = GC.GetTotalMemory(false),
                        Gen2Collections = GC.CollectionCount(2),
                        Retained = grid.Photos.Count,
                        FirstPhotoId = grid.Photos.FirstOrDefault()?.Id,
                        LastPhotoId = grid.Photos.LastOrDefault()?.Id,
                        Frames = frames
                    });
                    File.WriteAllText(Path.Combine(_output, "scroll-progress.json"), JsonSerializer.Serialize(samples));
                    Console.WriteLine($"Scroll {watch.Elapsed.TotalSeconds:F0}s, memory {process.WorkingSet64 / 1048576} MB");
                }
                await Task.Delay(50);
            }
        }
        finally { CompositionTarget.Rendering -= handler; }
        var sorted = latency.Order().ToArray();
        File.WriteAllText(Path.Combine(_output, "scroll-result.json"), JsonSerializer.Serialize(new
        {
            DurationSeconds = watch.Elapsed.TotalSeconds,
            Frames = frames,
            AverageRenderCallbackFps = frames / watch.Elapsed.TotalSeconds,
            DispatcherP95Ms = sorted[(int)Math.Ceiling(sorted.Length * .95) - 1],
            Samples = samples,
            Limitations = "Dispatcher probes are not OS input-to-present latency. Rendering callbacks are not physical monitor FPS."
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task RenderAsync(Window window, string name)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var folder = Path.Combine(_output, "screenshots", RenderOptions.ProcessRenderMode.ToString()); Directory.CreateDirectory(folder);
        using var stream = File.Create(Path.Combine(folder, name + ".png")); encoder.Save(stream);
    }

    private static void InvokeButton(Window window, string content)
    {
        var button = Descendants<Button>(window).First(control => Equals(control.Content, content));
        var peer = new ButtonAutomationPeer(button);
        ((IInvokeProvider)peer.GetPattern(PatternInterface.Invoke)).Invoke();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T typed) yield return typed;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static async Task UntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        await Task.Delay(100);
        var watch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.Elapsed > timeout) throw new TimeoutException();
            await Task.Delay(100);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static async Task CheckAsync(string id, Func<Task> action)
    {
        try { await action(); Results.Add(new(id, true, "Production services / WPF control path")); Console.WriteLine("PASS " + id); }
        catch (Exception exception) { Results.Add(new(id, false, exception.ToString())); _exitCode = 1; Console.WriteLine("FAIL " + id + ": " + exception.Message); }
    }

    private sealed record CheckResult(string Id, bool Passed, string Evidence);

    private sealed class ScriptedInteraction : IUserInteractionService
    {
        public string? Folder { get; set; }
        public string? Text { get; set; }
        public bool Confirm { get; set; } = true;
        public Task NotifyAsync(string title, string message, UserNotificationKind kind, CancellationToken cancellationToken = default) { Console.WriteLine($"NOTICE {title}: {message}"); return Task.CompletedTask; }
        public Task<bool> ConfirmAsync(string title, string message, CancellationToken cancellationToken = default) => Task.FromResult(Confirm);
        public Task<string?> SelectFolderAsync(string description, CancellationToken cancellationToken = default) => Task.FromResult(Folder);
        public Task<string?> PromptTextAsync(string title, string message, CancellationToken cancellationToken = default) => Task.FromResult(Text);
    }

    private sealed class ScriptedShell : IPlatformShell
    {
        public void OpenFile(string filePath) { }
        public void ShowInFileBrowser(string filePath) { }
        public void SetClipboardText(string text) { }
        public Task MoveToRecycleBinAsync(string filePath, CancellationToken cancellationToken = default) => throw new NotSupportedException("Native recycle-bin behavior requires a separate OS test.");
    }
}
