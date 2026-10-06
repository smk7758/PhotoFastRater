using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PhotoFastRater.UI.Services;

/// <summary>Prevents global photo gestures from stealing editing and standard control navigation.</summary>
public static class KeyboardInputPolicy
{
    public static bool IsControlInput(DependencyObject? target)
    {
        while (target is not null)
        {
            if (target is System.Windows.Controls.Button { DataContext: PhotoFastRater.UI.ViewModels.PhotoViewModel or PhotoFastRater.UI.ViewModels.FolderSessionPhotoViewModel }) return false;
            if (target is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox or System.Windows.Controls.ComboBox or DatePicker or Slider or GridSplitter or System.Windows.Controls.Primitives.ScrollBar or System.Windows.Controls.Primitives.ButtonBase or System.Windows.Controls.DataGrid)
                return true;
            target = target is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(target)
                : LogicalTreeHelper.GetParent(target);
        }
        return false;
    }
}
