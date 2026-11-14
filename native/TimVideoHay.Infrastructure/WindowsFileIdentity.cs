using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public static class WindowsFileIdentity
{
    private const uint Flags = 0x02000000 | 0x00200000; // Directory handle; do not follow reparse points.
    public static LocalFileIdentity? Capture(string path)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            PrivateFiles.RejectLinks(path);
            using var handle = CreateFileW(Path.GetFullPath(path), 0, 7, IntPtr.Zero, 3, Flags, IntPtr.Zero);
            if (handle.IsInvalid) return null;
            if (!GetFileInformationByHandleEx(handle, 18, out var id, 24)) return null;
            if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0) return null;
            var mount = new StringBuilder(512); var volume = new StringBuilder(512);
            if (!GetVolumePathNameW(Path.GetFullPath(path), mount, 512)) return null;
            if (!GetVolumeNameForVolumeMountPointW(mount.ToString(), volume, 512)) return null;
            return new(volume.ToString(), id.Serial, id.Low, id.High, ((ulong)info.CreatedHigh << 32) | info.CreatedLow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NeedsActionException or ArgumentException) { return null; }
    }
    public static string? Locate(LocalFileIdentity? saved)
    {
        if (saved is null || !OperatingSystem.IsWindows() || !saved.Volume.StartsWith(@"\\?\Volume{", StringComparison.Ordinal)
            || !saved.Volume.EndsWith(@"}\", StringComparison.Ordinal) || saved.Volume.Length != 49) return null;
        using var volume = CreateFileW(saved.Volume, 0, 7, IntPtr.Zero, 3, Flags, IntPtr.Zero);
        if (volume.IsInvalid) return null;
        var descriptor = new Descriptor { Size = 24, Type = 2, Low = saved.Low, High = saved.High };
        using var handle = OpenFileById(volume, ref descriptor, 0, 7, IntPtr.Zero, Flags);
        if (handle.IsInvalid || !GetFileInformationByHandleEx(handle, 18, out var id, 24)
            || !GetFileInformationByHandle(handle, out var info) || (info.Attributes & 0x400) != 0
            || id.Serial != saved.Serial || id.Low != saved.Low || id.High != saved.High
            || (((ulong)info.CreatedHigh << 32) | info.CreatedLow) != saved.Created) return null;
        var path = new StringBuilder(32768); var count = GetFinalPathNameByHandleW(handle, path, 32768, 0);
        if (count == 0 || count >= 32768) return null;
        var value = path.ToString();
        if (value.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return null;
        if (value.StartsWith(@"\\?\", StringComparison.Ordinal)) value = value[4..];
        try { PrivateFiles.RejectLinks(value); return Path.GetFullPath(value); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NeedsActionException or ArgumentException) { return null; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct IdInfo { public ulong Serial, Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct Descriptor { public uint Size; public int Type; public ulong Low, High; }
    [StructLayout(LayoutKind.Sequential)] private struct HandleInfo
    {
        public uint Attributes, CreatedLow, CreatedHigh, AccessLow, AccessHigh, WriteLow, WriteHigh,
            VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeFileHandle OpenFileById(SafeFileHandle volume, ref Descriptor id, uint access, uint share, IntPtr security, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int type, out IdInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetVolumePathNameW(string path, StringBuilder volume, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetVolumeNameForVolumeMountPointW(string mount, StringBuilder volume, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
}
