using System;
using System.Runtime.InteropServices;

namespace BigBlueFish.Windows;

internal static class CompanionTaskbar
{
    private static readonly Guid TaskbarListClassId = new("56FDF344-FD6D-11d0-958A-006097C9A090");

    [ComImport, Guid("56FDF342-FD6D-11d0-958A-006097C9A090"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(IntPtr window);
        [PreserveSig] int DeleteTab(IntPtr window);
        [PreserveSig] int ActivateTab(IntPtr window);
        [PreserveSig] int SetActiveAlt(IntPtr window);
    }

    internal static void HideButton(IntPtr window)
    {
        var type = Type.GetTypeFromCLSID(TaskbarListClassId, throwOnError: true)!;
        var taskbar = (ITaskbarList?)Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Could not create the Windows taskbar service.");
        try
        {
            Marshal.ThrowExceptionForHR(taskbar.HrInit());
            Marshal.ThrowExceptionForHR(taskbar.DeleteTab(window));
        }
        finally
        {
            Marshal.FinalReleaseComObject(taskbar);
        }
    }
}
