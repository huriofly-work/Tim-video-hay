using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace TimVideoHay.Desktop;

internal static class WindowAttention
{
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    internal static bool IsInputBlocked(Window window) { var handle = new WindowInteropHelper(window).Handle; return handle != IntPtr.Zero && !IsWindowEnabled(handle); }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
    [StructLayout(LayoutKind.Sequential)] private struct FlashInfo { public uint Size; public IntPtr Window; public uint Flags; public uint Count; public uint Timeout; }
    internal static void Present(Window window)
    {
        if (!window.IsVisible) window.Show();
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Topmost = true; window.Activate();
        var handle = new WindowInteropHelper(window).Handle;
        if (!SetForegroundWindow(handle))
        {
            var flash = new FlashInfo { Size = (uint)Marshal.SizeOf<FlashInfo>(), Window = handle, Flags = 3, Count = 5 };
            FlashWindowEx(ref flash);
        }
        // Bring forward once. Do not cover the browser while the user completes Google/Douyin verification.
        window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => window.Topmost = false));
    }
}
