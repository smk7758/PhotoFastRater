using System.Windows;
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
}
