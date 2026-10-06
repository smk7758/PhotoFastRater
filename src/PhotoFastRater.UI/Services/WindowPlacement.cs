using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace PhotoFastRater.UI.Services;

/// <summary>Fits a newly opened window into its actual monitor work area in physical pixels.
/// Native coordinates avoid treating a 150% monitor's logical dimensions as a 100% monitor's dimensions.</summary>
internal static class WindowPlacement
{
    /// <summary>Attaches a one-time initial fit; it does not fight later user resizing or monitor movement.</summary>
    internal static void FitOnFirstLoad(Window window)
    {
        RoutedEventHandler? loaded = null;
        loaded = (_, _) =>
        {
            window.Loaded -= loaded;
            var handle = new WindowInteropHelper(window).Handle;
            if (!GetWindowRect(handle, out var bounds)) return;
            var work = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            var width = Math.Min(bounds.Right - bounds.Left, work.Width);
            var height = Math.Min(bounds.Bottom - bounds.Top, work.Height);
            var left = Math.Clamp(bounds.Left, work.Left, work.Right - width);
            var top = Math.Clamp(bounds.Top, work.Top, work.Bottom - height);
            if (left == bounds.Left && top == bounds.Top && width == bounds.Right - bounds.Left && height == bounds.Bottom - bounds.Top) return;
            _ = SetWindowPos(handle, IntPtr.Zero, left, top, width, height, 0x0014); // Keep activation and Z-order unchanged.
        };
        window.Loaded += loaded;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rectangle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr handle, IntPtr after, int left, int top, int width, int height, uint flags);
}
