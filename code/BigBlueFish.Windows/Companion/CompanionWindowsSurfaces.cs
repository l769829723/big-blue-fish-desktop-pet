using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace BigBlueFish.Windows;

// Enumerates real top-level windows so the roaming engine can perch on them.
// Windows reports physical pixels here; the pet window's own DPI transform turns
// them into the same DIP space that WPF uses for Left/Top, which keeps mixed-DPI
// setups consistent without guessing per-monitor scale factors.
internal static class CompanionWindowsSurfaces
{
    private delegate bool EnumWindowsProc(nint window, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect value, int size);
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x00000080L;
    private const int DwmwaExtendedFrameBounds = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    internal static CompanionRect WorkArea(Window pet)
    {
        Rect area = CompanionEdgeLayout.WorkArea(pet);
        return new CompanionRect(area.X, area.Y, area.Width, area.Height);
    }

    // Independent copies of the engine geometry, so callers never share a mutable view.
    internal static CompanionRect VisibleBody(Window pet)
    {
        Rect body = CompanionEdgeLayout.PetBounds(pet);
        return new CompanionRect(pet.Left + body.X, pet.Top + body.Y, body.Width, body.Height);
    }

    internal static void PlaceBody(Window pet, CompanionRect body)
    {
        Rect local = CompanionEdgeLayout.PetBounds(pet);
        pet.Left = body.X - local.X;
        pet.Top = body.Y - local.Y;
    }

    internal static IReadOnlyList<CompanionSurface> Enumerate(Window pet, nint petHandle)
    {
        var surfaces = new List<CompanionSurface>();
        CompanionRect work = WorkArea(pet);
        nint foreground = CompanionNative.GetForegroundWindow();
        int order = 0;
        try
        {
            EnumWindows((window, _) =>
            {
                if (!TryDescribe(window, petHandle, pet, out var bounds)) return true;
                if (!bounds.IntersectsWith(work)) return true;
                surfaces.Add(new CompanionSurface(window, bounds, Title(window), ProcessId(window), order++,
                    window == foreground, false));
                return true;
            }, 0);
        }
        catch (Exception)
        {
            // A destroyed window between enumeration and measurement is harmless.
        }

        MarkOccluded(surfaces);
        return surfaces;
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

    private static bool TryDescribe(nint window, nint petHandle, Window pet, out CompanionRect bounds)
    {
        bounds = default;
        if (window == petHandle || window == 0) return false;
        // Visible, not minimized, not cloaked, not ours and not shell chrome.
        if (!CompanionNative.IsExternalForegroundCandidate(window)) return false;
        if (CompanionNative.IsDesktopWindow(window)) return false;
        if ((GetWindowLongPtr(window, GwlExStyle).ToInt64() & WsExToolWindow) != 0) return false;
        if (!CompanionNative.IsOnCurrentVirtualDesktop(window)) return false;
        if (!TryBounds(window, out var rect)) return false;
        if (rect.Right - rect.Left < 80 || rect.Bottom - rect.Top < 60) return false;
        try
        {
            System.Windows.Point topLeft = pet.PointFromScreen(new System.Windows.Point(rect.Left, rect.Top));
            System.Windows.Point bottomRight = pet.PointFromScreen(new System.Windows.Point(rect.Right, rect.Bottom));
            bounds = CompanionRect.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        return !bounds.IsEmpty;
    }

    private static bool TryBounds(nint window, out NativeRect rect)
    {
        // DWM's extended frame bounds exclude the invisible resize border, so the
        // pet sits on the visual top edge instead of floating above it.
        if (DwmGetWindowAttribute(window, DwmwaExtendedFrameBounds, out rect, Marshal.SizeOf<NativeRect>()) == 0
            && rect.Right > rect.Left && rect.Bottom > rect.Top)
            return true;
        return GetWindowRect(window, out rect) && rect.Right > rect.Left && rect.Bottom > rect.Top;
    }

    private static string Title(nint window)
    {
        var buffer = new StringBuilder(256);
        CompanionNative.GetWindowText(window, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static int ProcessId(nint window)
    {
        GetWindowThreadProcessId(window, out uint id);
        return (int)id;
    }
}
