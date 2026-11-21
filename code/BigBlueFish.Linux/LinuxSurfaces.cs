using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BigBlueFish.Windows;

// X11 counterpart of CompanionWindowsSurfaces. Geometry stays in physical pixels:
// Avalonia reports Window.Position and Screens.WorkingArea in physical pixels too,
// so the shared engine needs no DPI conversion here.
internal static partial class CompanionNative
{
    private static readonly string[] UnperchableWindowTypes =
    [
        "_NET_WM_WINDOW_TYPE_DESKTOP", "_NET_WM_WINDOW_TYPE_DOCK", "_NET_WM_WINDOW_TYPE_TOOLTIP",
        "_NET_WM_WINDOW_TYPE_DROPDOWN_MENU", "_NET_WM_WINDOW_TYPE_POPUP_MENU", "_NET_WM_WINDOW_TYPE_COMBO",
        "_NET_WM_WINDOW_TYPE_DND", "_NET_WM_WINDOW_TYPE_NOTIFICATION", "_NET_WM_WINDOW_TYPE_SPLASH",
    ];

    internal static List<CompanionSurface> Surfaces(IntPtr petHandle)
    {
        var result = new List<CompanionSurface>();
        if (display == IntPtr.Zero || Wayland) return result;
        lock (gate)
        {
            var stacking = Integers(Root, "_NET_CLIENT_LIST_STACKING");
            if (stacking.Length == 0) stacking = RootChildren();
            Array.Reverse(stacking);
            var foreground = Integers(Root, "_NET_ACTIVE_WINDOW").FirstOrDefault();
            int order = 0;
            foreach (var window in stacking)
            {
                if (window == 0 || window == (ulong)petHandle.ToInt64()) continue;
                if (Integers(window, "_NET_WM_PID").FirstOrDefault() == (ulong)Environment.ProcessId) continue;
                if (XGetWindowAttributes(display, window, out var attributes) == 0 || attributes.map_state != 2) continue;
                if (Integers(window, "_NET_WM_STATE").Contains(Atom("_NET_WM_STATE_HIDDEN"))) continue;
                var types = Integers(window, "_NET_WM_WINDOW_TYPE").Select(atom => AtomName(atom)).ToArray();
                if (types.Any(type => UnperchableWindowTypes.Contains(type))) continue;
                if (attributes.width < 80 || attributes.height < 60) continue;
                if (!TryFrameBounds(window, attributes, out var bounds)) continue;
                result.Add(new CompanionSurface(new IntPtr((long)window), bounds, Title(window),
                    (int)Integers(window, "_NET_WM_PID").FirstOrDefault(), order++,
                    window == foreground, false));
            }
        }

        MarkOccluded(result);
        return result;
    }

    // Minimal window managers do not publish _NET_CLIENT_LIST_STACKING; the root's
    // children are still in bottom-to-top order, which is all the planner needs.
    private static ulong[] RootChildren()
    {
        if (XQueryTree(display, Root, out _, out _, out var children, out var count) == 0 || children == IntPtr.Zero) return [];
        try
        {
            var bytes = new byte[count * IntPtr.Size];
            System.Runtime.InteropServices.Marshal.Copy(children, bytes, 0, bytes.Length);
            return Enumerable.Range(0, (int)count)
                .Select(i => IntPtr.Size == 8 ? BitConverter.ToUInt64(bytes, i * 8) : BitConverter.ToUInt32(bytes, i * 4))
                .ToArray();
        }
        finally { XFree(children); }
    }

    private static void MarkOccluded(List<CompanionSurface> surfaces)
    {
        for (int i = 0; i < surfaces.Count; i++)
        {
            var candidate = surfaces[i];
            double x = candidate.Bounds.CenterX;
            double y = candidate.Bounds.Top + 2;
            for (int higher = 0; higher < i; higher++)
            {
                if (surfaces[higher].Bounds.Contains(x, y))
                {
                    surfaces[i] = candidate with { Occluded = true };
                    break;
                }
            }
        }
    }

    private static bool TryFrameBounds(ulong window, Attributes attributes, out CompanionRect bounds)
    {
        bounds = default;
        if (XTranslateCoordinates(display, window, Root, 0, 0, out int x, out int y, out _) == 0) return false;
        // _NET_FRAME_EXTENTS is left, right, top, bottom. Client geometry excludes
        // the decorations, so the perch line has to use the frame's top edge.
        var extents = Integers(window, "_NET_FRAME_EXTENTS");
        int left = extents.Length >= 4 ? (int)extents[0] : 0;
        int right = extents.Length >= 4 ? (int)extents[1] : 0;
        int top = extents.Length >= 4 ? (int)extents[2] : 0;
        int bottom = extents.Length >= 4 ? (int)extents[3] : 0;
        bounds = CompanionRect.FromLTRB(x - left, y - top, x + attributes.width + right, y + attributes.height + bottom);
        return !bounds.IsEmpty;
    }

    private static string Title(ulong window)
    {
        var bytes = Property(window, "_NET_WM_NAME", out _);
        if (bytes.Length == 0) bytes = Property(window, "WM_NAME", out _);
        return Encoding.UTF8.GetString(bytes).TrimEnd('\0');
    }

    private static string AtomName(ulong atom)
    {
        if (atom == 0) return "";
        var name = XGetAtomName(display, atom);
        if (name == IntPtr.Zero) return "";
        try { return System.Runtime.InteropServices.Marshal.PtrToStringAnsi(name) ?? ""; }
        finally { XFree(name); }
    }

    [System.Runtime.InteropServices.DllImport("libX11.so.6")]
    private static extern IntPtr XGetAtomName(IntPtr display, ulong atom);
}
