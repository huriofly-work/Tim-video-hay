using System.Diagnostics;
using System.Runtime.InteropServices;
using TimVideoHay.Core;

namespace TimVideoHay.Infrastructure;

public sealed class MovedFolderFinder(string stateDirectory, ISecretStore secrets,
    Func<VideoJob, CancellationToken, IReadOnlyList<string>>? indexSearch = null,
    Func<IReadOnlyList<string>>? driveRoots = null)
{
    public Task<string?> FindAsync(VideoJob job, IReadOnlyList<string> roots, Func<Task> checkpoint, CancellationToken ct)
        => Task.Run(async () =>
        {
            var saved = MovedDownloadDirectory.Fingerprint(job);
            if (job.DownloadDirectory is null || job.FolderDeleted || Directory.Exists(job.DownloadDirectory) || saved is null) return null;
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(30));
            var token = budget.Token;
            try
            {
                var direct = WindowsFileIdentity.Locate(job.DirectoryIdentity);
                if (direct is null)
                {
                    var video = WindowsFileIdentity.Locate(job.VideoIdentity);
                    if (video is not null) direct = Path.GetDirectoryName(video);
                }
                if (direct is not null && Eligible(job, direct))
                {
                    var media = await MovedDownloadDirectory.MatchAsync(job, direct, stateDirectory, token);
                    await MovedDownloadDirectory.ApplyAsync(job, direct, media, stateDirectory, secrets, checkpoint, token);
                    return "Đã tự tìm và liên kết thư mục chuyển bằng mã định danh Windows: " + direct;
                }
                var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                void Add(string path) { if (Eligible(job, path)) candidates.Add(Path.GetFullPath(path)); }
                // A cross-volume move loses the old file ID. Probe one exact child per
                // local drive, including drive-root destinations; never enumerate a drive.
                foreach (var root in (driveRoots is null ? LocalDriveRoots() : driveRoots()).Concat(roots.Prepend(job.Folder).Take(16)).Distinct(StringComparer.OrdinalIgnoreCase).Take(48))
                {
                    token.ThrowIfCancellationRequested();
                    try { Add(Path.Combine(root, Rules.FileStem(job.Name))); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
                }
                foreach (var path in (indexSearch is null ? SearchIndex(Path.GetFileName(saved.Path), token) : indexSearch(job, token))) { token.ThrowIfCancellationRequested(); Add(path); }
                var watch = Stopwatch.StartNew(); var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var queue = new Queue<(string Path, int Depth)>();
                foreach (var root in roots.Prepend(job.Folder).Distinct(StringComparer.OrdinalIgnoreCase).Take(16))
                    if (SafeRoot(root)) queue.Enqueue((Path.GetFullPath(root), 0));
                while (queue.TryDequeue(out var item) && watch.Elapsed < TimeSpan.FromSeconds(3) && visited.Count < 2000)
                {
                    token.ThrowIfCancellationRequested(); if (!visited.Add(item.Path)) continue;
                    try
                    {
                        PrivateFiles.RejectLinks(item.Path); Add(item.Path);
                        if (item.Depth >= 3 || Eligible(job, item.Path)) continue;
                        foreach (var child in Directory.EnumerateDirectories(item.Path))
                        {
                            token.ThrowIfCancellationRequested();
                            if (watch.Elapsed >= TimeSpan.FromSeconds(3) || queue.Count + visited.Count >= 2000) break;
                            if (SafeRoot(child)) queue.Enqueue((child, item.Depth + 1));
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NeedsActionException) { }
                }
                if (candidates.Count > 16) return "Tìm thấy nhiều thư mục ứng viên; chọn vị trí mới để kiểm tra đúng file.";
                (string Path, MediaFile Media)? match = null;
                foreach (var path in candidates)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        // Cheap length check before reading potentially large files.
                        var file = Path.Combine(path, Path.GetFileName(saved.Path));
                        if (!File.Exists(file) || new FileInfo(file).Length != saved.Length) continue;
                        var media = await MovedDownloadDirectory.MatchAsync(job, path, stateDirectory, token);
                        if (match is not null) return "Có nhiều bản khớp video đã tải; chọn đúng thư mục trước khi liên kết.";
                        match = (path, media);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NeedsActionException) { }
                }
                if (match is null) return "Chưa tìm được thư mục khớp trong phạm vi tìm tự động. Chọn vị trí mới nếu đã chuyển, hoặc xác nhận đã xóa thủ công.";
                await MovedDownloadDirectory.ApplyAsync(job, match.Value.Path, match.Value.Media, stateDirectory, secrets, checkpoint, token);
                return "Đã tự tìm, đối chiếu SHA-256 và liên kết thư mục mới: " + match.Value.Path;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { return "Tìm/đối chiếu thư mục đã hết giới hạn 30 giây; chọn vị trí mới để tiếp tục kiểm tra file."; }
        }, ct);

    private static IReadOnlyList<string> LocalDriveRoots()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
            try
            {
                if (drive.DriveType is DriveType.Fixed or DriveType.Removable && drive.IsReady)
                    roots.Add(drive.RootDirectory.FullName);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return roots;
    }

    private static bool SafeRoot(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            return !full.StartsWith(@"\\", StringComparison.Ordinal) && Directory.Exists(full)
                && Path.TrimEndingDirectorySeparator(full) != Path.TrimEndingDirectorySeparator(Path.GetPathRoot(full)!)
                && (File.GetAttributes(full) & FileAttributes.ReparsePoint) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return false; }
    }
    private static bool Eligible(VideoJob job, string path)
    {
        if (!SafeRoot(path) || path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(s => s.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase))) return false;
        return string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(path)), Rules.FileStem(job.Name), StringComparison.OrdinalIgnoreCase);
    }
    private static IReadOnlyList<string> SearchIndex(string filename, CancellationToken ct)
    {
        var paths = new List<string>(); object? connection = null, records = null;
        try
        {
            if (!OperatingSystem.IsWindows()) return paths;
            var type = Type.GetTypeFromProgID("ADODB.Connection"); if (type is null) return paths;
            connection = Activator.CreateInstance(type)!; dynamic db = connection;
            db.ConnectionTimeout = 2; db.CommandTimeout = 2;
            db.Open("Provider=Search.CollatorDSO;Extended Properties='Application=Windows';", "", "", -1);
            var name = filename.Replace("'", "''", StringComparison.Ordinal);
            ct.ThrowIfCancellationRequested();
            records = db.Execute("SELECT TOP 64 System.ItemPathDisplay FROM SYSTEMINDEX WHERE System.FileName = '" + name + "'", Type.Missing, -1);
            dynamic rows = records;
            while (!rows.EOF && paths.Count < 64)
            {
                ct.ThrowIfCancellationRequested();
                string? file = Convert.ToString(rows.Fields.Item(0).Value);
                if (file is not null && Path.GetDirectoryName(file) is string parent) paths.Add(parent);
                rows.MoveNext();
            }
        }
        catch (Exception ex) when (ex is COMException or System.Runtime.InteropServices.InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or ArgumentException) { }
        finally
        {
            if (records is not null && Marshal.IsComObject(records)) Marshal.FinalReleaseComObject(records);
            if (connection is not null && Marshal.IsComObject(connection)) Marshal.FinalReleaseComObject(connection);
        }
        return paths;
    }
}
