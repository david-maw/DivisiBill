// Ignore Spelling: lprc lpfn

using System.Runtime.InteropServices;

namespace DivisiBill.Services;

/// <summary>
/// Validates and corrects window positions for multi-screen Windows
/// systems, ensuring saved window positions are visible.
/// </summary>
public static class WindowPositionValidator
{
    /// <summary>
    /// Windows P/Invoke declarations for enumerating display monitors.
    /// </summary>
    private static class NativeDisplayMethods
    {
        private const uint MONITOR_DEFAULTTONULL = 0;
        private const int MDT_EFFECTIVE_DPI = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(
            IntPtr hdc, IntPtr lprcClip,
            EnumMonitorsDelegate lpfnEnum, IntPtr dwData);

        public delegate bool EnumMonitorsDelegate(
            IntPtr hMonitor, IntPtr hdcMonitor, ref Rect lprcMonitor,
            IntPtr dwData);

        [DllImport("shcore.dll")]
        public static extern int GetDpiForMonitor(
            IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    }

    /// <summary>
    /// Gets bounds for all available screens on the system.
    /// </summary>
    /// <returns>
    /// List of screen work area rectangles in logical MAUI units. On Windows
    /// enumerates all connected monitors using native P/Invoke API and applies
    /// per-monitor DPI scaling. On non-Windows platforms returns MAUI display
    /// info. If all enumeration fails, returns a default 1920x1080 screen.
    /// </returns>
    public static List<Rect> GetAllScreenBounds()
    {
        var screens = new List<Rect>();

#if WINDOWS
        // Try to enumerate all display monitors using Windows API
        try
        {
            var monitorData = new List<(IntPtr Handle, NativeDisplayMethods.Rect Bounds)>();
            bool EnumCallback(IntPtr hMonitor, IntPtr hdcMonitor,
                ref NativeDisplayMethods.Rect lprcMonitor, IntPtr dwData)
            {
                monitorData.Add((hMonitor, lprcMonitor));
                return true;
            }

            if (NativeDisplayMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumCallback, IntPtr.Zero))
            {
                // Successfully enumerated monitors
                foreach (var (monitorHandle, nativeRect) in monitorData)
                {
                    try
                    {
                        // Get the DPI for this specific monitor
                        int result = NativeDisplayMethods.GetDpiForMonitor(
                            monitorHandle, 0, out uint dpiX, out uint dpiY);

                        if (result != 0)
                        {
                            // GetDpiForMonitor failed, use default 96 DPI
                            dpiX = 96;
                            dpiY = 96;
                        }

                        // Convert from physical pixels to logical units
                        // MAUI uses 96 DPI as the base unit
                        var scale = dpiX / 96.0;
                        var logicalX = (int)(nativeRect.Left / scale);
                        var logicalY = (int)(nativeRect.Top / scale);
                        var logicalWidth = (int)((nativeRect.Right - nativeRect.Left) / scale);
                        var logicalHeight = (int)((nativeRect.Bottom - nativeRect.Top) / scale);

                        screens.Add(new Rect(logicalX, logicalY,
                            logicalWidth, logicalHeight));
                    }
                    catch
                    {
                        // If DPI retrieval fails for this monitor, use
                        // physical pixels (fallback)
                        var width = nativeRect.Right - nativeRect.Left;
                        var height = nativeRect.Bottom - nativeRect.Top;
                        screens.Add(new Rect(nativeRect.Left, nativeRect.Top,
                            width, height));
                    }
                }

                if (screens.Count > 0)
                    return screens;
            }

            var primaryArea = Microsoft.UI.Windowing.DisplayArea.Primary;
            if (primaryArea is not null)
            {
                var workArea = primaryArea.WorkArea;
                screens.Add(new Rect(workArea.X, workArea.Y,
                    workArea.Width, workArea.Height));
                return screens;
            }
        }
        catch
        {
            // If P/Invoke faults, try DisplayArea.Primary
            try
            {
                var primaryArea =
                    Microsoft.UI.Windowing.DisplayArea.Primary;
                if (primaryArea is not null)
                {
                    var workArea = primaryArea.WorkArea;
                    screens.Add(new Rect(workArea.X, workArea.Y,
                        workArea.Width, workArea.Height));
                    return screens;
                }
            }
            catch
            {
                // Continue to default fallback
            }
        }
#else
        // Non-Windows platforms: use MAUI display info
        try
        {
            var displayInfo = DeviceDisplay.Current.MainDisplayInfo;
            screens.Add(new Rect(0, 0,
                displayInfo.Width / displayInfo.Density,
                displayInfo.Height / displayInfo.Density));
            return screens;
        }
        catch
        {
            // Continue to default fallback
        }
#endif
        // Last resort default
        screens.Add(new Rect(0, 0, 1920, 1080));
        return screens;
    }

    /// <summary>
    /// Checks if a window rectangle is completely within any available
    /// screen bounds.
    /// </summary>
    /// <param name="windowRect">The window rectangle to validate</param>
    /// <returns>
    /// True if the window is completely visible on at least one screen
    /// </returns>
    public static bool IsPositionValid(Rect windowRect)
    {
        if (windowRect.Width <= 0 || windowRect.Height <= 0)
            return false;

        var screens = GetAllScreenBounds();
        return screens.Any(screen => IsCompletelyOnScreen(windowRect, screen));
    }

    /// <summary>
    /// Ensures a window position is visible, adjusting coordinates if
    /// needed to fit within screen bounds.
    /// </summary>
    /// <param name="initialPosition">The desired window rectangle</param>
    /// <param name="minWidth">Minimum acceptable window width</param>
    /// <param name="minHeight">Minimum acceptable window height</param>
    /// <returns>
    /// A corrected rectangle that fits within available screens
    /// </returns>
    public static Rect EnsureVisiblePosition(Rect initialPosition, double minWidth = 100, double minHeight = 100)
    {
        var screens = GetAllScreenBounds();

        // Validate dimensions
        var width = Math.Max(initialPosition.Width, minWidth);
        var height = Math.Max(initialPosition.Height, minHeight);

        // Check if already valid
        var testRect = new Rect(initialPosition.X, initialPosition.Y,
            width, height);
        if (screens.Any(screen => IsCompletelyOnScreen(testRect, screen)))
            return testRect;

        // Find closest screen to the window's center
        var centerX = initialPosition.X + (initialPosition.Width / 2.0);
        var centerY = initialPosition.Y + (initialPosition.Height / 2.0);
        var closestScreen = screens.OrderBy(s =>
            GetDistanceToPoint(s, centerX, centerY)).First();

        // Clamp position to the closest screen
        var clampedX = Math.Max(closestScreen.X,
            Math.Min(initialPosition.X,
                closestScreen.Right - width));
        var clampedY = Math.Max(closestScreen.Y,
            Math.Min(initialPosition.Y,
                closestScreen.Bottom - height));

        return new Rect(clampedX, clampedY, width, height);
    }

    /// <summary>
    /// Checks if a rectangle is completely within a screen's bounds.
    /// </summary>
    private static bool IsCompletelyOnScreen(Rect windowRect, Rect screenBounds)
    {
        return windowRect.Left >= screenBounds.X &&
               windowRect.Top >= screenBounds.Y &&
               windowRect.Right <= screenBounds.Right &&
               windowRect.Bottom <= screenBounds.Bottom;
    }

    /// <summary>
    /// Computes the distance from a point to the nearest edge of a
    /// rectangle.
    /// </summary>
    private static double GetDistanceToPoint(Rect rect, double x, double y)
    {
        var dx = Math.Max(Math.Max(rect.X - x, 0), x - rect.Right);
        var dy = Math.Max(Math.Max(rect.Y - y, 0), y - rect.Bottom);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
