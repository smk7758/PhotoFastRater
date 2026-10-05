using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhotoFastRater.UI.ViewModels;

namespace PhotoFastRater.UI.Views;

public partial class CompareWindow : Window
{
    private bool _synchronizingScroll;

    public CompareWindow(CompareWorkspaceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void PaneScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control ||
            sender is not ScrollViewer { Tag: ComparePaneViewModel pane } ||
            DataContext is not CompareWorkspaceViewModel workspace)
        {
            return;
        }

        workspace.AdjustZoom(pane, e.Delta > 0 ? 0.25 : -0.25);
        e.Handled = true;
    }

    private void PaneScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_synchronizingScroll ||
            DataContext is not CompareWorkspaceViewModel { SynchronizeViewport: true } ||
            sender is not ScrollViewer source ||
            (e.HorizontalChange == 0 && e.VerticalChange == 0))
        {
            return;
        }

        _synchronizingScroll = true;
        try
        {
            var horizontalRatio = source.ScrollableWidth <= 0 ? 0 : source.HorizontalOffset / source.ScrollableWidth;
            var verticalRatio = source.ScrollableHeight <= 0 ? 0 : source.VerticalOffset / source.ScrollableHeight;
            foreach (var target in FindVisualChildren<ScrollViewer>(this).Where(target => !ReferenceEquals(target, source)))
            {
                target.ScrollToHorizontalOffset(horizontalRatio * target.ScrollableWidth);
                target.ScrollToVerticalOffset(verticalRatio * target.ScrollableHeight);
            }
        }
        finally
        {
            _synchronizingScroll = false;
        }
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
    }

    private async void Rating_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string tag, DataContext: ComparePaneViewModel pane } ||
            !int.TryParse(tag, out var rating) ||
            DataContext is not CompareWorkspaceViewModel workspace)
        {
            return;
        }
        await workspace.SetRatingAsync(pane, rating);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }
}
