using System.Security.Cryptography;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public static class MovedDownloadDirectory
{
    internal static MediaFile? Fingerprint(VideoJob job) => job.Media ?? job.Receipts.LastOrDefault(r => r.Action == JobActions.Download && !r.Deleted && r.Directory == job.DownloadDirectory && r.Media is not null)?.Media;
    public static async Task RelinkAsync(VideoJob job, string directory, string stateDirectory, ISecretStore secrets, Func<Task> checkpoint, CancellationToken ct)
    {
        var media = await MatchAsync(job, directory, stateDirectory, ct);
        await ApplyAsync(job, directory, media, stateDirectory, secrets, checkpoint, ct);
    }
    internal static async Task<MediaFile> MatchAsync(VideoJob job, string directory, string stateDirectory, CancellationToken ct)
    {
        var saved = Fingerprint(job);
        if (job.Source is null || job.DownloadDirectory is null || saved is null || job.FolderDeleted)
            throw new NeedsActionException("Cần thông tin video đã tải hoàn chỉnh để đối chiếu thư mục mới. Thư mục chỉ có file tải dở cần đưa về vị trí cũ.");
        var old = Path.TrimEndingDirectorySeparator(Path.GetFullPath(job.DownloadDirectory));
        var next = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        if (Directory.Exists(old) || File.Exists(old)) throw new NeedsActionException("Thư mục cũ vẫn tồn tại; app chưa đổi đường dẫn để tránh bỏ sót kết quả ở vị trí cũ.");
        if (!string.Equals(old, Path.GetFullPath(Path.Combine(job.Folder, Rules.FileStem(job.Name))), StringComparison.OrdinalIgnoreCase) || !Directory.Exists(next)
            || !string.Equals(Path.GetFileName(next), Rules.FileStem(job.Name), StringComparison.OrdinalIgnoreCase))
            throw new NeedsActionException("Chọn đúng thư mục video đã chuyển, giữ tên thư mục của công việc.");
        PrivateFiles.RejectLinks(next);
        var owner = job.Id + "\n" + job.Source.Id;
        var oldMarker = DownloadPolicy.OwnershipMarker(old, stateDirectory);
        PrivateFiles.RejectLinks(oldMarker);
        if (!File.Exists(oldMarker) || File.ReadAllText(oldMarker) != owner)
            throw new NeedsActionException("Thiếu bằng chứng sở hữu thư mục cũ. App chưa liên kết vị trí mới.");
        string Map(string path)
        {
            if (!string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), old, StringComparison.OrdinalIgnoreCase))
                throw new NeedsActionException("Thông tin file cũ không thuộc thư mục công việc.");
            return Path.Combine(next, Path.GetFileName(path));
        }
        async Task Verify(string path, long? length, string hash)
        {
            PrivateFiles.RejectLinks(path);
            if (!File.Exists(path)) throw new NeedsActionException("Thư mục được chọn không có đúng video đã tải.");
            await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (length is not null && input.Length != length || Convert.ToHexString(await SHA256.HashDataAsync(input, ct)) != hash)
                throw new NeedsActionException("Nội dung file không khớp kết quả đã tải. App chưa liên kết hoặc xóa file.");
        }
        var videoPath = Map(saved.Path);
        var covers = saved.Covers?.Select(c => c with { Path = Map(c.Path) }).ToArray();
        var coverPath = saved.CoverPath is null ? null : Map(saved.CoverPath);
        var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { videoPath };
        if (covers is not null) foreach (var cover in covers) known.Add(cover.Path);
        if (coverPath is not null) known.Add(coverPath);
        if (Directory.EnumerateFileSystemEntries(next).Any(p => !known.Contains(Path.GetFullPath(p)) || (File.GetAttributes(p) & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0))
            throw new NeedsActionException("Thư mục mới có file/thư mục ngoài kết quả công việc. App chưa nhận quyền quản lý để tránh xóa nhầm.");
        var nextMarker = DownloadPolicy.OwnershipMarker(next, stateDirectory); PrivateFiles.RejectLinks(nextMarker);
        if (File.Exists(nextMarker) && File.ReadAllText(nextMarker) != owner)
            throw new NeedsActionException("Vị trí mới thuộc công việc khác.");
        await Verify(videoPath, saved.Length, saved.Sha256);
        if (covers is not null) foreach (var cover in covers.Where(c => File.Exists(c.Path))) await Verify(cover.Path, null, cover.Sha256);
        return saved with { Path = videoPath, Covers = covers, CoverPath = coverPath };
    }
    internal static async Task ApplyAsync(VideoJob job, string directory, MediaFile media, string stateDirectory, ISecretStore secrets, Func<Task> checkpoint, CancellationToken ct)
    {
        var old = Path.GetFullPath(job.DownloadDirectory!); var next = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var oldMarker = DownloadPolicy.OwnershipMarker(old, stateDirectory); var nextMarker = DownloadPolicy.OwnershipMarker(next, stateDirectory);
        var owner = job.Id + "\n" + job.Source!.Id;
        var cache = new DouyinDownloadMetadata(secrets); var info = await cache.ReadAsync(job, ct);
        ct.ThrowIfCancellationRequested();
        PrivateFiles.RejectLinks(next, recursive: true);
        PrivateFiles.EnsureDirectory(stateDirectory);
        if (File.Exists(nextMarker) && File.ReadAllText(nextMarker) != owner) throw new NeedsActionException("Vị trí mới đã được công việc khác quản lý.");
        if (!File.Exists(nextMarker))
        {
            using var file = new FileStream(nextMarker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(file); writer.Write(owner);
        }
        job.Folder = Path.GetDirectoryName(next)!; job.DownloadDirectory = next; job.FolderAbsenceConfirmed = false;
        job.Media = media;
        job.DirectoryIdentity = WindowsFileIdentity.Capture(next); job.VideoIdentity = WindowsFileIdentity.Capture(media.Path);
        job.Message = "Đã đối chiếu dung lượng/SHA-256 và liên kết thư mục mới; các kết quả Google giữ nguyên.";
        await checkpoint();
        if (info is not null) await cache.WriteAsync(job, info, CancellationToken.None);
        if (File.Exists(oldMarker) && File.ReadAllText(oldMarker) == owner) File.Delete(oldMarker);
    }
}
