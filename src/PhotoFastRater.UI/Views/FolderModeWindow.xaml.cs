using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using PhotoFastRater.UI.Models;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class FolderModeWindow : Window
{
    private readonly FolderModeViewModel _vm;
    private readonly ShortcutService _shortcutService;
    private readonly Dictionary<string, ICommand> _commandMap;
    private List<ShortcutEntry> _activeShortcuts = new();

    public FolderModeWindow(FolderModeViewModel viewModel, ShortcutService shortcutService)
    {
        InitializeComponent();
        _vm = viewModel;
        _shortcutService = shortcutService;
        DataContext = viewModel;

        _commandMap = new Dictionary<string, ICommand>
        {
            ["NavigateUp"]     = viewModel.NavigateUpCommand,
            ["NavigateDown"]   = viewModel.NavigateDownCommand,
            ["NavigateLeft"]   = viewModel.NavigateLeftCommand,
            ["NavigateRight"]  = viewModel.NavigateRightCommand,
            ["OpenFolder"]     = viewModel.OpenFolderCommand,
            ["SetRating0"]     = new RelayCommand(() => viewModel.SetRatingCommand.Execute("0")),
            ["SetRating1"]     = new RelayCommand(() => viewModel.SetRatingCommand.Execute("1")),
            ["SetRating2"]     = new RelayCommand(() => viewModel.SetRatingCommand.Execute("2")),
            ["SetRating3"]     = new RelayCommand(() => viewModel.SetRatingCommand.Execute("3")),
            ["SetRating4"]     = new RelayCommand(() => viewModel.SetRatingCommand.Execute("4")),
            ["SetRating5"]     = new RelayCommand(() => viewModel.SetRatingCommand.Execute("5")),
            ["ToggleFavorite"] = viewModel.ToggleFavoriteCommand,
            ["ToggleReject"]   = viewModel.ToggleRejectCommand,
            ["ReloadFolder"]   = viewModel.ReloadFolderCommand,
        };

        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FolderModeViewModel.SelectedPhoto))
                Dispatcher.InvokeAsync(ScrollSelectedIntoView, DispatcherPriority.Loaded);
        };

        viewModel.ShortcutsUpdated += () => _activeShortcuts = _shortcutService.Load();

        _activeShortcuts = _shortcutService.Load();
        PreviewKeyDown += HandleShortcutKeys;
    }

    private void HandleShortcutKeys(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        foreach (var entry in _activeShortcuts)
        {
            if (entry.Key != key || entry.Modifiers != modifiers) continue;
            if (!_commandMap.TryGetValue(entry.CommandName, out var cmd)) continue;
            if (!cmd.CanExecute(null)) continue;
            cmd.Execute(null);
            e.Handled = true;
            return;
        }
    }

    private void ScrollSelectedIntoView()
    {
        if (_vm.SelectedPhoto is null) return;
        var index = _vm.DisplayPhotos.IndexOf(_vm.SelectedPhoto);
        if (index < 0) return;
        if (PhotoGrid.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
            container.BringIntoView();
    }

    public async void LoadFolder(string folderPath)
    {
        if (DataContext is FolderModeViewModel viewModel)
            await viewModel.LoadFolderAsync(folderPath);
    }

    public void OpenFolderDialog()
    {
        if (DataContext is FolderModeViewModel viewModel)
            viewModel.OpenFolderCommand.Execute(null);
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] paths) return;
        var folder = paths.FirstOrDefault(System.IO.Directory.Exists);
        if (folder != null && DataContext is FolderModeViewModel viewModel)
            await viewModel.LoadFolderAsync(folder);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is FolderModeViewModel viewModel && PhotoGrid != null)
            viewModel.NotifyGridWidth(PhotoGrid.ActualWidth);
    }
}
