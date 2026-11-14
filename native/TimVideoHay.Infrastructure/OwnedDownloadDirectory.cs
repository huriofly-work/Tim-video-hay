using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public static class OwnedDownloadDirectory
{
    public static string Validate(VideoJob job, string stateDirectory)
    {
        if (job.DownloadDirectory is null || job.Source is null) throw new NeedsActionException("Công việc chưa có thư mục video được xác nhận để xóa.");
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(job.Folder));
        var expected = Path.GetFullPath(Path.Combine(root, Rules.FileStem(job.Name)));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(job.DownloadDirectory));
        if (!string.Equals(target, expected, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(target), root, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, Path.TrimEndingDirectorySeparator(Path.GetPathRoot(target)!), StringComparison.OrdinalIgnoreCase))
            throw new NeedsActionException("Đường dẫn thư mục không khớp công việc. App chưa xóa thư mục.");
        // Do not cross junctions/symbolic links, including links in the parent path.
        for (var directory = new DirectoryInfo(target); directory is not null; directory = directory.Parent)
            if (directory.Exists && (directory.Attributes & FileAttributes.ReparsePoint) != 0) throw LinkError();
        var marker = DownloadPolicy.OwnershipMarker(target, stateDirectory);
        PrivateFiles.RejectLinks(stateDirectory); PrivateFiles.RejectLinks(marker);
        var owner = job.Id + "\n" + job.Source.Id;
        var legacy = Path.Combine(target, ".timvideohay-owner");
        if (File.Exists(marker) && File.ReadAllText(marker) != owner) throw OwnerError();
        if (File.Exists(legacy) && File.ReadAllText(legacy) != owner) throw OwnerError();
        if (!Directory.Exists(target))
        {
            if (File.Exists(target)) throw OwnerError();
            if (!job.FolderAbsenceConfirmed) throw new NeedsActionException("Không tìm thấy thư mục cũ. Chọn vị trí mới nếu đã chuyển, hoặc xác nhận thư mục đã xóa thủ công trong lựa chọn Xóa.");
            return target; // Already deleted: never create a replacement directory.
        }
        if (job.FolderAbsenceConfirmed) throw new NeedsActionException("Thư mục đã xuất hiện lại. Mở lại lựa chọn xóa để xác nhận đúng thư mục hiện có.");
        if (!File.Exists(marker) && !File.Exists(legacy)) throw OwnerError();
        var pending = new Stack<string>(); pending.Push(target);
        while (pending.TryPop(out var directory))
            foreach (var item in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(item);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw LinkError();
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(item);
            }
        return target;
    }
    public static async Task DeleteAsync(VideoJob job, string stateDirectory, Func<Task> checkpoint, CancellationToken ct)
    {
        if (job.FolderDeleted) return;
        ct.ThrowIfCancellationRequested();
        var target = Validate(job, stateDirectory);
        job.FolderDeleteStarted = true; await checkpoint();
        ct.ThrowIfCancellationRequested();
        _ = Validate(job, stateDirectory);
        // target is the exact app-owned child checked above, never the selected root.
        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
        var marker = DownloadPolicy.OwnershipMarker(target, stateDirectory);
        if (File.Exists(marker))
        {
            if (File.ReadAllText(marker) != job.Id + "\n" + job.Source!.Id) throw OwnerError();
            File.Delete(marker);
        }
        job.FolderDeleted = true; await checkpoint();
    }
    private static NeedsActionException OwnerError() => new("Không xác nhận được thư mục thuộc công việc này. App chưa xóa; kiểm tra thư mục trước khi thử lại.");
    private static NeedsActionException LinkError() => new("Thư mục có liên kết/junction. App chưa xóa để tránh tác động sang vị trí khác.");
}
