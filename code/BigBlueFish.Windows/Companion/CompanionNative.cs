using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace BigBlueFish.Windows;
internal static class CompanionNative
{
    [ComImport, Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(IntPtr window, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(IntPtr window, out Guid desktopId);
        [PreserveSig] int MoveWindowToDesktop(IntPtr window, ref Guid desktopId);
    }
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IComServiceProvider
    {
        void QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }
    [ComImport, Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationViewCollection
    {
        IntPtr GetViews();
        IntPtr GetViewsByZOrder();
        IntPtr GetViewsByAppUserModelId(string id);
        IApplicationView GetViewForHwnd(IntPtr hwnd);
    }
    [ComImport, Guid("372E1D3B-38D3-42E4-A15B-8AB2B178F513"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationView { }
    [ComImport, Guid("4CE81583-1E4C-4632-A621-07A53543148F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopPinnedApps
    {
        [return: MarshalAs(UnmanagedType.Bool)] bool IsAppIdPinned([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void PinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        void UnpinAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [return: MarshalAs(UnmanagedType.Bool)] bool IsViewPinned(IApplicationView view);
        void PinView(IApplicationView view);
        void UnpinView(IApplicationView view);
    }
    private sealed record PinnedDesktopServices(IApplicationViewCollection Views, IVirtualDesktopPinnedApps PinnedApps);
    private static readonly Lazy<IVirtualDesktopManager?> virtualDesktop = new(() =>
    {
        try { return (IVirtualDesktopManager?)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"), true)!); }
        catch { return null; }
    });
    private static readonly Lazy<PinnedDesktopServices?> pinnedDesktop = new(CreatePinnedDesktopServices);
    internal static bool? TestPowerOverride;
    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr h);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr h);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr h, int attribute, out int value, int size);
    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr h, out uint p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")]
    internal static extern bool GetLastInputInfo(ref Input i);
    [DllImport("kernel32.dll")]
    internal static extern bool GetSystemPowerStatus(out Power p);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetStyle(IntPtr h, int n);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern IntPtr SetStyle(IntPtr h, int n, IntPtr v);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr h, int n);
    [DllImport("user32.dll")]
    internal static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Input
    {
        public uint Size, Time;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Power
    {
        public byte AC, Battery, Percent, Reserved;
        public uint Life, FullLife;
    }

    internal static bool OnAC => TestPowerOverride ?? (GetSystemPowerStatus(out var p) && p.AC == 1);

    internal static double IdleSeconds
    {
        get
        {
            var i = new Input
            {
                Size = (uint)Marshal.SizeOf<Input>()
            };
            return GetLastInputInfo(ref i) ? unchecked((uint)Environment.TickCount - i.Time) / 1000.0 : 0;
        }
    }

    internal static string ForegroundContext(IntPtr preferredHandle = default)
    {
        var h = preferredHandle == IntPtr.Zero ? GetForegroundWindow() : preferredHandle;
        if (h == IntPtr.Zero)
            return "前台窗口：Windows 桌面\n当前可见活动线索：桌面没有活动窗口标题。";
        GetWindowThreadProcessId(h, out var id);
        if (id == Environment.ProcessId)
            return "用户正在查看桌宠";
        var s = new StringBuilder(256);
        GetWindowText(h, s, 256);
        var className = new StringBuilder(128);
        GetClassName(h, className, className.Capacity);
        string name = "未知应用";
        try
        {
            name = Process.GetProcessById((int)id).ProcessName;
        }
        catch
        {
        }

        var snapshot = ReadAccessibilitySnapshot(h);
        return BuildForegroundContext(name, s.ToString(), className.ToString(), snapshot.Name, snapshot.ControlType, snapshot.Descendants);
    }

    internal static CompanionWindowImage? ForegroundWindowImage(IntPtr preferredHandle = default)
    {
        var handle = preferredHandle == IntPtr.Zero ? GetForegroundWindow() : preferredHandle;
        if (handle == IntPtr.Zero)
            return null;
        GetWindowThreadProcessId(handle, out var id);
        return id == Environment.ProcessId ? null : CompanionScreenCapture.Capture(handle);
    }

    // Supplemental identity only; OCR screen content is the primary evidence in OCR mode.
    // Avoid the accessibility traversal, which can be noisy or slow in custom-rendered apps.
    internal static string ForegroundIdentity(IntPtr handle)
    {
        GetWindowThreadProcessId(handle, out var id);
        var title = new StringBuilder(512);
        GetWindowText(handle, title, title.Capacity);
        var category = new StringBuilder(128);
        GetClassName(handle, category, category.Capacity);
        if (IsDesktopWindow(handle))
            return "当前前台画面：Windows 桌面，壁纸与快捷方式图标。";
        string name = "未知应用";
        try { using var process = Process.GetProcessById((int)id); name = process.ProcessName; } catch { }
        return "前台辅助信息（以屏幕文字为主要依据）：\n进程：" + name + "\n标题：" + title;
    }

    internal static bool IsDesktopWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        if (handle == GetShellWindow()) return true;
        var category = new StringBuilder(128);
        GetClassName(handle, category, category.Capacity);
        if (category.ToString() is not ("Progman" or "WorkerW")) return false;
        GetWindowThreadProcessId(handle, out var id);
        try { using var process = Process.GetProcessById((int)id); return process.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    internal static bool IsVisibleCaptureTarget(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !IsWindow(handle) || !IsWindowVisible(handle) || IsIconic(handle))
            return false;
        return DwmGetWindowAttribute(handle, 14 /* DWMWA_CLOAKED */, out var cloaked, sizeof(int)) != 0 || cloaked == 0;
    }

    internal static bool IsOnCurrentVirtualDesktop(IntPtr handle)
    {
        if (handle == IntPtr.Zero || virtualDesktop.Value is not { } manager) return true;
        try { return manager.IsWindowOnCurrentVirtualDesktop(handle, out var current) != 0 || current; }
        catch { return true; }
    }

    internal static bool MoveToCurrentVirtualDesktop(IntPtr handle)
    {
        if (handle == IntPtr.Zero || virtualDesktop.Value is not { } manager) return false;
        try
        {
            var foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || manager.GetWindowDesktopId(foreground, out var desktop) != 0) return false;
            return manager.MoveWindowToDesktop(handle, ref desktop) == 0;
        }
        catch { return false; }
    }

    private static PinnedDesktopServices? CreatePinnedDesktopServices()
    {
        try
        {
            var shellType = Type.GetTypeFromCLSID(new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239"), true)!;
            if (Activator.CreateInstance(shellType) is not IComServiceProvider shell) return null;
            Guid pinnedService = new("B5A399E7-1C87-46B8-88E9-FC5747B171BD");
            Guid pinnedIid = typeof(IVirtualDesktopPinnedApps).GUID;
            shell.QueryService(ref pinnedService, ref pinnedIid, out var pinnedObject);
            Guid viewsService = typeof(IApplicationViewCollection).GUID;
            shell.QueryService(ref viewsService, ref viewsService, out var viewsObject);
            return new((IApplicationViewCollection)viewsObject, (IVirtualDesktopPinnedApps)pinnedObject);
        }
        catch (COMException) { return null; }
        catch (InvalidCastException) { return null; }
        catch (ArgumentException) { return null; }
    }

    internal static bool PinWindowToAllVirtualDesktops(IntPtr handle)
    {
        if (handle == IntPtr.Zero) return false;
        try
        {
            var services = pinnedDesktop.Value;
            if (services == null) return false;
            var view = services.Views.GetViewForHwnd(handle);
            if (view == null) return false;
            if (services.PinnedApps.IsViewPinned(view)) return true;
            services.PinnedApps.PinView(view);
            return services.PinnedApps.IsViewPinned(view);
        }
        catch (COMException) { return false; }
        catch (InvalidCastException) { return false; }
        catch (ArgumentException) { return false; }
    }

    internal static bool IsExternalForegroundCandidate(IntPtr handle)
    {
        if (!IsVisibleCaptureTarget(handle))
            return false;
        GetWindowThreadProcessId(handle, out var id);
        if (id == Environment.ProcessId)
            return false;
        var className = new StringBuilder(128);
        GetClassName(handle, className, className.Capacity);
        return className.ToString() is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "NotifyIconOverflowWindow" or "#32768");
    }

    // The pet/menu may own focus at the click. A remembered window is usable only
    // while it still belongs to the visible desktop; PrintWindow can render a
    // minimized application even though it is no longer the user's foreground.
    internal static IntPtr ResolveSpeechForeground(IntPtr current, IntPtr remembered)
    {
        if (current == IntPtr.Zero)
            return IntPtr.Zero;
        if (IsExternalForegroundCandidate(current))
            return current;
        if (IsExternalForegroundCandidate(remembered))
            return remembered;
        var desktop = GetShellWindow();
        return IsExternalForegroundCandidate(desktop) ? desktop : IntPtr.Zero;
    }

    internal static string BuildForegroundContext(string processName, string title, string className, string accessibleName, string controlType, IReadOnlyList<string> descendants)
    {
        var lines = new List<string>
        {
            "前台应用：" + Limit(processName, 120),
            "窗口标题：" + (Limit(title, 220) is { Length: > 0 } safeTitle ? safeTitle : "（没有窗口标题）"),
            "窗口类别：" + (Limit(className, 120) is { Length: > 0 } safeClass ? safeClass : "（未知）"),
            "无障碍名称：" + (Limit(accessibleName, 180) is { Length: > 0 } safeName ? safeName : "（没有提供）"),
            "无障碍控件类型：" + (Limit(controlType, 120) is { Length: > 0 } safeType ? safeType : "（未知）")
        };
        var clues = descendants
            .Select(x => Normalize(x, 120))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
        lines.Add(clues.Length == 0
            ? "可见界面线索：没有读取到子控件名称。"
            : "可见界面线索：\n" + string.Join("\n", clues.Select(x => "- " + x)));
        return string.Join("\n", lines);
    }

    private static (string Name, string ControlType, IReadOnlyList<string> Descendants) ReadAccessibilitySnapshot(IntPtr handle)
    {
        try
        {
            var root = AutomationElement.FromHandle(handle);
            var current = root.Current;
            var descendants = new List<string>();
            var walker = TreeWalker.ControlViewWalker;
            CollectAccessibilityChildren(walker, root, descendants, 0, 3, 32);
            return (current.Name ?? "", current.ControlType?.ProgrammaticName ?? "", descendants);
        }
        catch
        {
            return ("", "", Array.Empty<string>());
        }
    }

    private static void CollectAccessibilityChildren(TreeWalker walker, AutomationElement parent, List<string> values, int depth, int maxDepth, int maxItems)
    {
        if (depth >= maxDepth || values.Count >= maxItems)
            return;
        AutomationElement? child = null;
        try { child = walker.GetFirstChild(parent); } catch { return; }
        while (child != null && values.Count < maxItems)
        {
            try
            {
                var current = child.Current;
                var name = Normalize(current.Name, 120);
                var type = current.ControlType?.ProgrammaticName ?? "";
                if (name.Length > 0)
                    values.Add(type.Length > 0 ? type + "：" + name : name);
                CollectAccessibilityChildren(walker, child, values, depth + 1, maxDepth, maxItems);
                child = walker.GetNextSibling(child);
            }
            catch
            {
                break;
            }
        }
    }

    private static string Normalize(string value, int maxLength)
    {
        var normalized = string.Join(" ", (value ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "…";
    }

    private static string Limit(string value, int maxLength) => Normalize(value, maxLength);

    internal static bool IsFullscreen()
    {
        var h = GetForegroundWindow();
        GetWindowThreadProcessId(h, out var id);
        if (id == Environment.ProcessId || !GetWindowRect(h, out var r))
            return false;
        try
        {
            var n = Process.GetProcessById((int)id).ProcessName;
            if (n == "explorer")
                return false;
        }
        catch
        {
        }

        // A maximized window covers the monitor too, but still has a caption and
        // resize frame. Treat only borderless windows that closely match the
        // monitor bounds as full screen (games, video players, presentations).
        const long WsCaption = 0x00C00000L;
        const long WsThickFrame = 0x00040000L;
        long style = GetWindowLongPtr(h, -16).ToInt64();
        if ((style & (WsCaption | WsThickFrame)) != 0) return false;
        var b = System.Windows.Forms.Screen.FromHandle(h).Bounds;
        return Math.Abs(r.Left - b.Left) <= 2 && Math.Abs(r.Top - b.Top) <= 2
            && Math.Abs(r.Right - b.Right) <= 2 && Math.Abs(r.Bottom - b.Bottom) <= 2;
    }
}
