using Microsoft.Extensions.DependencyInjection;
using PhotoFastRater.UI.ViewModels;
using PhotoFastRater.UI.Views;

namespace PhotoFastRater.UI.Services;

/// <summary>Creates one dependency-injection scope per top-level window.</summary>
public sealed class WindowManager
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Initializes a window composition boundary.</summary>
    public WindowManager(IServiceScopeFactory scopeFactory) => _scopeFactory = scopeFactory;

    /// <summary>Shows the library window and disposes its scope when it closes.</summary>
    public MainWindow ShowMainWindow() => ShowScopedWindow<MainWindow>();

    /// <summary>Shows a comparison workspace whose decode lifetime ends with the window scope.</summary>
    public async Task<CompareWindow?> ShowCompareWindowAsync(
        IReadOnlyList<PhotoViewModel> loadedPhotos,
        int selectedIndex,
        int paneCount)
    {
        ArgumentNullException.ThrowIfNull(loadedPhotos);
        if (paneCount is < 2 or > 4 || selectedIndex < 0 || loadedPhotos.Count < paneCount)
            return null;

        var scope = _scopeFactory.CreateScope();
        CompareWindow? window = null;
        try
        {
            var viewModel = scope.ServiceProvider.GetRequiredService<CompareWorkspaceViewModel>();
            window = scope.ServiceProvider.GetRequiredService<CompareWindow>();
            window.Closed += (_, _) => scope.Dispose();
            window.Show();
            await viewModel.InitializeAsync(loadedPhotos, selectedIndex, paneCount);
            return window;
        }
        catch
        {
            if (window?.IsVisible == true)
                window.Close();
            else
                scope.Dispose();
            throw;
        }
    }

    /// <summary>Shows an independent folder workspace and optionally starts loading a path.</summary>
    public FolderModeWindow ShowFolderWindow(string? folderPath = null, bool openDialogWhenEmpty = false)
    {
        var window = ShowScopedWindow<FolderModeWindow>();
        if (!string.IsNullOrWhiteSpace(folderPath))
            window.LoadFolder(folderPath);
        else if (openDialogWhenEmpty)
            window.OpenFolderDialog();
        return window;
    }

    /// <summary>Shows shortcut settings in an isolated dialog scope.</summary>
    public bool ShowKeyboardShortcutsDialog()
    {
        using var scope = _scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<KeyboardShortcutsWindow>();
        SetDialogOwner(window);
        return window.ShowDialog() == true;
    }

    /// <summary>Shows folder settings while preserving the caller-owned settings model.</summary>
    public bool ShowFolderSettingsDialog(FolderModeSettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var scope = _scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<FolderModeSettingsWindow>();
        window.DataContext = settings;
        SetDialogOwner(window);
        return window.ShowDialog() == true;
    }

    private static void SetDialogOwner(System.Windows.Window dialog)
    {
        var owner = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>()
            .FirstOrDefault(window => window.IsActive && !ReferenceEquals(window, dialog))
            ?? System.Windows.Application.Current.MainWindow;
        if (owner is not { IsVisible: true } || ReferenceEquals(owner, dialog)) return;
        // Settings belong to the workspace that opened them, including when that workspace is on another monitor.
        dialog.Owner = owner;
        dialog.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
    }

    private TWindow ShowScopedWindow<TWindow>() where TWindow : System.Windows.Window
    {
        var scope = _scopeFactory.CreateScope();
        try
        {
            var window = scope.ServiceProvider.GetRequiredService<TWindow>();
            window.Closed += (_, _) => scope.Dispose();
            window.Show();
            return window;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
