using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Screen = System.Windows.Forms.Screen;

namespace MiniLocalChat.Infrastructure;

public static class WindowSizing
{
    /// <summary>
    /// Caps a dialog's height at <paramref name="preferredMax"/> and at the monitor it opens on (its owner's,
    /// or the main one), so its bottom is never off screen; its content scrolls instead.
    /// </summary>
    public static void FitHeight(Window window, Window? owner, double preferredMax)
    {
        var available = SystemParameters.WorkArea.Height;
        if (owner is not null && new WindowInteropHelper(owner).Handle is var handle && handle != IntPtr.Zero)
        {
            available = Screen.FromHandle(handle).WorkingArea.Height / VisualTreeHelper.GetDpi(owner).DpiScaleY;
        }

        window.MaxHeight = Math.Max(240, Math.Min(preferredMax, available - 32));
    }

    /// <summary>
    /// For a dialog's scrolling body (its PreviewMouseWheel): lists inside it, like the color swatches,
    /// would otherwise swallow the mouse wheel. An open drop-down still scrolls itself.
    /// </summary>
    public static void ScrollOnWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is ScrollViewer scroller && Mouse.Captured is not ComboBox)
        {
            scroller.ScrollToVerticalOffset(scroller.VerticalOffset - e.Delta * 0.4);
            e.Handled = true;
        }
    }
}
