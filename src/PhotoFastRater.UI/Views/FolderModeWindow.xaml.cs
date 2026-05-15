using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PhotoFastRater.UI.Models;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class FolderModeWindow : Window
{
    private readonly FolderModeViewModel _vm;
    private readonly ShortcutService _shortcutService;
    private readonly IServiceProvider _serviceProvider;
    private readonly Dictionary<string, ICommand> _commandMap;
    private List<ShortcutEntry> _activeShortcuts = new();
    private PhotoPreviewWindow? _previewWindow;

    public FolderModeWindow(FolderModeViewModel viewModel, ShortcutService shortcutService, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _vm = viewModel;
        _shortcutService = shortcutService;
        _serviceProvider = serviceProvider;
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
            ["NavigateHome"]   = viewModel.NavigateHomeCommand,
            ["NavigateEnd"]    = viewModel.NavigateEndCommand,
        };

        viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FolderModeViewModel.SelectedPhoto))
            {
                Dispatcher.InvokeAsync(ScrollSelectedIntoView, DispatcherPriority.Loaded);
                if (_vm.ShowOriginalImages)
                    _vm.SelectedPhoto?.TriggerFullImageLoad();
            }
            else if (e.PropertyName == nameof(FolderModeViewModel.ShowOriginalImages) && _vm.ShowOriginalImages)
            {
                _vm.SelectedPhoto?.TriggerFullImageLoad();
                _ = TriggerVisibleFullImageLoadAsync();
            }
            else if (e.PropertyName == nameof(FolderModeViewModel.IsInlinePreviewMode))
            {
                UpdateInlinePreviewLayout();
            }
        };

        viewModel.ShortcutsUpdated += () => _activeShortcuts = _shortcutService.Load();

        viewModel.BoundaryReached += atStart =>
        {
            Dispatcher.InvokeAsync(() => FlashBoundaryItem(atStart));
        };

        _activeShortcuts = _shortcutService.Load();
        PreviewKeyDown += HandleShortcutKeys;

        Loaded += (_, _) =>
        {
            PhotoScrollViewer.ScrollChanged += OnPhotoScrollChanged;
        };
    }

    private void OnPhotoScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (Math.Abs(e.VerticalChange) < 1 && Math.Abs(e.HorizontalChange) < 1) return;
        _ = LoadVisibleThumbnailsAsync();
        if (_vm.ShowOriginalImages)
            _ = TriggerVisibleFullImageLoadAsync();
    }

    private async Task LoadVisibleThumbnailsAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);

        var top = PhotoScrollViewer.VerticalOffset;
        var bottom = top + PhotoScrollViewer.ViewportHeight;

        for (int i = 0; i < _vm.DisplayPhotos.Count; i++)
        {
            var photo = _vm.DisplayPhotos[i];
            if (photo.Thumbnail != null) continue;

            try
            {
                if (PhotoGrid.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement container)
                    continue;

                var pos = container.TransformToVisual(PhotoScrollViewer).Transform(new System.Windows.Point(0, 0));
                if (pos.Y < bottom && pos.Y + container.ActualHeight > top)
                    _ = _vm.LoadThumbnailIfMissingAsync(photo);
            }
            catch (InvalidOperationException) { }
        }
    }

    private async Task TriggerVisibleFullImageLoadAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        var top = PhotoScrollViewer.VerticalOffset;
        var bottom = top + PhotoScrollViewer.ViewportHeight;
        for (int i = 0; i < _vm.DisplayPhotos.Count; i++)
        {
            try
            {
                if (PhotoGrid.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
                var pos = c.TransformToVisual(PhotoScrollViewer).Transform(new System.Windows.Point(0, 0));
                if (pos.Y < bottom && pos.Y + c.ActualHeight > top)
                    _vm.DisplayPhotos[i].TriggerFullImageLoad();
            }
            catch (InvalidOperationException) { }
        }
    }

    private void HandleShortcutKeys(object sender, System.Windows.Input.KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = Keyboard.Modifiers;

        // PageUp/Down はビューポート計算が必要なのでここで直接処理
        if (key == Key.PageDown && modifiers == ModifierKeys.None)
        {
            NavigatePageDown(); e.Handled = true; return;
        }
        if (key == Key.PageUp && modifiers == ModifierKeys.None)
        {
            NavigatePageUp(); e.Handled = true; return;
        }

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

    private void NavigatePageDown()
    {
        if (_vm.DisplayPhotos.Count == 0) return;
        var bottom = PhotoScrollViewer.VerticalOffset + PhotoScrollViewer.ViewportHeight;
        int lastFullyVisible = -1;
        for (int i = 0; i < _vm.DisplayPhotos.Count; i++)
        {
            try
            {
                if (PhotoGrid.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
                var pos = c.TransformToVisual(PhotoScrollViewer).Transform(new System.Windows.Point(0, 0));
                if (pos.Y >= 0 && pos.Y + c.ActualHeight <= bottom)
                    lastFullyVisible = i;
            }
            catch (InvalidOperationException) { }
        }
        var target = lastFullyVisible >= 0 ? Math.Min(lastFullyVisible + 1, _vm.DisplayPhotos.Count - 1) : _vm.DisplayPhotos.Count - 1;
        _vm.SelectPhotoCommand.Execute(_vm.DisplayPhotos[target]);
    }

    private void NavigatePageUp()
    {
        if (_vm.DisplayPhotos.Count == 0) return;
        var top = PhotoScrollViewer.VerticalOffset;
        var bottom = top + PhotoScrollViewer.ViewportHeight;
        int firstFullyVisible = -1;
        for (int i = 0; i < _vm.DisplayPhotos.Count; i++)
        {
            try
            {
                if (PhotoGrid.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
                var pos = c.TransformToVisual(PhotoScrollViewer).Transform(new System.Windows.Point(0, 0));
                if (pos.Y >= 0 && pos.Y + c.ActualHeight <= bottom)
                {
                    firstFullyVisible = i;
                    break;
                }
            }
            catch (InvalidOperationException) { }
        }
        var target = firstFullyVisible > 0 ? firstFullyVisible - 1 : 0;
        _vm.SelectPhotoCommand.Execute(_vm.DisplayPhotos[target]);
    }

    private void FlashBoundaryItem(bool atStart)
    {
        if (_vm.DisplayPhotos.Count == 0) return;
        var index = atStart ? 0 : _vm.DisplayPhotos.Count - 1;
        try
        {
            if (PhotoGrid.ItemContainerGenerator.ContainerFromIndex(index) is not FrameworkElement fe) return;
            // Grid の最初の子 (Border) を取得
            var border = fe is Border b ? b : (fe is ContentPresenter cp
                ? VisualTreeHelper.GetChild(cp, 0) as Border
                : null);
            if (border == null && VisualTreeHelper.GetChildrenCount(fe) > 0)
                border = VisualTreeHelper.GetChild(fe, 0) as Border;
            if (border == null) return;

            var brush = new SolidColorBrush(Colors.Gray);
            border.BorderBrush = brush;
            var anim = new ColorAnimation(Colors.Red, Colors.Gray, new Duration(TimeSpan.FromMilliseconds(600)));
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
        catch { }
    }

    private void ScrollSelectedIntoView()
    {
        if (_vm.SelectedPhoto is null) return;
        var index = _vm.DisplayPhotos.IndexOf(_vm.SelectedPhoto);
        if (index < 0) return;
        if (PhotoGrid.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container)
            container.BringIntoView();
    }

    private void OpenPreview_Click(object sender, RoutedEventArgs e)
    {
        _vm.SelectedPhoto?.TriggerFullImageLoad();
        if (_previewWindow != null && _previewWindow.IsLoaded)
        {
            _previewWindow.Activate();
            return;
        }
        _previewWindow = _serviceProvider.GetRequiredService<PhotoPreviewWindow>();
        _previewWindow.Show();
    }

    private void UpdateInlinePreviewLayout()
    {
        if (_vm.IsInlinePreviewMode)
        {
            InlinePreviewColumn.Width = new System.Windows.GridLength(400, System.Windows.GridUnitType.Pixel);
            InlinePreviewSplitterColumn.Width = new System.Windows.GridLength(5, System.Windows.GridUnitType.Pixel);
        }
        else
        {
            InlinePreviewColumn.Width = new System.Windows.GridLength(0);
            InlinePreviewSplitterColumn.Width = new System.Windows.GridLength(0);
        }
    }

    private void CopyFileName_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FolderSessionPhotoViewModel vm)
            System.Windows.Clipboard.SetText(vm.FileName);
    }

    private void CopyFilePath_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FolderSessionPhotoViewModel vm)
            System.Windows.Clipboard.SetText(vm.FilePath);
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
