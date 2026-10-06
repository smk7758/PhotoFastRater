using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class MainWindow : Window
{
    private readonly WindowManager _windowManager;
    private ScrollViewer? PhotoGridScrollViewer;
    private bool _isLoadingNextPage;
    private bool _isClosed;
    private CancellationTokenSource? _visibleLoadCancellation;
    private readonly HashSet<object> _invalidInputs = [];

    public MainWindow(MainViewModel viewModel, WindowManager windowManager)
    {
        InitializeComponent();
        DataContext = viewModel;
        _windowManager = windowManager;
        AddHandler(System.Windows.Controls.Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>((_, args) =>
        {
            if (args.Action == ValidationErrorEventAction.Added) _invalidInputs.Add(args.OriginalSource);
            else _invalidInputs.Remove(args.OriginalSource);
            viewModel.HasInputErrors = _invalidInputs.Count > 0;
        }));

        PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                int delta = e.Delta > 0 ? 20 : -20;
                viewModel.PhotoGrid.ThumbnailSize = Math.Clamp(viewModel.PhotoGrid.ThumbnailSize + delta, 80, 600);
                e.Handled = true;
            }
        };

        Loaded += async (_, _) =>
        {
            PhotoGridScrollViewer = FindScrollViewer(PhotoGridList);
            if (PhotoGridScrollViewer != null)
            {
                PhotoGridScrollViewer.Name = "PhotoGridScrollViewer";
                PhotoGridScrollViewer.ScrollChanged += PhotoGridScrollViewer_ScrollChanged;
                PhotoGridScrollViewer.SizeChanged += (s, e) =>
                    viewModel.PhotoGrid.NotifyGridWidth(e.NewSize.Width);
                viewModel.PhotoGrid.NotifyGridWidth(PhotoGridScrollViewer.ActualWidth);
            }
            await viewModel.LoadPhotosCommand.ExecuteAsync(null);
        };
        KeyDown += HandlePhotoKeys;
        Closed += (_, _) => { _isClosed = true; _visibleLoadCancellation?.Cancel(); _visibleLoadCancellation?.Dispose(); };
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
            if (FindScrollViewer(System.Windows.Media.VisualTreeHelper.GetChild(root, index)) is { } match) return match;
        return null;
    }
    private async void HandlePhotoKeys(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || WorkspaceTabs.SelectedIndex != 0 ||
            KeyboardInputPolicy.IsControlInput(e.OriginalSource as DependencyObject) || DataContext is not MainViewModel vm)
            return;
        var grid = vm.PhotoGrid;
        int stars = e.Key >= Key.D0 && e.Key <= Key.D5 ? e.Key - Key.D0
            : e.Key >= Key.NumPad0 && e.Key <= Key.NumPad5 ? e.Key - Key.NumPad0 : -1;
        if (stars >= 0) await grid.SetRatingCommand.ExecuteAsync(stars);
        else if (e.Key == Key.F) await grid.ToggleFavoriteCommand.ExecuteAsync(null);
        else if (e.Key == Key.X) await grid.ToggleRejectCommand.ExecuteAsync(null);
        else if (e.Key == Key.Up) grid.NavigateUpCommand.Execute(null);
        else if (e.Key == Key.Down) grid.NavigateDownCommand.Execute(null);
        else if (e.Key == Key.Left) grid.NavigateLeftCommand.Execute(null);
        else if (e.Key == Key.Right) grid.NavigateRightCommand.Execute(null);
        else if (e.Key == Key.Enter && grid.SelectedPhoto is { } photo) grid.OpenPhotoCommand.Execute(photo);
        else return;
        e.Handled = true;
    }

    private void OpenFolderMode_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _windowManager.ShowFolderWindow();
    }

    private async void OpenCompare_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
            return;
        var paneCount = sender is System.Windows.Controls.Button { Tag: string tag } && int.TryParse(tag, out var parsed)
            ? parsed
            : 2;
        var loadedPhotos = viewModel.PhotoGrid.GetLoadedPhotosSnapshot();
        if (loadedPhotos.Count < paneCount || viewModel.PhotoGrid.SelectedLoadedIndex < 0)
        {
            await viewModel.ShowCompareRequirementAsync();
            return;
        }
        await _windowManager.ShowCompareWindowAsync(
            loadedPhotos,
            viewModel.PhotoGrid.SelectedLoadedIndex,
            paneCount);
    }

    private async void PhotoGridScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isClosed) return;
        if (DataContext is MainViewModel visibleModel && e.VerticalChange != 0)
        {
            _visibleLoadCancellation?.Cancel();
            _visibleLoadCancellation?.Dispose();
            _visibleLoadCancellation = new CancellationTokenSource();
            var cell = visibleModel.PhotoGrid.ThumbnailSize + 8;
            var columns = visibleModel.PhotoGrid.GridColumns;
            var start = Math.Max(0, (int)(e.VerticalOffset / cell) * columns);
            var count = ((int)(e.ViewportHeight / cell) + 2) * columns;
            await visibleModel.PhotoGrid.LoadVisiblePhotosAsync(start, count, _visibleLoadCancellation.Token);
        }
        if (_isLoadingNextPage || DataContext is not MainViewModel viewModel || !viewModel.PhotoGrid.Photos.HasMore)
            return;
        if (e.VerticalOffset < e.ExtentHeight - e.ViewportHeight * 3)
            return;

        _isLoadingNextPage = true;
        try
        {
            await viewModel.PhotoGrid.LoadNextPageAsync();
        }
        finally
        {
            _isLoadingNextPage = false;
        }
    }

    private void CollectionTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is MainViewModel viewModel)
            viewModel.Organization.SelectedCollection = e.NewValue as CollectionNodeViewModel;
    }

    private async void RatingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem &&
            menuItem.Tag is string ratingStr &&
            int.TryParse(ratingStr, out var rating))
        {
            var photo = GetPhotoFromContextMenu(menuItem);
            if (photo != null && DataContext is MainViewModel mainViewModel)
            {
                await mainViewModel.PhotoGrid.SetRatingAsync(photo, rating);
            }
        }
    }

    private async void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            var photo = GetPhotoFromContextMenu(menuItem);
            if (photo != null && DataContext is MainViewModel mainViewModel)
            {
                await mainViewModel.PhotoGrid.ToggleFavoriteAsync(photo);
            }
        }
    }

    private async void ToggleReject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            var photo = GetPhotoFromContextMenu(menuItem);
            if (photo != null && DataContext is MainViewModel mainViewModel)
            {
                await mainViewModel.PhotoGrid.ToggleRejectAsync(photo);
            }
        }
    }

    private async void ExportToSocialMedia_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            var photo = GetPhotoFromContextMenu(menuItem);
            if (photo != null && DataContext is MainViewModel mainViewModel)
            {
                await mainViewModel.PhotoGrid.ExportToSocialMediaAsync(photo);
            }
        }
    }

    private async void DeleteFromDatabase_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            var photo = GetPhotoFromContextMenu(menuItem);
            if (photo != null && DataContext is MainViewModel mainViewModel)
            {
                var result = System.Windows.MessageBox.Show(
                    $"DBから削除しますか?\n{photo.FileName}",
                    "確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    await mainViewModel.PhotoGrid.DeleteFromDatabaseAsync(photo);
                }
            }
        }
    }

    private async void DeleteFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            var photo = GetPhotoFromContextMenu(menuItem);
            if (photo != null && DataContext is MainViewModel mainViewModel)
            {
                var result = System.Windows.MessageBox.Show(
                    $"ファイルをごみ箱へ移動しますか?\n{photo.FilePath}",
                    "ごみ箱へ移動",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    await mainViewModel.PhotoGrid.DeleteFileAsync(photo);
                }
            }
        }
    }

    private PhotoViewModel? GetPhotoFromContextMenu(System.Windows.Controls.MenuItem menuItem)
    {
        if (menuItem.DataContext is PhotoViewModel directPhoto) return directPhoto;
        // Navigate up the visual tree to find the ContextMenu
        var contextMenu = FindParent<System.Windows.Controls.ContextMenu>(menuItem);
        if (contextMenu?.DataContext is PhotoViewModel photo)
        {
            return photo;
        }
        return null;
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = LogicalTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T typedParent)
                return typedParent;
            parent = LogicalTreeHelper.GetParent(parent);
        }
        return null;
    }
}
