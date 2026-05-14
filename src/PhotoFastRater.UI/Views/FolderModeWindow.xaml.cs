using System.IO;
using System.Windows;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class FolderModeWindow : Window
{
    public FolderModeWindow(FolderModeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public async void LoadFolder(string folderPath)
    {
        if (DataContext is FolderModeViewModel viewModel)
        {
            await viewModel.LoadFolderAsync(folderPath);
        }
    }

    public void OpenFolderDialog()
    {
        if (DataContext is FolderModeViewModel viewModel)
        {
            viewModel.OpenFolderCommand.Execute(null);
        }
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
        var folder = paths.FirstOrDefault(Directory.Exists);
        if (folder != null && DataContext is FolderModeViewModel viewModel)
            await viewModel.LoadFolderAsync(folder);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (DataContext is FolderModeViewModel viewModel && PhotoGrid != null)
            viewModel.NotifyGridWidth(PhotoGrid.ActualWidth);
    }
}
