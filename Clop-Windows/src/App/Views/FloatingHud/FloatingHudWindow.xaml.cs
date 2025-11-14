using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClopWindows.App.Views.FloatingHud;

public partial class FloatingHudWindow : Window
{
    private const double MarginFromScreen = 24d;

    public FloatingHudWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyTransparentBackground();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MoveToTopRight();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        MoveToTopRight();
    }

    public void MoveToTopRight()
    {
        var workArea = SystemParameters.WorkArea;
        var width = ActualWidth > 0 ? ActualWidth : Width;
        var height = ActualHeight > 0 ? ActualHeight : Height;

        Left = workArea.Right - width - MarginFromScreen;
        Top = workArea.Top + MarginFromScreen;
    }

    public void BringToFront()
    {
        if (!IsVisible)
        {
            Show();
        }

        Topmost = false;
        Topmost = true;
    }

    private void ApplyTransparentBackground()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        var hwnd = source.Handle;
        var attrValue = new int[] { 2 }; // DWMWA_FORCE_ICONIC_REPRESENTATION off, ensures transparency works
        _ = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, attrValue, sizeof(int));
    }

    private static class NativeMethods
    {
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, int[] attrValue, int attrSize);
    }
}
