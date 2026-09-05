using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhotoFastRater.UI.Services;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class MainWindow : Window
{
    private readonly WindowManager _windowManager;
    private bool _isLoadingNextPage;

    public MainWindow(MainViewModel viewModel, WindowManager windowManager)
    {
        InitializeComponent();
        DataContext = viewModel;
        _windowManager = windowManager;

        PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                int delta = e.Delta > 0 ? 20 : -20;
                viewModel.PhotoGrid.ThumbnailSize = Math.Clamp(viewModel.PhotoGrid.ThumbnailSize + delta, 80, 600);
                e.Handled = true;
            }
        };

        Loaded += (_, _) =>
        {
            if (PhotoGridScrollViewer != null)
            {
                PhotoGridScrollViewer.SizeChanged += (s, e) =>
                    viewModel.PhotoGrid.NotifyGridWidth(e.NewSize.Width);
                viewModel.PhotoGrid.NotifyGridWidth(PhotoGridScrollViewer.ActualWidth);
            }
        };
    }

    private void OpenFolderMode_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        _windowManager.ShowFolderWindow();
    }

    private async void PhotoGridScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
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
