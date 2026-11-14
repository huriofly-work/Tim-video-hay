using System.Security.AccessControl;
using System.Security.Principal;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public static class PrivateFiles
{
    public static void RejectLinks(string path, bool recursive = false)
    {
        var full = Path.GetFullPath(path);
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if (File.Exists(current) || Directory.Exists(current))
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new NeedsActionException("Đường dẫn có liên kết/junction. Chọn thư mục thật để tránh đọc, ghi hoặc xóa sang vị trí khác.");
        if (!recursive || !Directory.Exists(full)) return;
        foreach (var child in Directory.EnumerateFileSystemEntries(full))
        {
            RejectLinks(child);
            if (Directory.Exists(child)) RejectLinks(child, true);
        }
    }

    // Call only for dedicated application directories, never for a user's download root.
    public static void EnsureDirectory(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (full == Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!)
            || Enum.GetValues<Environment.SpecialFolder>().Select(Environment.GetFolderPath).Where(p => !string.IsNullOrEmpty(p))
                .Any(p => string.Equals(full, Path.TrimEndingDirectorySeparator(Path.GetFullPath(p)), StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Dữ liệu app phải nằm trong một thư mục riêng, không dùng thư mục hệ thống hoặc thư mục gốc của người dùng.");
        RejectLinks(path);
        Directory.CreateDirectory(path);
        var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Không xác định được tài khoản Windows.");
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        foreach (var identity in new[] { sid, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        new DirectoryInfo(path).SetAccessControl(acl);
    }

    public static void ProtectFile(string path)
    {
        RejectLinks(path);
        var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException();
        var acl = new FileSecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var identity in new[] { sid, new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
            acl.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, AccessControlType.Allow));
        new FileInfo(path).SetAccessControl(acl);
    }

    public static string NewTemporaryDirectory(string parent)
    {
        EnsureDirectory(parent);
        var directory = Path.Combine(parent, Guid.NewGuid().ToString("N"));
        EnsureDirectory(directory); return directory;
    }

    public static void CleanTemporaryDirectory(string parent)
    {
        EnsureDirectory(parent);
        foreach (var item in Directory.EnumerateFileSystemEntries(parent))
        {
            RejectLinks(item, recursive: true);
            if (Directory.Exists(item)) Directory.Delete(item, recursive: true);
            else File.Delete(item);
        }
    }
}
