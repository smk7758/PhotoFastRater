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
        return scope.ServiceProvider.GetRequiredService<KeyboardShortcutsWindow>().ShowDialog() == true;
    }

    /// <summary>Shows folder settings while preserving the caller-owned settings model.</summary>
    public bool ShowFolderSettingsDialog(FolderModeSettingsViewModel settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        using var scope = _scopeFactory.CreateScope();
        var window = scope.ServiceProvider.GetRequiredService<FolderModeSettingsWindow>();
        window.DataContext = settings;
        return window.ShowDialog() == true;
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
