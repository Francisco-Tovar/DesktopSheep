using System.Runtime.InteropServices;
using Avalonia;

namespace KawaiiDesktopPets;

public readonly record struct TrackedWindow(IntPtr Hwnd, Rect Bounds, int ZOrder);

internal static class MacNative
{
    private const string ObjCLib = "/usr/lib/libobjc.A.dylib";
    private const long NSApplicationActivationPolicyAccessory = 1;
    private const long NSNormalWindowLevel = 0;
    private const long NSStatusWindowLevel = 25;

    [DllImport(ObjCLib, EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass(string className);

    [DllImport(ObjCLib, EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string selectorName);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSend(IntPtr receiver, IntPtr selector);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendBool(IntPtr receiver, IntPtr selector, byte value);

    [DllImport(ObjCLib, EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendLong(IntPtr receiver, IntPtr selector, long value);

    public static void HideDockIcon()
    {
        if (!OperatingSystem.IsMacOS())
            return;

        try
        {
            IntPtr nsAppClass = ObjcGetClass("NSApplication");
            if (nsAppClass == IntPtr.Zero)
                return;

            IntPtr sharedApp = ObjcMsgSend(nsAppClass, SelRegisterName("sharedApplication"));
            if (sharedApp != IntPtr.Zero)
                ObjcMsgSendLong(sharedApp, SelRegisterName("setActivationPolicy:"), NSApplicationActivationPolicyAccessory);
        }
        catch
        {
            // Ignore Objective-C runtime issues
        }
    }

    public static void SetWindowClickThrough(IntPtr nsWindow, bool clickThrough)
    {
        if (!OperatingSystem.IsMacOS() || nsWindow == IntPtr.Zero)
            return;

        try
        {
            ObjcMsgSendBool(nsWindow, SelRegisterName("setIgnoresMouseEvents:"), clickThrough ? (byte)1 : (byte)0);
        }
        catch
        {
            // Ignore Objective-C runtime issues
        }
    }

    public static void SetWindowAlwaysOnTop(IntPtr nsWindow, bool alwaysOnTop)
    {
        if (!OperatingSystem.IsMacOS() || nsWindow == IntPtr.Zero)
            return;

        try
        {
            ObjcMsgSendLong(nsWindow, SelRegisterName("setLevel:"), alwaysOnTop ? NSStatusWindowLevel : NSNormalWindowLevel);
        }
        catch
        {
            // Ignore Objective-C runtime issues
        }
    }
}

internal sealed class MacWindowTracker
{
    private const string CoreGraphicsLib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundationLib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    private const uint kCGWindowListOptionOnScreenOnly = 1 << 0;
    private const uint kCGWindowListExcludeDesktopElements = 1 << 4;
    private const int kCFNumberIntType = 9;
    private const int kCFNumberDoubleType = 13;
    private const uint kCFStringEncodingUTF8 = 0x08000100;

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize
    {
        public double Width;
        public double Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect
    {
        public CGPoint Origin;
        public CGSize Size;
    }

    [DllImport(CoreGraphicsLib)]
    private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);

    [DllImport(CoreGraphicsLib)]
    private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dict, out CGRect rect);

    [DllImport(CoreFoundationLib)]
    private static extern nint CFArrayGetCount(IntPtr theArray);

    [DllImport(CoreFoundationLib)]
    private static extern IntPtr CFArrayGetValueAtIndex(IntPtr theArray, nint idx);

    [DllImport(CoreFoundationLib)]
    private static extern IntPtr CFDictionaryGetValue(IntPtr theDict, IntPtr key);

    [DllImport(CoreFoundationLib)]
    private static extern bool CFNumberGetValue(IntPtr number, int theType, out int valuePtr);

    [DllImport(CoreFoundationLib)]
    private static extern bool CFNumberGetValue(IntPtr number, int theType, out double valuePtr);

    [DllImport(CoreFoundationLib)]
    private static extern IntPtr CFStringCreateWithCString(IntPtr alloc, string cStr, uint encoding);

    [DllImport(CoreFoundationLib)]
    private static extern void CFRelease(IntPtr cf);

    private readonly int _currentPid = Environment.ProcessId;
    private readonly List<TrackedWindow> _windows = new();
    private long _lastRefreshTick;

    public IReadOnlyList<TrackedWindow> GetWindows(IReadOnlyList<Rect> screenWorkAreas)
    {
        long now = Environment.TickCount64;
        if (now - _lastRefreshTick < 60 && _windows.Count > 0)
            return _windows;

        _lastRefreshTick = now;
        _windows.Clear();

        if (!OperatingSystem.IsMacOS())
            return _windows;

        IntPtr array = IntPtr.Zero;
        IntPtr keyPid = IntPtr.Zero;
        IntPtr keyLayer = IntPtr.Zero;
        IntPtr keyNumber = IntPtr.Zero;
        IntPtr keyBounds = IntPtr.Zero;
        IntPtr keyAlpha = IntPtr.Zero;

        try
        {
            array = CGWindowListCopyWindowInfo(kCGWindowListOptionOnScreenOnly | kCGWindowListExcludeDesktopElements, 0);
            if (array == IntPtr.Zero)
                return _windows;

            keyPid = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowOwnerPID", kCFStringEncodingUTF8);
            keyLayer = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowLayer", kCFStringEncodingUTF8);
            keyNumber = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowNumber", kCFStringEncodingUTF8);
            keyBounds = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowBounds", kCFStringEncodingUTF8);
            keyAlpha = CFStringCreateWithCString(IntPtr.Zero, "kCGWindowAlpha", kCFStringEncodingUTF8);

            nint count = CFArrayGetCount(array);
            int zOrder = 0;

            for (nint i = 0; i < count; i++)
            {
                IntPtr dict = CFArrayGetValueAtIndex(array, i);
                if (dict == IntPtr.Zero)
                    continue;

                if (!TryGetInt(dict, keyLayer, out int layer) || layer != 0)
                    continue;

                if (!TryGetInt(dict, keyPid, out int pid) || pid == _currentPid)
                    continue;

                if (TryGetDouble(dict, keyAlpha, out double alpha) && alpha <= 0.05)
                    continue;

                if (!TryGetInt(dict, keyNumber, out int winNumber) || winNumber <= 0)
                    continue;

                IntPtr boundsDict = CFDictionaryGetValue(dict, keyBounds);
                if (boundsDict == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(boundsDict, out CGRect cgRect))
                    continue;

                if (cgRect.Size.Width < 80 || cgRect.Size.Height < 50)
                    continue;

                var bounds = new Rect(cgRect.Origin.X, cgRect.Origin.Y, cgRect.Size.Width, cgRect.Size.Height);

                // Skip fullscreen/maximized windows whose top sits at the very top of the work area
                bool isFullScreen = false;
                double cx = bounds.X + bounds.Width / 2.0;
                double cy = bounds.Y + bounds.Height / 2.0;
                foreach (var wa in screenWorkAreas)
                {
                    if (wa.Contains(new Point(cx, cy))
                        && bounds.Top <= wa.Top + 12
                        && bounds.Bottom >= wa.Bottom - 12
                        && bounds.Width >= wa.Width - 24)
                    {
                        isFullScreen = true;
                        break;
                    }
                }
                if (isFullScreen)
                    continue;

                _windows.Add(new TrackedWindow(new IntPtr(winNumber), bounds, zOrder++));
            }
        }
        catch
        {
            _windows.Clear();
        }
        finally
        {
            if (keyPid != IntPtr.Zero) CFRelease(keyPid);
            if (keyLayer != IntPtr.Zero) CFRelease(keyLayer);
            if (keyNumber != IntPtr.Zero) CFRelease(keyNumber);
            if (keyBounds != IntPtr.Zero) CFRelease(keyBounds);
            if (keyAlpha != IntPtr.Zero) CFRelease(keyAlpha);
            if (array != IntPtr.Zero) CFRelease(array);
        }

        return _windows;
    }

    public TrackedWindow? FindWindow(IntPtr hwnd, IReadOnlyList<Rect> screenWorkAreas)
    {
        if (hwnd == IntPtr.Zero)
            return null;

        var windows = GetWindows(screenWorkAreas);
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

            if (win.Bounds.Contains(new Point(x, y)))
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

    private static bool TryGetInt(IntPtr dict, IntPtr key, out int value)
    {
        value = 0;
        IntPtr ptr = CFDictionaryGetValue(dict, key);
        return ptr != IntPtr.Zero && CFNumberGetValue(ptr, kCFNumberIntType, out value);
    }

    private static bool TryGetDouble(IntPtr dict, IntPtr key, out double value)
    {
        value = 0;
        IntPtr ptr = CFDictionaryGetValue(dict, key);
        return ptr != IntPtr.Zero && CFNumberGetValue(ptr, kCFNumberDoubleType, out value);
    }
}
