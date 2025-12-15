using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class MainWindow : Window
{
    private readonly IServiceProvider _serviceProvider;

    public MainWindow(MainViewModel viewModel, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        DataContext = viewModel;
        _serviceProvider = serviceProvider;
    }

    private void OpenFolderMode_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        var folderWindow = _serviceProvider.GetRequiredService<FolderModeWindow>();
        folderWindow.Show();
    }

    private void RatingMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem &&
            menuItem.Tag is string ratingStr &&
            int.TryParse(ratingStr, out var rating))
        {
            var photo = GetPhotoFromContextMenu(menuItem);
            if (photo != null && DataContext is MainViewModel mainViewModel)
            {
                mainViewModel.PhotoGrid.SetRating(photo, rating);
            }
        }
    }

    private void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            var photo = GetPhotoFromContextMenu(menuItem);
            if (photo != null && DataContext is MainViewModel mainViewModel)
            {
                mainViewModel.PhotoGrid.ToggleFavorite(photo);
            }
        }
    }

    private void ToggleReject_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            var photo = GetPhotoFromContextMenu(menuItem);
            if (photo != null && DataContext is MainViewModel mainViewModel)
            {
                mainViewModel.PhotoGrid.ToggleReject(photo);
            }
        }
    }

    private void ExportToSocialMedia_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            var photo = GetPhotoFromContextMenu(menuItem);
            if (photo != null && DataContext is MainViewModel mainViewModel)
            {
                mainViewModel.PhotoGrid.ExportToSocialMedia(photo);
            }
        }
    }

    private void DeleteFromDatabase_Click(object sender, RoutedEventArgs e)
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
                    mainViewModel.PhotoGrid.DeleteFromDatabase(photo);
                }
            }
        }
    }

    private void DeleteFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem menuItem)
        {
            var photo = GetPhotoFromContextMenu(menuItem);
            if (photo != null && DataContext is MainViewModel mainViewModel)
            {
                var result = System.Windows.MessageBox.Show(
                    $"ファイルを完全に削除しますか?\n{photo.FilePath}\n\nこの操作は取り消せません！",
                    "警告",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error);

                if (result == MessageBoxResult.Yes)
                {
                    mainViewModel.PhotoGrid.DeleteFile(photo);
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
