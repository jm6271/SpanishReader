using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using SpanishReader.Core.Models;
using SpanishReader.Core.Services;

namespace SpanishReader.Services;

/// <summary>Bridges native window placement to the platform-independent preference store.</summary>
internal static class WindowPlacementService
{
    private const uint ShowNormal = 1;
    private const uint ShowMinimized = 2;
    private const uint ShowMaximized = 3;
    private const uint RestoreToMaximized = 2;

    public static void Restore(Window window, WindowPlacementStore store)
    {
        if (store.Load() is not { } saved)
            return;

        var placement = NewPlacement();
        placement.ShowCommand = ShowNormal;
        placement.NormalPosition = new NativeRect
        {
            Left = saved.Left, Top = saved.Top, Right = saved.Right, Bottom = saved.Bottom
        };
        // Windows adjusts inaccessible bounds after a monitor is removed. Using
        // the matching native APIs also avoids taskbar-offset drift across launches.
        nint handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (!SetWindowPlacement(handle, ref placement))
        {
            Debug.WriteLine($"Could not restore window placement: {Marshal.GetLastWin32Error()}");
            return;
        }

        // A new HWND initially uses its creation monitor's DPI. The first move to
        // the saved monitor can trigger WM_DPICHANGED and resize the supplied
        // bounds again (for example, by 150/225). Reapply the original physical
        // bounds after that synchronous DPI transition; never scale the saved
        // coordinates ourselves or mix them with screen-coordinate APIs.
        // Also move the normal window to its monitor before maximizing, so both
        // its maximized frame and the Restore button's bounds use the target DPI.
        // PowerToys uses the same two-pass placement pattern:
        // https://github.com/microsoft/PowerToys/blob/main/src/modules/Workspaces/WorkspacesWindowArranger/WindowArranger.cpp
        placement.ShowCommand = saved.IsMaximized ? ShowMaximized : ShowNormal;
        if (!SetWindowPlacement(handle, ref placement))
            Debug.WriteLine($"Could not finish restoring window placement: {Marshal.GetLastWin32Error()}");
    }

    public static void Save(Window window, WindowPlacementStore store)
    {
        var placement = NewPlacement();
        if (!GetWindowPlacement(WinRT.Interop.WindowNative.GetWindowHandle(window), ref placement))
        {
            Debug.WriteLine($"Could not read window placement: {Marshal.GetLastWin32Error()}");
            return;
        }

        // NormalPosition remains the restore bounds even when maximized/minimized.
        // Reopen a minimized window visibly, preserving whether it restores maximized.
        bool maximized = placement.ShowCommand == ShowMaximized
            || (placement.ShowCommand == ShowMinimized && (placement.Flags & RestoreToMaximized) != 0);
        var bounds = placement.NormalPosition;
        var saved = new WindowPlacement(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom, maximized);
        if (!saved.IsValid)
            return;

        try
        {
            store.Save(saved);
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            // A settings write failure must not interrupt shutdown or erase the
            // last successfully saved placement. No dialog can outlive this window.
            Debug.WriteLine($"Could not save window placement: {exception}");
        }
    }

    private static NativePlacement NewPlacement() => new() { Length = (uint)Marshal.SizeOf<NativePlacement>() };

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement
    {
        public uint Length, Flags, ShowCommand;
        public NativePoint MinPosition, MaxPosition;
        public NativeRect NormalPosition;
    }

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint window, ref NativePlacement placement);

    [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPlacement(nint window, ref NativePlacement placement);
}
