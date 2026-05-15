using System.IO;
using System.Windows;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class FolderModeSettingsWindow : Window
{
    public FolderModeSettingsWindow(FolderModeSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is FolderModeSettingsViewModel vm)
            vm.Save();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OpenSettingsFile_Click(object sender, RoutedEventArgs e)
    {
        var path = FolderModeSettingsViewModel.SettingsPath;
        if (!File.Exists(path))
            (DataContext as FolderModeSettingsViewModel)?.Save();
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
    }
}
