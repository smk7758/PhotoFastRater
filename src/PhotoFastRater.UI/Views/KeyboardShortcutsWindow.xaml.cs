using System.Windows;
using System.Windows.Input;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class KeyboardShortcutsWindow : Window
{
    public KeyboardShortcutsWindow(KeyboardShortcutsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (DataContext is not KeyboardShortcutsViewModel vm || !vm.IsCapturing) return;

        if (e.Key is Key.LeftShift or Key.RightShift
                  or Key.LeftCtrl or Key.RightCtrl
                  or Key.LeftAlt or Key.RightAlt
                  or Key.System)
            return;

        vm.CaptureKey(e.Key, Keyboard.Modifiers);
        e.Handled = true;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is KeyboardShortcutsViewModel vm)
            vm.Save();
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
