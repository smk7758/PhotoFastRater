using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PhotoFastRater.UI.ViewModels;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Image = System.Windows.Controls.Image;

namespace PhotoFastRater.UI.Views;

/// <summary>Routes comparison input while leaving durable rating and image work in the workspace services.</summary>
public partial class CompareWindow : Window
{
    private bool _synchronizingScroll;
    private ScrollViewer? _dragViewer;
    private Point _dragStart;
    private Point _dragOffset;

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
        try { await workspace.SetRatingAsync(pane, rating); }
        catch (Exception exception)
        {
            // An async UI event must report a failed commit without advancing or terminating the application.
            pane.ErrorMessage = $"評価を保存できませんでした。再試行してください: {exception.Message}";
        }
    }

    private void PaneScrollViewer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ScrollViewer viewer || e.OriginalSource is not Image) return;
        _dragViewer = viewer;
        _dragStart = e.GetPosition(viewer);
        _dragOffset = new Point(viewer.HorizontalOffset, viewer.VerticalOffset);
        viewer.CaptureMouse();
        e.Handled = true;
    }

    private void PaneScrollViewer_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragViewer is not { } viewer || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(viewer);
        viewer.ScrollToHorizontalOffset(_dragOffset.X + _dragStart.X - position.X);
        viewer.ScrollToVerticalOffset(_dragOffset.Y + _dragStart.Y - position.Y);
        e.Handled = true;
    }

    private void PaneScrollViewer_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        _dragViewer?.ReleaseMouseCapture();
        _dragViewer = null;
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
