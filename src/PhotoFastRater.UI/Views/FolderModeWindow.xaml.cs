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
}
