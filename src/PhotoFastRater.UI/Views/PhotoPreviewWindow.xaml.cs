using System.Windows;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class PhotoPreviewWindow : Window
{
    public PhotoPreviewWindow(FolderModeViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
