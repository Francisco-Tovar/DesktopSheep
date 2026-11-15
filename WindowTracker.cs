using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using Forms = System.Windows.Forms;

namespace KawaiiDesktopPets;

public readonly record struct TrackedWindow(IntPtr Hwnd, Rect Bounds, int ZOrder);

internal sealed class WindowTracker
{
    private readonly uint _currentPid = (uint)Environment.ProcessId;
    private readonly List<TrackedWindow> _windows = new();
    private long _lastRefreshTick;

    public IReadOnlyList<TrackedWindow> GetWindows(double dpiX, double dpiY)
    {
        long now = Environment.TickCount64;
        if (now - _lastRefreshTick < 45 && _windows.Count > 0)
            return _windows;

        _lastRefreshTick = now;
        _windows.Clear();

        int zOrder = 0;
        var classBuilder = new StringBuilder(128);

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
                return true;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == _currentPid)
                return true;

            int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0)
                return true;

            if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            classBuilder.Clear();
            NativeMethods.GetClassName(hwnd, classBuilder, classBuilder.Capacity);
            string className = classBuilder.ToString();
            if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
                return true;

            if (!TryGetWindowRect(hwnd, out NativeMethods.RECT rawRect))
                return true;

            double left = rawRect.Left * dpiX;
            double top = rawRect.Top * dpiY;
            double width = (rawRect.Right - rawRect.Left) * dpiX;
            double height = (rawRect.Bottom - rawRect.Top) * dpiY;

            if (width < 80 || height < 50)
                return true;

            var bounds = new Rect(left, top, width, height);

            // Skip maximized or full-desktop windows whose top edge sits at the very top of the screen
            var centerScreen = Forms.Screen.FromPoint(new System.Drawing.Point(
                (rawRect.Left + rawRect.Right) / 2,
                (rawRect.Top + rawRect.Bottom) / 2));
            var wa = centerScreen.WorkingArea;
            double waTop = wa.Top * dpiY;
            double waBottom = wa.Bottom * dpiY;
            double waWidth = wa.Width * dpiX;
            if (bounds.Top <= waTop + 12 && bounds.Bottom >= waBottom - 12 && bounds.Width >= waWidth - 24)
                return true;

            _windows.Add(new TrackedWindow(hwnd, bounds, zOrder++));
            return true;
        }, IntPtr.Zero);

        return _windows;
    }

    public TrackedWindow? FindWindow(IntPtr hwnd, double dpiX, double dpiY)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd) || !NativeMethods.IsWindowVisible(hwnd) || NativeMethods.IsIconic(hwnd))
            return null;

        var windows = GetWindows(dpiX, dpiY);
        foreach (var win in windows)
        {
            if (win.Hwnd == hwnd)
                return win;
        }
        return null;
    }

    public bool IsPointOccluded(double x, double y, int targetZOrder, IReadOnlyList<TrackedWindow> windows)
    {
        foreach (var win in windows)
        {
            if (win.ZOrder >= targetZOrder)
                break;

            if (win.Bounds.Contains(x, y))
                return true;
        }
        return false;
    }

    public TrackedWindow? FindLandingWindow(
        double sheepCenterX,
        double prevFeetY,
        double newFeetY,
        double workAreaTop,
        IReadOnlyList<TrackedWindow> windows)
    {
        foreach (var win in windows)
        {
            double surfaceY = win.Bounds.Top;
            if (surfaceY < workAreaTop + 18)
                continue;

            if (sheepCenterX < win.Bounds.Left + 14 || sheepCenterX > win.Bounds.Right - 14)
                continue;

            if (prevFeetY <= surfaceY + 8 && newFeetY >= surfaceY - 2)
            {
                if (!IsPointOccluded(sheepCenterX, surfaceY + 4, win.ZOrder, windows))
                    return win;
            }
        }
        return null;
    }

    public TrackedWindow? FindClimbableWindow(
        double sheepCenterX,
        double sheepFeetY,
        int facing,
        double workAreaTop,
        IReadOnlyList<TrackedWindow> windows,
        out bool climbLeftSide)
    {
        climbLeftSide = true;
        foreach (var win in windows)
        {
            if (win.Bounds.Top < workAreaTop + 20)
                continue;

            if (sheepFeetY < win.Bounds.Top + 24 || sheepFeetY > win.Bounds.Bottom + 10)
                continue;

            if (facing >= 0 && Math.Abs(sheepCenterX - win.Bounds.Left) < 22)
            {
                if (!IsPointOccluded(win.Bounds.Left + 4, sheepFeetY - 12, win.ZOrder, windows))
                {
                    climbLeftSide = true;
                    return win;
                }
            }
            else if (facing < 0 && Math.Abs(sheepCenterX - win.Bounds.Right) < 22)
            {
                if (!IsPointOccluded(win.Bounds.Right - 4, sheepFeetY - 12, win.ZOrder, windows))
                {
                    climbLeftSide = false;
                    return win;
                }
            }
        }
        return null;
    }

    private static bool TryGetWindowRect(IntPtr hwnd, out NativeMethods.RECT rect)
    {
        if (NativeMethods.DwmGetWindowAttribute(
                hwnd,
                NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                out rect,
                Marshal.SizeOf<NativeMethods.RECT>()) == 0)
        {
            return true;
        }

        return NativeMethods.GetWindowRect(hwnd, out rect);
    }
}
