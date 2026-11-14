using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TimVideoHay.Core;
using TimVideoHay.Infrastructure;

var root = Path.GetFullPath(args.FirstOrDefault() ?? "test-output/native-tests");
Directory.CreateDirectory(root);
var bundleIndex = Array.IndexOf(args, "--check-bundle");
if (bundleIndex >= 0)
{
    var assembly = System.Reflection.Assembly.LoadFile(Path.GetFullPath(args[bundleIndex + 1]));
    var client = await GoogleClientConfiguration.ReadBundledAsync(assembly);
    var expected = args.Contains("--expect-bundled");
    if ((client is not null) != expected) throw new Exception("Bundled configuration state mismatch");
    using var resource = assembly.GetManifestResourceStream(GoogleClientConfiguration.ResourceName);
    if (resource is not null) { using var reader = new StreamReader(resource); var json = await reader.ReadToEndAsync(); if (json.Contains("DO_NOT_BUNDLE") || json.Contains("refresh_token")) throw new Exception("Bundled config contains extra token fields"); }
    await File.WriteAllTextAsync(Path.Combine(root, "bundle-check.json"), JsonSerializer.Serialize(new { configurationBundled = client is not null, tokensExcluded = true, diagnostic = "Resource/format check only; no real Google authorization" }));
    Console.WriteLine("PASS bundled Google configuration resource and token exclusion"); return 0;
}
var publicIndex = Array.IndexOf(args, "--probe-public");
if (publicIndex >= 0)
{
    var uri = Rules.DouyinUri(args[publicIndex + 1]); var id = Rules.SourceId(uri) ?? throw new ArgumentException("Cần link video đầy đủ.");
    using var trace = new PublicProbeTraceHandler(); using var http = new HttpClient(trace) { Timeout = Timeout.InfiniteTimeSpan };
    using var reader = new DouyinPublicReader(http); using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(75));
    try
    {
        var info = await reader.ReadAsync(new(id, "https://www.douyin.com/video/" + id), true, budget.Token);
        var variants = info?["formats"]?.AsArray().Where(f => f is not null).Select(f => DownloadPolicy.Variant(f!)).ToArray() ?? [];
        await File.WriteAllTextAsync(Path.Combine(root, "public-responses.json"), JsonSerializer.Serialize(trace.Responses));
        var report = new { succeeded = info is not null, sourceId = id, durationMilliseconds = info?["source_duration_ms"]?.ToString(), metadataSource = info?["metadata_source"]?.ToString(),
            variantCount = variants.Length, largestAdvertisedHeight = variants.Length > 0 ? variants.Max(f => f.Height) : 0, bothCoverGroups = info is not null && DouyinBrowserCapture.HasBothCovers(info),
            accountSessionUsed = false, accountCredentialRead = false, googleCalled = false, mediaDownloaded = false, actualQualityProbed = false };
        await File.WriteAllTextAsync(Path.Combine(root, "public-probe.json"), JsonSerializer.Serialize(report));
        Console.WriteLine(JsonSerializer.Serialize(report)); return info is null ? 1 : 0;
    }
    catch (Exception ex) when (ex is NeedsActionException or OperationCanceledException)
    { await File.WriteAllTextAsync(Path.Combine(root, "public-responses.json"), JsonSerializer.Serialize(trace.Responses)); await File.WriteAllTextAsync(Path.Combine(root, "public-probe.json"), JsonSerializer.Serialize(new { succeeded = false, error = ex is NeedsActionException ? ex.Message : "Lượt thử công khai hết thời gian", accountSessionUsed = false, googleCalled = false })); return 1; }
}
var runRoot = Path.Combine(root, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(runRoot);
var passed = 0; var failed = 0;
async Task Test(string name, Func<Task> action)
{
    var filter = Array.IndexOf(args, "--test-filter");
    if (filter >= 0 && args.Length > filter + 1 && !System.Text.RegularExpressions.Regex.IsMatch(name, args[filter + 1])) return;
    if (args.Contains("--live-only")) return;
    if (args.Contains("--source-only") && !name.StartsWith("Tải công khai") && !name.StartsWith("Tải lại sau") && !name.StartsWith("Metadata lượt") && !name.StartsWith("Bổ sung tải sau") && !name.StartsWith("Đọc thời lượng bằng khách") && !name.StartsWith("Tải gặp 429")) return;
    if (args.Contains("--history-only") && !name.StartsWith("Lịch sử") && !name.StartsWith("Hàng chờ")) return;
    if (args.Contains("--progress-only") && !name.StartsWith("Tiến độ")) return;
    if (args.Contains("--download-only") && !name.StartsWith("Tải song song") && !name.StartsWith("Bộ tải nội bộ") && !name.StartsWith("Tải gián đoạn") && !name.StartsWith("Tiến độ tải") && !name.StartsWith("Tiến độ checkpoint tải") && !name.StartsWith("Partial") && !name.StartsWith("Range sai") && !name.StartsWith("CDN") && !name.StartsWith("Video lớn") && !name.StartsWith("Tải vẫn tiếp tục")) return;
    if (args.Contains("--operations-only") && !name.StartsWith("Thao tác độc lập")) return;
    if (args.Contains("--retry-only") && !name.StartsWith("Chờ 429")) return;
    if (args.Contains("--relocation-only") && !name.StartsWith("Tự tìm") && !name.StartsWith("Windows File ID") && !name.StartsWith("YouTube tự cập nhật")) return;
    try { await action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.WriteLine("FAIL " + name + " — " + ex.GetType().Name + ": " + ex.Message); }
}
static void Assert(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
static async Task Throws<T>(Func<Task> action) where T : Exception { try { await action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
static Task Sync(Action action) { action(); return Task.CompletedTask; }
var media = Path.Combine(runRoot, "sample.mp4"); await File.WriteAllBytesAsync(media, Enumerable.Range(0, 100).Select(x => (byte)x).ToArray());
VideoJob Job(JobMode mode = JobMode.Full) => new() { Mode = mode, Input = "https://www.douyin.com/video/7659702794928147739", Name = "Tên thử: khoa học?", Niche = "Vũ trụ", Folder = runRoot,
    Sheet = new("sheet-id", 7, "Bảng", "Công việc", "sheets-test"), Channel = new("channel-id", "Kênh", "youtube-test"), Source = new("7659702794928147739", "https://www.douyin.com/video/7659702794928147739"),
    Metadata = new("7659702794928147739", 240000, "fixture", DateTimeOffset.UtcNow), Media = new(media, 240, 100, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(awaitFile())), "video/mp4"), Title = "Tên thử", Privacy = "private" };
byte[] awaitFile() => File.ReadAllBytes(media);
await Test("5 phút là Short, phần thập phân trên 5 phút là Long", () => Sync(() => { Assert(Rules.VideoType(240.001) == "Short"); Assert(Rules.VideoType(300) == "Short"); Assert(Rules.VideoType(300.001) == "Long"); }));
await Test("Từ chối thời lượng không hợp lệ", () => Throws<ArgumentException>(() => Sync(() => Rules.VideoType(double.NaN))));
await Test("Chỉ tải không yêu cầu Google/ngách", () => Sync(() => { var job = Job(JobMode.DownloadOnly); job.Sheet = null; job.Channel = null; job.Niche = ""; Rules.Validate(job); }));
await Test("Tên input giữ nguyên, filename được làm an toàn", () => Sync(() => { var job = Job(); Rules.Validate(job); Assert(job.Name == "Tên thử: khoa học?"); Assert(!Rules.FileStem(job.Name).Contains(':')); Assert(Rules.FileStem("CON") == "_CON"); }));
await Test("Tên video giới hạn 30 ký tự gồm cả dấu cách cho cả hai chế độ", async () => {
    foreach (var mode in new[] { JobMode.Full, JobMode.DownloadOnly })
    {
        var job = Job(mode); job.Name = new string('a', 20) + " " + new string('b', 9); Rules.Validate(job);
        Assert(job.Name.Length == 30);
        job.Name += " "; await Throws<ArgumentException>(() => Sync(() => Rules.Validate(job)));
    }
});
await Test("Bộ giới hạn tên giữ nguyên dấu tiếng Việt và emoji khi dán", () => Sync(() => {
    var input = string.Concat(Enumerable.Repeat("a\u0301", 29)) + "👩‍🔬" + "thừa";
    var limited = Rules.LimitNameInput(input);
    Assert(Rules.NameLength(limited) == 30 && limited.EndsWith("👩‍🔬"));
    Assert(Rules.NameLength(Rules.VideoName(limited)) == 30);
}));
await Test("Tiêu đề hỗ trợ 100 Unicode scalar, chặn 101", async () => { var job = Job(); job.Title = string.Concat(Enumerable.Repeat("😀", 100)); Rules.Validate(job); job.Title += "a"; await Throws<ArgumentException>(() => Sync(() => Rules.Validate(job))); });
await Test("Link giả/ngoài Douyin không lọt resolver", async () => { foreach (var url in new[] { "https://douyin.com.evil.test/video/123", "http://www.douyin.com/video/123", "https://user@www.douyin.com/video/123", "https://www.douyin.com:999/video/123" }) await Throws<ArgumentException>(() => Sync(() => Rules.DouyinUri(url))); });

await Test("Chọn 2160p thay 1080p và không chọn bản chỉ có hình", () => Sync(() => {
    var low = new VideoVariant("1080", 1920, 1080, 30, 990, 31_000_000, false, false, "h264");
    var high = low with { Id = "2160", Width = 3840, Height = 2160, Bitrate = 1400 };
    var silent = high with { Id = "silent", Width = 7680, Height = 4320, HasAudio = false };
    Assert(DownloadPolicy.Best([low, high, silent]).Id == "2160");
}));
await Test("Bản tải nguồn được so bằng kích thước thực, không bằng preview", () => Sync(() => {
    var play = new VideoVariant("play", 1280, 720, 30, 700, 24_000_000, false, false, "h264");
    var download = play with { Id = "download_addr", SourceDownload = true, Bitrate = 1068, Size = 38_000_000, Probed = true };
    Assert(DownloadPolicy.Best([play, download]).Id == "download_addr");
    Assert(DownloadPolicy.Best([play with { Width = 1920, Height = 1080 }, download]).Id == "play");
}));
await Test("Bìa đã chọn không lấy frame cover/origin_cover hay ảnh động không liên quan", () => Sync(() => {
    var covers = JsonNode.Parse("""[{"id":"dynamic_cover","url":"https://p3.douyinpic.com/animated.webp","width":4000,"height":4000},{"id":"cover","url":"https://p3.douyinpic.com/cover.jpg"},{"id":"origin_cover","url":"https://p3.douyinpic.com/origin.jpg"},{"id":"horizontal_cover","url":"https://p3.douyinpic.com/landscape.jpg"},{"id":"cover_original_scale","url":"https://p3.douyinpic.com/authored.jpg"}]""")!.AsArray();
    var selected = DownloadPolicy.Covers(covers).ToArray(); Assert(selected.Length == 2 && selected[0].Key == "cover_original_scale" && selected[1].Key == "horizontal_cover");
    Assert(new MediaCover("x", 360, 481, "cover_original_scale", "hash").Orientation == "dọc");
    Assert(new MediaCover("x", 440, 330, "cover", "hash").Orientation == "ngang");
}));
await Test("Poster nguyên kích thước chỉ được dùng khi trùng URI; bìa ngang ưu tiên bản lớn", () => Sync(() => {
    var video = JsonNode.Parse("""{"cover_original_scale":{"uri":"poster","url_list":["https://p3.douyinpic.com/small.jpeg"]},"dynamic_cover":{"uri":"poster","url_list":["https://p3.douyinpic.com/full.webp"]},"cover":{"url_list":["https://p3.douyinpic.com/wrong.jpeg"]},"universal_cover_horizontal169":{"url_list":["https://p3.douyinpic.com/landscape-small.jpeg"]},"universal_cover_horizontal169_big":{"url_list":["https://p3.douyinpic.com/landscape-big.jpeg"]}}""")!;
    var candidates = DouyinWebMetadata.CoverCandidates(video);
    Assert(candidates.Count == 4 && candidates[0]!["source_field"]!.ToString() == "dynamic_cover");
    var horizontal = DownloadPolicy.Covers(candidates).Single(x => x.Key == "horizontal_cover");
    Assert(horizontal.First()["source_field"]!.ToString() == "universal_cover_horizontal169_big");
    video["dynamic_cover"]!["uri"] = "other";
    Assert(DouyinWebMetadata.CoverCandidates(video).All(x => x!["source_field"]!.ToString() != "dynamic_cover"));
}));
await Test("Không lưu WebP/APNG động làm bìa tĩnh", () => Sync(() => {
    byte[] Webp(string kind, byte flag = 0) { var bytes = new byte[30]; "RIFF"u8.CopyTo(bytes); "WEBP"u8.CopyTo(bytes.AsSpan(8)); Encoding.ASCII.GetBytes(kind).CopyTo(bytes, 12); bytes[16] = 10; bytes[20] = flag; return bytes; }
    Assert(DouyinCovers.IsStaticImage(Webp("VP8 ")));
    Assert(!DouyinCovers.IsStaticImage(Webp("ANIM")) && !DouyinCovers.IsStaticImage(Webp("VP8X", 2)));
    var apng = new byte[20]; new byte[] { 137,80,78,71,13,10,26,10 }.CopyTo(apng, 0); "acTL"u8.CopyTo(apng.AsSpan(12));
    Assert(!DouyinCovers.IsStaticImage(apng));
}));
await Test("Alien 03: nhận cover cùng tài nguyên dynamic khi thiếu poster, không nhận origin frame", () => Sync(() => {
    var video = JsonNode.Parse("""{"cover":{"uri":"publisher-image","url_list":["https://p3.douyinpic.com/display.jpeg"]},"dynamic_cover":{"uri":"publisher-image","url_list":["https://p3.douyinpic.com/full.webp"]},"origin_cover":{"uri":"video-frame","url_list":["https://p3.douyinpic.com/frame.jpeg"]}}""")!;
    var candidates = DouyinWebMetadata.CoverCandidates(video);
    var selected = DownloadPolicy.Covers(candidates).Single();
    Assert(selected.Key == "source_cover" && selected.Count() == 2 && selected.First()["source_field"]!.ToString() == "dynamic_cover");
    Assert(candidates.All(x => x!["source_field"]!.ToString() != "origin_cover"));
    video["origin_cover"]!["uri"] = "publisher-image";
    Assert(DouyinWebMetadata.CoverCandidates(video).Count == 0);
    video["origin_cover"]!["uri"] = "video-frame"; video["dynamic_cover"]!["uri"] = "unrelated-animation";
    Assert(DouyinWebMetadata.CoverCandidates(video).Count == 0);
    // A later browser/Select response with an explicit poster supersedes feed fallback.
    candidates.Add(new JsonObject { ["id"] = "cover_original_scale", ["url"] = "https://p3.douyinpic.com/authored.jpeg" });
    Assert(DownloadPolicy.Covers(candidates).Single().Key == "cover_original_scale");
}));
await Test("Metadata web giữ các độ phân giải cao và bỏ codec bytevc2", () => Sync(() => {
    var detail = JsonNode.Parse("""{"aweme_id":"123","video":{"duration":20000,"bit_rate":[{"bit_rate":1402751,"is_bytevc1":1,"FPS":30,"play_addr":{"width":3840,"height":2160,"data_size":44572419,"url_list":["https://p3.douyinpic.com/high.mp4"]}},{"bit_rate":1348054,"is_bytevc1":1,"play_addr":{"width":3840,"height":2160,"url_list":["https://p3.douyinpic.com/media-video-hvc1/"]}},{"bit_rate":2000000,"play_addr":{"url_key":"x_bytevc2_4k_2000000","url_list":["https://p3.douyinpic.com/vvc.mp4"]}}]}}""")!;
    var parsed = DouyinWebMetadata.Parse(detail, new("123", "https://www.douyin.com/video/123"))!;
    var formats = parsed["formats"]!.AsArray(); Assert(formats.Count == 2);
    Assert(DownloadPolicy.Usable(formats[0]!) && !DownloadPolicy.Usable(formats[1]!));
    Assert(DownloadPolicy.Variant(formats[0]!).Height == 2160);
}));
JsonNode DetailReply(string id = "123", bool horizontal = false) => JsonNode.Parse("""{"aweme_detail":{"aweme_id":"123","video":{"cover_original_scale":{"uri":"poster","url_list":["https://p3.douyinpic.com/portrait.webp"]},"play_addr":{"width":3840,"height":2160,"url_list":["https://p3.douyinpic.com/4k.mp4"]}}}}""")!.Also(n => {
    n["aweme_detail"]!["aweme_id"] = id;
    if (horizontal) n["aweme_detail"]!["video"]!["y_cover_hor"] = JsonNode.Parse("""{"url_list":["https://p3.douyinpic.com/horizontal.jpeg"]}""");
});

await Test("Thư mục và tên file không thêm ID, giữ đủ tên 100 ký tự", () => Sync(() => {
    var job = Job(JobMode.DownloadOnly); job.Name = "Tên gọn có dấu"; job.Folder = Path.Combine(runRoot, "clean-names");
    var folder = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "ownership")); Assert(Path.GetFileName(folder) == job.Name);
    Assert(!Directory.EnumerateFileSystemEntries(folder).Any());
    Assert(DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "ownership")) == folder);
    Assert(Rules.FileStem(new string('x', 100)).Length == 100);
}));
await Test("Dấu phần trăm trong tên không bị hiểu thành template của bộ tải", () => Sync(() => {
    var template = DownloadPolicy.OutputTemplate(Path.Combine(runRoot, "100%"), "Tên %(id)s");
    Assert(template.EndsWith(@"100%%\Tên %%(id)s.%(ext)s", StringComparison.Ordinal));
    Assert(Rules.FileStem("Tên %(id)s") == "Tên %(id)s");
}));
await Test("Tên trùng hoặc thư mục có dữ liệu không được ghi đè", async () => {
    var job = Job(JobMode.DownloadOnly); job.Name = "Trùng"; job.Folder = Path.Combine(runRoot, "collision");
    var folder = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "ownership")); var another = Job(JobMode.DownloadOnly); another.Name = job.Name; another.Folder = job.Folder;
    await Throws<NeedsActionException>(() => Sync(() => DownloadPolicy.ReserveDirectory(another, Path.Combine(runRoot, "ownership"))));
    var unowned = Job(JobMode.DownloadOnly); unowned.Name = "Có dữ liệu"; unowned.Folder = job.Folder;
    var occupied = Path.Combine(unowned.Folder, unowned.Name); Directory.CreateDirectory(occupied); File.WriteAllText(Path.Combine(occupied, "keep.txt"), "keep");
    await Throws<NeedsActionException>(() => Sync(() => DownloadPolicy.ReserveDirectory(unowned, Path.Combine(runRoot, "ownership"))));
    Assert(File.ReadAllText(Path.Combine(occupied, "keep.txt")) == "keep");
});
await Test("Chuyển marker cũ sang dữ liệu riêng mà không chạm vào video", () => Sync(() => {
    var job = Job(JobMode.DownloadOnly); job.Name = "Migrate owner"; job.Folder = Path.Combine(runRoot, "owner-migration");
    var folder = Path.Combine(job.Folder, job.Name); Directory.CreateDirectory(folder);
    File.WriteAllText(Path.Combine(folder, ".timvideohay-owner"), job.Id + "\n" + job.Source!.Id);
    File.WriteAllText(Path.Combine(folder, "video.mov"), "preserve");
    Assert(DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "ownership")) == folder);
    Assert(!File.Exists(Path.Combine(folder, ".timvideohay-owner")) && File.ReadAllText(Path.Combine(folder, "video.mov")) == "preserve");
    Assert(DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "ownership")) == folder);
}));
await Test("DPAPI round trip và không ghi plaintext", async () => { var vault = new DpapiSecretStore(Path.Combine(runRoot, "vault")); await vault.WriteAsync("test", "SECRET-never-plaintext"); Assert(await vault.ReadAsync("test") == "SECRET-never-plaintext"); Assert(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(runRoot, "vault/test.dpapi"))).Contains("SECRET")); });
await Test("DPAPI key không thoát thư mục", () => Throws<ArgumentException>(() => new DpapiSecretStore(runRoot).WriteAsync("../token", "secret")));
await Test("SQLite idempotent request và snapshot settings", async () => { var db = new LocalStore(Path.Combine(runRoot, "db")); var job = Job(); await db.SaveAsync(job); job.Message = "Saved"; await db.SaveAsync(job); Assert((await db.ListAsync()).Count == 1); Assert((await db.ListAsync())[0].Message == "Saved"); await db.SaveSettingsAsync(new() { Sheet = job.Sheet }); Assert((await db.SettingsAsync()).Sheet!.SheetId == 7); });
await Test("Nâng dữ liệu v1 giữ job/đích và chặn binary cũ đọc trạng thái mới", async () => {
    var folder = Path.Combine(runRoot, "state-version"); var db = new LocalStore(folder); var job = Job(); job.State = JobState.Complete; await db.SaveAsync(job);
    using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(folder, "jobs.db")); connection.Open();
    using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=1;"; command.ExecuteNonQuery();
    var upgraded = new LocalStore(folder); var saved = (await upgraded.ListAsync()).Single(); Assert(saved.Id == job.Id && saved.State == JobState.Complete && saved.Channel == job.Channel);
    command.CommandText = "PRAGMA user_version;"; Assert(Convert.ToInt32(command.ExecuteScalar()) == 8);
    command.CommandText = "PRAGMA user_version=9;"; command.ExecuteNonQuery();
    await Throws<InvalidDataException>(() => Sync(() => _ = new LocalStore(folder)));
});
await Test("Checkpoint đang chạy được phục hồi thành Paused", async () => { var db = new LocalStore(Path.Combine(runRoot, "restart")); var job = Job(); job.State = JobState.Running; job.SheetWriteStarted = true; job.MediaSendStarted = true; await db.SaveAsync(job); var f = new Fakes(); var c = new JobCoordinator(db, f, f, f); await c.InitializeAsync(); var recovered = (await db.ListAsync())[0]; Assert(recovered.State == JobState.Paused && recovered.SheetWriteStarted && recovered.MediaSendStarted); });
await Test("Một thư mục mặc định; nâng cấp giữ thư mục Full và đường dẫn từng job", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "unified-folder"));
    var settings = new AppSettings { FullDownloadFolder = Path.Combine(runRoot, "full"), DownloadOnlyFolder = Path.Combine(runRoot, "downloads") };
    var oldJob = Job(JobMode.DownloadOnly); var previous = oldJob.Folder; await db.SaveAsync(oldJob);
    await db.SaveSettingsAsync(settings); Assert(settings.VideoFolder == Path.Combine(runRoot, "full") && settings.FullDownloadFolder is null && settings.DownloadOnlyFolder is null);
    settings.VideoFolder = Path.Combine(runRoot, "new-folder"); await db.SaveSettingsAsync(settings);
    Assert((await db.SettingsAsync()).CurrentFolder == settings.VideoFolder && (await db.ListAsync()).Single().Folder == previous);
});
await Test("Thiết lập shared folder cũ chuyển một lần và bỏ trường dư", () => Sync(() => {
    var legacy = JsonSerializer.Deserialize<AppSettings>("""{"DownloadFolder":"F:\\old-video"}""", LocalStore.Json)!;
    legacy.NormalizeFolders(); Assert(legacy.VideoFolder == @"F:\old-video" && legacy.DownloadFolder is null && legacy.FullDownloadFolder is null && legacy.DownloadOnlyFolder is null);
}));
VideoJob OwnedJob(string label) { var job = Job(JobMode.DownloadOnly); job.Folder = Path.Combine(runRoot, label); job.Name = "Video sở hữu"; return job; }
await Test("Xóa thư mục đúng công việc, giữ thư mục bên cạnh và marker riêng", async () => {
    var job = OwnedJob("folder-delete"); var state = Path.Combine(runRoot, "folder-delete-state"); var directory = DownloadPolicy.ReserveDirectory(job, state);
    await File.WriteAllTextAsync(Path.Combine(directory, "video.mp4"), "fixture"); Directory.CreateDirectory(Path.Combine(directory, "sub"));
    await File.WriteAllTextAsync(Path.Combine(directory, "sub", "notes.txt"), "fixture");
    var adjacent = Path.Combine(job.Folder, "Video khác"); Directory.CreateDirectory(adjacent); await File.WriteAllTextAsync(Path.Combine(adjacent, "keep.txt"), "keep");
    var checkpoints = 0; await OwnedDownloadDirectory.DeleteAsync(job, state, () => { checkpoints++; return Task.CompletedTask; }, default);
    Assert(job.FolderDeleted && job.FolderDeleteStarted && checkpoints == 2 && !Directory.Exists(directory) && File.Exists(Path.Combine(adjacent, "keep.txt")));
    Assert(!File.Exists(DownloadPolicy.OwnershipMarker(directory, state)));
    await OwnedDownloadDirectory.DeleteAsync(job, state, () => throw new Exception("Unexpected second delete"), default);
});
await Test("Không xóa root hoặc thư mục có marker công việc khác", async () => {
    var job = OwnedJob("folder-foreign"); var state = Path.Combine(runRoot, "folder-foreign-state"); var directory = DownloadPolicy.ReserveDirectory(job, state);
    await File.WriteAllTextAsync(Path.Combine(directory, "keep.mp4"), "keep");
    job.DownloadDirectory = job.Folder;
    await Throws<NeedsActionException>(() => OwnedDownloadDirectory.DeleteAsync(job, state, () => Task.CompletedTask, default));
    job.DownloadDirectory = directory; await File.WriteAllTextAsync(DownloadPolicy.OwnershipMarker(directory, state), "foreign-owner");
    await Throws<NeedsActionException>(() => OwnedDownloadDirectory.DeleteAsync(job, state, () => Task.CompletedTask, default));
    Assert(File.Exists(Path.Combine(directory, "keep.mp4")) && !job.FolderDeleteStarted);
});
await Test("Hủy trước khi xóa giữ nguyên thư mục; thư mục đã mất cần xác nhận trước retry", async () => {
    var job = OwnedJob("folder-cancel"); var state = Path.Combine(runRoot, "folder-cancel-state"); var directory = DownloadPolicy.ReserveDirectory(job, state);
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    await Throws<OperationCanceledException>(() => OwnedDownloadDirectory.DeleteAsync(job, state, () => Task.CompletedTask, canceled.Token));
    Assert(Directory.Exists(directory));
    await OwnedDownloadDirectory.DeleteAsync(job, state, () => Task.CompletedTask, default);
    job.FolderDeleted = false; await Throws<NeedsActionException>(() => OwnedDownloadDirectory.DeleteAsync(job, state, () => Task.CompletedTask, default));
    job.FolderAbsenceConfirmed = true; await OwnedDownloadDirectory.DeleteAsync(job, state, () => Task.CompletedTask, default);
    Assert(job.FolderDeleted && !Directory.Exists(directory));
});
await Test("Xóa kết hợp lỗi thư mục: retry không xóa lại Google hay upload", async () => {
    var job = OwnedJob("folder-combined"); job.Mode = JobMode.Full; job.State = JobState.Complete; job.YouTubeId = "existing"; job.SheetVerified = true;
    var state = Path.Combine(runRoot, "folder-combined-state"); var directory = DownloadPolicy.ReserveDirectory(job, state);
    var db = new LocalStore(Path.Combine(runRoot, "folder-combined-db")); await db.SaveAsync(job);
    var f = new Fakes { FolderState = state, FailDeleteFolderOnce = true }; var c = new JobCoordinator(db, f, f, f);
    await c.RemoveJobAsync(job.Id, true, true, true, removeHistory: true); await Finish(db, job.Id);
    var partial = (await db.ListAsync()).Single(); Assert(partial.YouTubeDeleted && partial.SheetDeleted && !partial.FolderDeleted && !partial.RemovedFromHistory && Directory.Exists(directory));
    await c.ResumeAsync(job.Id); await Finish(db, job.Id); await c.StopAsync();
    var done = (await db.ListAsync()).Single(); Assert(done.RemovedFromHistory && done.FolderDeleted && f.VideoDeletes == 1 && f.SheetDeletes == 1 && f.FolderDeletes == 2 && f.Uploads == 0 && f.Writes == 0);
});
await Test("Thư mục không thuộc công việc chặn trước khi xóa Google", async () => {
    var job = OwnedJob("folder-preflight"); job.Mode = JobMode.Full; job.YouTubeId = "existing"; job.SheetVerified = true; job.State = JobState.Complete;
    var state = Path.Combine(runRoot, "folder-preflight-state"); var directory = DownloadPolicy.ReserveDirectory(job, state);
    await File.WriteAllTextAsync(DownloadPolicy.OwnershipMarker(directory, state), "foreign");
    var db = new LocalStore(Path.Combine(runRoot, "folder-preflight-db")); await db.SaveAsync(job); var f = new Fakes { FolderState = state }; var c = new JobCoordinator(db, f, f, f);
    await c.RemoveJobAsync(job.Id, true, true, true); await Finish(db, job.Id); await c.StopAsync();
    Assert(f.VideoDeletes == 0 && f.SheetDeletes == 0 && Directory.Exists(directory));
});
await Test("Junction trong thư mục video không được xóa xuyên sang đích khác", async () => {
    var job = OwnedJob("folder-junction"); var state = Path.Combine(runRoot, "folder-junction-state"); var directory = DownloadPolicy.ReserveDirectory(job, state);
    var destination = Path.Combine(runRoot, "junction-destination"); Directory.CreateDirectory(destination); await File.WriteAllTextAsync(Path.Combine(destination, "keep.txt"), "keep");
    var link = Path.GetFullPath(Path.Combine(directory, "linked"));
    Assert(link.StartsWith(Path.GetFullPath(runRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    await CreateJunctionFixtureAsync(link, destination);
    await Throws<NeedsActionException>(() => OwnedDownloadDirectory.DeleteAsync(job, state, () => Task.CompletedTask, default));
    Assert(File.Exists(Path.Combine(destination, "keep.txt")) && Directory.Exists(directory));
    Directory.Delete(link, recursive: false); // Exact verified fixture link, not its target.
});
await Test("Đổi marker trong lúc checkpoint ngăn xóa thư mục", async () => {
    var job = OwnedJob("folder-changed-marker"); var state = Path.Combine(runRoot, "folder-changed-marker-state"); var directory = DownloadPolicy.ReserveDirectory(job, state);
    await Throws<NeedsActionException>(() => OwnedDownloadDirectory.DeleteAsync(job, state, () => File.WriteAllTextAsync(DownloadPolicy.OwnershipMarker(directory, state), "foreign"), default));
    Assert(Directory.Exists(directory) && !job.FolderDeleted);
});
var verticalPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAADCAYAAAC56t6BAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAALSURBVBhXY2DACQAAGwABGrcpYwAAAABJRU5ErkJggg==");
JsonArray CoverUrls() => JsonNode.Parse("""[{"id":"cover_original_scale","url":"https://p3.douyinpic.com/vertical"},{"id":"horizontal_cover","url":"https://p3.douyinpic.com/horizontal"}]""")!.AsArray();
await Test("Cả hai ảnh bìa HTTP lỗi: trả trạng thái thiếu, không chặn video", async () => {
    var job = OwnedJob("covers-none"); Directory.CreateDirectory(job.DownloadDirectory = Path.Combine(job.Folder, Rules.FileStem(job.Name)));
    using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound))));
    var result = await new DouyinCovers("missing-probe", Path.Combine(runRoot, "cover-private"), http).DownloadAsync(job, CoverUrls(), () => Task.CompletedTask, default);
    Assert(result.Covers!.Count == 0 && result.CoverPath is null && result.CoverStatus!.Contains("dọc, ngang") && result.Path == media);
});
await Test("Bìa dọc thành công, bìa ngang lỗi: giữ bìa dọc và tiếp tục", async () => {
    var job = OwnedJob("covers-partial"); Directory.CreateDirectory(job.DownloadDirectory = Path.Combine(job.Folder, Rules.FileStem(job.Name)));
    using var http = new HttpClient(new ScriptedHandler(request => Task.FromResult(request.RequestUri!.AbsolutePath == "/vertical"
        ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(verticalPng) }
        : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound))));
    var result = await new DouyinCovers(Path.GetFullPath("tools/ffprobe.exe"), Path.Combine(runRoot, "cover-private"), http).DownloadAsync(job, CoverUrls(), () => Task.CompletedTask, default);
    Assert(result.Covers!.Count == 1 && result.Covers[0].Orientation == "dọc" && File.Exists(result.CoverPath) && result.CoverStatus!.Contains("bìa ngang"), JsonSerializer.Serialize(result));
    job.Media = result;
    var preserved = await new DouyinCovers("missing-probe", Path.Combine(runRoot, "cover-private"), http).DownloadAsync(job, null, () => Task.CompletedTask, default);
    Assert(preserved.CoverPath == result.CoverPath && File.Exists(result.CoverPath));
});
await Test("File ảnh bìa do người dùng đặt sẵn không bị ghi đè", async () => {
    var job = OwnedJob("covers-conflict"); Directory.CreateDirectory(job.DownloadDirectory = Path.Combine(job.Folder, Rules.FileStem(job.Name)));
    var existing = Path.Combine(job.DownloadDirectory, Rules.FileStem(job.Name) + " - bìa dọc.png"); await File.WriteAllTextAsync(existing, "user file");
    using var http = new HttpClient(new ScriptedHandler(request => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(verticalPng) })));
    var result = await new DouyinCovers(Path.GetFullPath("tools/ffprobe.exe"), Path.Combine(runRoot, "cover-private"), http).DownloadAsync(job, CoverUrls(), () => Task.CompletedTask, default);
    Assert(await File.ReadAllTextAsync(existing) == "user file" && result.CoverStatus is not null && result.Covers!.Count == 0);
});
await Test("Hết thời gian lấy ảnh bìa tự bỏ qua; nút dừng vẫn hủy", async () => {
    var job = OwnedJob("covers-budget"); Directory.CreateDirectory(job.DownloadDirectory = Path.Combine(job.Folder, Rules.FileStem(job.Name)));
    using var http = new HttpClient(new CancellableHandler(async ct => { await Task.Delay(Timeout.Infinite, ct); return new HttpResponseMessage(System.Net.HttpStatusCode.OK); }));
    var loader = new DouyinCovers("missing-probe", Path.Combine(runRoot, "cover-private"), http, TimeSpan.FromMilliseconds(40));
    var result = await loader.DownloadAsync(job, CoverUrls(), () => Task.CompletedTask, default); Assert(result.CoverStatus is not null && result.Covers!.Count == 0);
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    await Throws<OperationCanceledException>(() => loader.DownloadAsync(job, CoverUrls(), () => Task.CompletedTask, canceled.Token));
});
await Test("Tải lỗi không gọi Sheets/YouTube, tải riêng vẫn chạy", async () => { var db = new LocalStore(Path.Combine(runRoot, "isolated")); var f = new Fakes { FailFullDownload = true }; var c = new JobCoordinator(db, f, f, f); var full = Job(); full.Media = null; await c.SubmitAsync(full); await Finish(db, full.Id); Assert((await db.ListAsync())[0].State == JobState.NeedsAction); Assert(f.Writes == 0 && f.Uploads == 0); var single = Job(JobMode.DownloadOnly); single.Input = "https://www.douyin.com/video/7673782911702748458"; await c.SubmitAsync(single); await Finish(db, single.Id); Assert((await db.ListAsync()).Single(x => x.Id == single.Id).State == JobState.Complete); Assert(f.Writes == 0 && f.Uploads == 0); });
await Test("Thiếu ảnh bìa vẫn ghi bảng và đăng video hợp lệ", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "cover-retry")); var f = new Fakes { FailCoverOnce = true }; var c = new JobCoordinator(db, f, f, f);
    var job = Job(); job.DownloadDirectory = runRoot; await c.SubmitAsync(job); await Finish(db, job.Id);
    var resumed = (await db.ListAsync()).Single(); Assert(resumed.State == JobState.Complete && resumed.Media!.Path == media && resumed.Media.CoverStatus == "Thiếu ảnh bìa; tiếp tục video");
    Assert(f.Writes == 1 && f.Uploads == 1);
});
await Test("Media JSON cũ vẫn đọc được, checkpoint chất lượng và thư mục giữ sau restart", async () => {
    var old = JsonSerializer.Deserialize<MediaFile>("""{"Path":"x.mp4","Duration":20,"Length":1,"Sha256":"abc","MimeType":"video/mp4"}""", LocalStore.Json)!;
    Assert(old.Width == 0 && old.CoverPath is null);
    var db = new LocalStore(Path.Combine(runRoot, "quality-checkpoint")); var job = Job(JobMode.DownloadOnly); job.DownloadDirectory = runRoot; job.DownloadFormatId = "download_addr-original-0"; job.DownloadExtension = "mov";
    job.Media = job.Media! with { Width = 3840, Height = 2160, CoverStatus = "Đã tải ảnh bìa", CoverPath = "cover.jpeg", FormatId = job.DownloadFormatId };
    await db.SaveAsync(job); var restored = (await new LocalStore(Path.Combine(runRoot, "quality-checkpoint")).ListAsync()).Single();
    Assert(restored.Media!.Height == 2160 && restored.Media.CoverPath == "cover.jpeg" && restored.DownloadFormatId == job.DownloadFormatId && restored.DownloadDirectory == runRoot && restored.DownloadExtension == "mov");
});
await Test("Sửa bìa công việc Full đã hoàn tất không ghi lại Sheets hoặc upload YouTube", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "refresh-covers")); var f = new Fakes(); var c = new JobCoordinator(db, f, f, f);
    var job = Job(); job.State = JobState.Complete; job.Stage = "Hoàn tất"; job.Progress = 100; job.SheetVerified = true; job.YouTubeId = "existing-id"; job.DownloadDirectory = runRoot;
    await db.SaveAsync(job); await c.RefreshCoversAsync(job.Id, default);
    var refreshed = (await db.ListAsync()).Single();
    Assert(refreshed.State == JobState.Complete && refreshed.YouTubeId == "existing-id" && refreshed.SheetVerified && refreshed.Progress == 100);
    Assert(refreshed.Media!.CoverStatus == "Đã tải ảnh bìa" && f.Writes == 0 && f.Uploads == 0);
});
await Test("Ghi bảng lỗi không gọi upload", async () => { var db = new LocalStore(Path.Combine(runRoot, "sheet-fail")); var f = new Fakes { FailSheet = true }; var c = new JobCoordinator(db, f, f, f); var job = Job(); await c.SubmitAsync(job); await Finish(db, job.Id); Assert(f.Writes == 1 && f.Uploads == 0); });
await Test("File thay đổi ngăn ghi bảng và đăng", async () => { var db = new LocalStore(Path.Combine(runRoot, "file-fail")); var f = new Fakes { ChangedFile = true }; var c = new JobCoordinator(db, f, f, f); var job = Job(); await c.SubmitAsync(job); await Finish(db, job.Id); Assert(f.Writes == 0 && f.Uploads == 0); });
await Test("Video đã có upload chưa rõ bị chặn trước khi tạo, ghi hay tải", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "duplicate")); var old = Job(); old.MediaSendStarted = true; old.State = JobState.NeedsAction; await db.SaveAsync(old);
    var f = new Fakes(); var c = new JobCoordinator(db, f, f, f); await Throws<DuplicateJobException>(() => c.SubmitAsync(Job()));
    Assert((await db.ListAsync()).Count == 1 && f.Writes == 0 && f.Uploads == 0 && f.Validations == 0);
});
await Test("Dừng riêng job đang chạy không dừng job khác, tiếp tục không ghi/upload lại", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "pause-one")); var full = Job();
    var f = new Fakes { BlockUploadJobId = full.Id }; var c = new JobCoordinator(db, f, f, f);
    await c.SubmitAsync(full); await f.UploadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var download = Job(JobMode.DownloadOnly); download.Input = "https://www.douyin.com/video/7673782911702748458"; await c.SubmitAsync(download);
    await c.PauseJobAsync(full.Id); await FinishAutomatically(db, download.Id);
    var paused = (await db.ListAsync()).Single(x => x.Id == full.Id);
    Assert(paused.State == JobState.Paused && paused.YouTubeId == "id" && paused.SheetVerified);
    await c.ResumeAsync(full.Id); await FinishAutomatically(db, full.Id); await c.StopAsync();
    Assert(f.Writes == 1 && f.Uploads == 1);
});
await Test("Dừng riêng job xếp hàng và job chờ xác nhận", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "pause-queued")); var full = Job(); var f = new Fakes { BlockUploadJobId = full.Id };
    var c = new JobCoordinator(db, f, f, f); await c.SubmitAsync(full); await f.UploadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var queued = Job(JobMode.DownloadOnly); queued.Input = "https://www.douyin.com/video/7673782911702748458"; await c.SubmitAsync(queued); await c.PauseJobAsync(queued.Id); await c.PauseJobAsync(full.Id);
    Assert((await db.ListAsync()).All(x => x.State == JobState.Paused));
    full = (await db.ListAsync()).Single(x => x.Id == full.Id); full.State = JobState.WaitingForYouTube; full.NextYouTubeCheckAt = DateTimeOffset.UtcNow.AddHours(1); await db.SaveAsync(full);
    await c.PauseJobAsync(full.Id); Assert((await db.ListAsync()).Single(x => x.Id == full.Id).NextYouTubeCheckAt is null);
    await c.ResumeAsync(queued.Id); await FinishAutomatically(db, queued.Id); await c.StopAsync();
});
await Test("Core không cho ẩn job đang chạy khi giữ kết quả; không ảnh hưởng job khác", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "remove-running")); var full = Job(); var f = new Fakes { BlockUploadJobId = full.Id };
    var c = new JobCoordinator(db, f, f, f); await c.SubmitAsync(full); await f.UploadEntered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    var download = Job(JobMode.DownloadOnly); download.Input = "https://www.douyin.com/video/7673782911702748458"; await c.SubmitAsync(download);
    await Throws<NeedsActionException>(() => c.RemoveJobAsync(full.Id, removeHistory: true)); await FinishAutomatically(db, download.Id);
    var retained = (await db.ListAsync()).Single(x => x.Id == full.Id); Assert(!retained.RemovedFromHistory && retained.SheetVerified && retained.YouTubeId == "id");
    await Throws<DuplicateJobException>(() => c.SubmitAsync(Job())); await c.StopAsync();
});
await Test("Xóa cả hai: lỗi Sheets giữ mục, tiếp tục chỉ xóa phần thiếu, không upload", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "remove-partial")); var full = Job(); full.State = JobState.Complete; full.YouTubeId = "id"; full.SheetVerified = true; full.SheetWriteStarted = true;
    await db.SaveAsync(full); var f = new Fakes { FailDeleteSheetOnce = true }; var c = new JobCoordinator(db, f, f, f);
    await c.RemoveJobAsync(full.Id, deleteSheet: true, deleteYouTube: true, removeHistory: true); await Finish(db, full.Id);
    var partial = (await db.ListAsync()).Single(); Assert(partial.State == JobState.NeedsAction && partial.YouTubeDeleted && !partial.SheetDeleted && !partial.RemovedFromHistory);
    var restarted = new JobCoordinator(db, f, f, f); await restarted.InitializeAsync(); await restarted.ResumeAsync(full.Id); await FinishAutomatically(db, full.Id); await restarted.StopAsync();
    var removed = (await db.ListAsync()).Single(); Assert(removed.RemovedFromHistory && removed.SheetDeleted && removed.YouTubeDeleted && f.VideoDeletes == 1 && f.SheetDeletes == 2);
    Assert(f.Uploads == 0 && f.Writes == 0 && f.Validations == 0 && File.Exists(media));
});
await Test("Chọn xóa một kết quả không xóa kết quả còn lại", async () => {
    foreach (var onlySheet in new[] { true, false })
    {
        var db = new LocalStore(Path.Combine(runRoot, "remove-only-" + onlySheet)); var job = Job(); job.State = JobState.Complete; job.YouTubeId = "id"; job.SheetVerified = true; job.SheetWriteStarted = true; await db.SaveAsync(job);
        var f = new Fakes(); var c = new JobCoordinator(db, f, f, f); await c.RemoveJobAsync(job.Id, deleteSheet: onlySheet, deleteYouTube: !onlySheet); await FinishAutomatically(db, job.Id); await c.StopAsync();
        var removed = (await db.ListAsync()).Single(); Assert(!removed.RemovedFromHistory && removed.SheetDeleted == onlySheet && removed.YouTubeDeleted == !onlySheet && f.Uploads == 0 && f.Writes == 0);
    }
});
await Test("Xóa upload không rõ ID không đoán video và không bỏ mục khỏi lịch sử", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "remove-unknown")); var job = Job(); job.State = JobState.NeedsAction; job.MediaSendStarted = true; await db.SaveAsync(job);
    var f = new Fakes(); var c = new JobCoordinator(db, f, f, f); await Throws<NeedsActionException>(() => c.RemoveJobAsync(job.Id, deleteYouTube: true)); await c.StopAsync();
    var saved = (await db.ListAsync()).Single(); Assert(!saved.RemovedFromHistory && saved.State == JobState.Paused && f.VideoDeletes == 0 && f.Uploads == 0);
});
await Test("Khởi động lại không hồi sinh mục đã xóa hoặc tự đăng job đang xóa", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "remove-restart"));
    var hidden = Job(); hidden.YouTubeId = "hidden-id"; hidden.SheetVerified = true; hidden.RemovedFromHistory = true; hidden.State = JobState.WaitingForYouTube; await db.SaveAsync(hidden);
    var deleting = Job(); deleting.YouTubeId = "deleting-id"; deleting.SheetVerified = true; deleting.State = JobState.Running; deleting.DeletionRequested = true; deleting.RemoveHistoryRequested = false; deleting.DeleteYouTubeRequested = true; deleting.Actions = JobActions.All; await db.SaveAsync(deleting);
    var f = new Fakes(); var c = new JobCoordinator(db, f, f, f); await c.InitializeAsync();
    Assert((await db.ListAsync()).Single(x => x.Id == hidden.Id).RemovedFromHistory && (await db.ListAsync()).Single(x => x.Id == deleting.Id).State == JobState.Paused);
    Assert(f.Checks == 0 && f.Uploads == 0 && f.VideoDeletes == 0);
    await c.ResumeAsync(deleting.Id); await FinishAutomatically(db, deleting.Id); await c.StopAsync();
    Assert(f.VideoDeletes == 1 && f.Uploads == 0 && !(await db.ListAsync()).Single(x => x.Id == deleting.Id).RemovedFromHistory);
});
JsonNode Grid(params JsonNode[] additionalRows) => new JsonObject {
    ["properties"] = new JsonObject { ["sheetId"] = 7, ["gridProperties"] = new JsonObject { ["rowCount"] = 100, ["columnCount"] = 5 } },
    ["data"] = new JsonArray(new JsonObject { ["rowData"] = new JsonArray(new[] { Row(Rules.Headers.Select(Value).Append(new JsonObject()).ToArray()) }.Concat(additionalRows).ToArray()) })
};
static JsonNode Value(string v) => new JsonObject { ["userEnteredValue"] = new JsonObject { ["stringValue"] = v } };
static JsonNode Row(params JsonNode[] values) => new JsonObject { ["values"] = new JsonArray(values) };
await Test("Công thức ra chuỗi rỗng vẫn chiếm dòng", () => Sync(() => { var g = Grid(Row(new JsonObject { ["userEnteredValue"] = new JsonObject { ["formulaValue"] = "=\"\"" }, ["effectiveValue"] = new JsonObject { ["stringValue"] = "" } })); Assert(SheetLayout.Parse(g).LastOccupiedRow == 1); }));
await Test("Dữ liệu cột khác cũng chiếm dòng", () => Sync(() => Assert(SheetLayout.Parse(Grid(Row(new JsonObject(), new JsonObject(), new JsonObject(), new JsonObject(), Value("Không ghi đè")))).LastOccupiedRow == 1)));
await Test("Header trùng bị chặn", () => Throws<NeedsActionException>(() => Sync(() => { var grid = Grid(); grid["data"]![0]!["rowData"]![0]!["values"]!.AsArray().Add(Value(Rules.Headers[0])); SheetLayout.Parse(grid); })));
await Test("Ô gộp không được ghi", () => Throws<NeedsActionException>(() => Sync(() => { var grid = Grid(); grid["merges"] = new JsonArray(new JsonObject { ["startRowIndex"] = 1, ["endRowIndex"] = 2, ["startColumnIndex"] = 0, ["endColumnIndex"] = 2 }); SheetLayout.Parse(grid).ValidateDestination(1, ["url", "name", "niche", "Short"]); })));
await Test("Dropdown strict từ chối ngách chưa có", () => Throws<NeedsActionException>(() => Sync(() => { var cell = new JsonObject { ["dataValidation"] = new JsonObject { ["strict"] = true, ["condition"] = new JsonObject { ["type"] = "ONE_OF_LIST", ["values"] = new JsonArray(new JsonObject { ["userEnteredValue"] = "Vũ trụ" }) } } }; SheetLayout.Parse(Grid(Row(new JsonObject(), new JsonObject(), cell))).ValidateDestination(1, ["url", "name", "Ngách mới", "Short"]); })));
await Test("Source ID tìm canonical và share link", () => Sync(() => { var g = Grid(Row(Value("https://www.iesdouyin.com/share/video/7659702794928147739/"), Value("name"), Value("niche"), Value("Short"))); Assert(SheetLayout.Parse(g).FindSource("7659702794928147739").SequenceEqual([1])); }));
await Test("Giữ nhãn Loại video tiếng Việt của bảng hiện tại", () => Sync(() => { var layout = SheetLayout.Parse(Grid(Row(Value("url"), Value("name"), Value("niche"), Value("Video ngắn")))); Assert(layout.TypeForDestination(2, "Long") == "Video dài"); Assert(layout.ExistingType(1, "Short") == "Video ngắn"); }));
JsonNode Dropdown(string type, params string[] values) => new JsonObject { ["dataValidation"] = new JsonObject {
    ["strict"] = true, ["condition"] = new JsonObject { ["type"] = type,
        ["values"] = new JsonArray(values.Select(v => (JsonNode)new JsonObject { ["userEnteredValue"] = v }).ToArray()) } } };
await Test("Ngách lấy danh sách dropdown thay vì các giá trị đã ghi", () => Sync(() => {
    var cell = Dropdown("ONE_OF_LIST", "Vũ trụ", "Động vật", "Vũ trụ", "", "  ");
    var occupied = Value("Ngách cũ đã bỏ"); occupied["dataValidation"] = Dropdown("ONE_OF_LIST", "Ngách cũ đã bỏ")["dataValidation"]!.DeepClone();
    var grid = Grid(Row(Value("url"), Value("Tên cũ"), occupied), Row(new JsonObject(), new JsonObject(), cell));
    Assert(SheetLayout.Parse(grid).NicheOptions().SequenceEqual(["Vũ trụ", "Động vật"]));
}));
await Test("Thiếu hoặc trống dropdown Ngách báo lỗi rõ", async () => {
    await Throws<NeedsActionException>(() => Sync(() => SheetLayout.Parse(Grid()).NicheOptions()));
    await Throws<NeedsActionException>(() => Sync(() => SheetLayout.Parse(Grid(Row(new JsonObject(), new JsonObject(), Dropdown("ONE_OF_LIST", "")))).NicheOptions()));
});
await Test("Upload Range được hiểu là offset N+1", () => Sync(() => { using var response = new HttpResponseMessage((HttpStatusCode)308); response.Headers.TryAddWithoutValidation("Range", "bytes=0-255"); Assert(YouTubeDestination.AcceptedBytes(response, 1000) == 256); }));
await Test("Upload Range vượt tổng bị chặn", () => Throws<NeedsActionException>(() => Sync(() => { using var response = new HttpResponseMessage((HttpStatusCode)308); response.Headers.TryAddWithoutValidation("Range", "bytes=0-1000"); YouTubeDestination.AcceptedBytes(response, 1000); })));
await Test("Không gửi Bearer tới URL ngoài Google", () => Throws<NeedsActionException>(() => Sync(() => GoogleTransport.ValidateGoogleUri(new Uri("https://googleapis.com.evil.test/upload")))));

async Task<(GoogleSheets Sheets, YouTubeDestination YouTube, MemorySecrets Secrets)> Services(ScriptedHandler handler, ISourceChannelReader? channelReader = null, bool checkTitles = false, bool allowDeletion = false)
{
    var secrets = new MemorySecrets();
    var token = JsonSerializer.Serialize(new OAuthToken("test-bearer", "test-refresh", DateTimeOffset.UtcNow.AddHours(1), GoogleAuth.YouTubeScopes + (allowDeletion ? " " + GoogleAuth.YouTubeDeleteScope : "")));
    await secrets.WriteAsync("youtube-test", token); await secrets.WriteAsync("sheets-test", token);
    var http = new HttpClient(handler); var transport = new GoogleTransport(new GoogleAuth(secrets, http), http);
    return (new(transport, channelReader), new(transport, secrets, checkTitles), secrets);
}
HttpResponseMessage JsonReply(JsonNode json, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(json.ToJsonString(), Encoding.UTF8, "application/json") };
await Test("Xóa YouTube kiểm tra kênh/ID, lưu intent trước DELETE và xử lý 204", async () => {
    var job = Job(); job.YouTubeId = "video-id"; var deletes = 0;
    var svc = await Services(new ScriptedHandler(req => {
        if (req.Method == HttpMethod.Delete) { deletes++; Assert(job.YouTubeDeleteStarted && !job.YouTubeDeleted && req.RequestUri!.Query == "?id=video-id"); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)); }
        Assert(req.Method == HttpMethod.Get);
        return Task.FromResult(JsonReply(req.RequestUri!.AbsolutePath.EndsWith("/channels")
            ? JsonNode.Parse("""{"items":[{"id":"channel-id","snippet":{"title":"Kênh"}}]}""")!
            : JsonNode.Parse("""{"items":[{"id":"video-id","snippet":{"channelId":"channel-id"}}]}""")!));
    }), allowDeletion: true);
    await svc.YouTube.DeleteVideoAsync(job, () => Task.CompletedTask, default);
    Assert(job.YouTubeDeleted && deletes == 1 && job.YouTubeId == "video-id");
    await svc.YouTube.DeleteVideoAsync(job, () => Task.CompletedTask, default); Assert(deletes == 1);
});
await Test("YouTube đọc metadata trống cập nhật đã xóa, không gửi DELETE", async () => {
    var job = Job(); job.YouTubeId = "video-id"; var deletes = 0;
    var svc = await Services(new ScriptedHandler(req => {
        if (req.Method == HttpMethod.Delete) { deletes++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }
        return Task.FromResult(JsonReply(req.RequestUri!.AbsolutePath.EndsWith("/channels")
            ? JsonNode.Parse("""{"items":[{"id":"channel-id","snippet":{"title":"Kênh"}}]}""")! : JsonNode.Parse("""{"items":[]}""")!));
    }));
    await svc.YouTube.DeleteVideoAsync(job, () => Task.CompletedTask, default); Assert(deletes == 0 && job.YouTubeDeleted);
});
await Test("YouTube khác kênh hoặc thiếu ID không gửi DELETE", async () => {
    var job = Job(); job.YouTubeId = "video-id"; var deletes = 0;
    var svc = await Services(new ScriptedHandler(req => {
        if (req.Method == HttpMethod.Delete) deletes++;
        return Task.FromResult(JsonReply(req.RequestUri!.AbsolutePath.EndsWith("/channels")
            ? JsonNode.Parse("""{"items":[{"id":"channel-id","snippet":{"title":"Kênh"}}]}""")!
            : JsonNode.Parse("""{"items":[{"id":"video-id","snippet":{"channelId":"wrong-channel"}}]}""")!));
    }));
    await Throws<NeedsActionException>(() => svc.YouTube.DeleteVideoAsync(job, () => Task.CompletedTask, default));
    job.YouTubeId = null; await Throws<NeedsActionException>(() => svc.YouTube.DeleteVideoAsync(job, () => Task.CompletedTask, default));
    Assert(deletes == 0 && !job.YouTubeDeleted);
});
await Test("Scope chỉ upload không đủ xóa; cấp thêm quyền riêng cho chức năng xóa", async () => {
    var vault = new MemorySecrets(); using var http = new HttpClient(); var auth = new GoogleAuth(vault, http);
    await vault.WriteAsync("upload-only", JsonSerializer.Serialize(new OAuthToken("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), GoogleAuth.YouTubeScopes)));
    await vault.WriteAsync("delete", JsonSerializer.Serialize(new OAuthToken("access", "refresh", DateTimeOffset.UtcNow.AddHours(1), GoogleAuth.YouTubeDeleteScope)));
    Assert(!await auth.HasYouTubeDeleteScopeAsync("upload-only", default) && await auth.HasYouTubeDeleteScopeAsync("delete", default));
    await auth.AdoptYouTubeDeleteGrantAsync("upload-only", "delete", default);
    Assert(await auth.HasYouTubeDeleteScopeAsync("upload-only", default) && await vault.ReadAsync("delete") is null);
});
await Test("Xóa dòng Sheets bị mất phản hồi: tìm theo nguồn khi thử lại, không xóa dòng kế tiếp", async () => {
    var job = Job(); job.SheetVerified = true; job.SheetWriteStarted = true; var removed = false; var posts = 0;
    var svc = await Services(new ScriptedHandler(async req => {
        if (req.Method == HttpMethod.Post)
        {
            posts++; Assert(job.SheetDeleteStarted);
            var body = JsonNode.Parse(await req.Content!.ReadAsStringAsync())!; var range = body["requests"]![0]!["deleteDimension"]!["range"]!;
            Assert(range["sheetId"]!.GetValue<int>() == 7 && range["startIndex"]!.GetValue<int>() == 1 && range["endIndex"]!.GetValue<int>() == 2);
            removed = true; throw new HttpRequestException("Lost response after row delete");
        }
        if (req.RequestUri!.Query.Contains("includeGridData"))
            return JsonReply(new JsonObject { ["sheets"] = new JsonArray(removed ? Grid(Row(Value("https://www.douyin.com/video/7659702794928147700"), Value("Dòng khác"), Value("Khác"), Value("Short")))
                : Grid(Row(Value(job.Source!.CanonicalUrl), Value(job.Name), Value(job.Niche), Value(job.Type)))) });
        return JsonReply(JsonNode.Parse("""{"sheets":[{"properties":{"sheetId":7,"title":"Tab","sheetType":"GRID","gridProperties":{"rowCount":100,"columnCount":5}}}]}""")!);
    }));
    await Throws<HttpRequestException>(() => svc.Sheets.DeleteSheetAsync(job, () => Task.CompletedTask, default));
    Assert(job.SheetDeleteStarted && !job.SheetDeleted);
    await svc.Sheets.DeleteSheetAsync(job, () => Task.CompletedTask, default); Assert(job.SheetDeleted && posts == 1);
});
await Test("Dòng Sheets đổi nội dung hoặc job chưa ghi bảng thì không xóa", async () => {
    var job = Job(); job.SheetVerified = true; var posts = 0;
    var svc = await Services(new ScriptedHandler(req => {
        if (req.Method == HttpMethod.Post) posts++;
        if (req.RequestUri!.Query.Contains("includeGridData"))
            return Task.FromResult(JsonReply(new JsonObject { ["sheets"] = new JsonArray(Grid(Row(Value(job.Source!.CanonicalUrl), Value("Tên đã đổi"), Value(job.Niche), Value(job.Type)))) }));
        return Task.FromResult(JsonReply(JsonNode.Parse("""{"sheets":[{"properties":{"sheetId":7,"title":"Tab","sheetType":"GRID","gridProperties":{"rowCount":100,"columnCount":5}}}]}""")!));
    }));
    await Throws<NeedsActionException>(() => svc.Sheets.DeleteSheetAsync(job, () => Task.CompletedTask, default));
    job.SheetVerified = false; await Throws<NeedsActionException>(() => svc.Sheets.DeleteSheetAsync(job, () => Task.CompletedTask, default));
    Assert(posts == 0 && !job.SheetDeleted);
});
await Test("Tải dropdown từ vùng ô: đọc một lần, giữ thứ tự và xác thực trước ghi", async () => {
    var rangeReads = 0; var gets = 0;
    var svc = await Services(new ScriptedHandler(req => {
        Assert(req.Method == HttpMethod.Get); gets++;
        if (req.RequestUri!.AbsolutePath.Contains("/values/"))
        {
            rangeReads++; Assert(Uri.UnescapeDataString(req.RequestUri.AbsolutePath).EndsWith("/'Tab'' mới'!$E$2:$E$10"));
            return Task.FromResult(JsonReply(JsonNode.Parse("""{"values":[["Vũ trụ"],[""],["Động vật"],["Vũ trụ"]]}""")!));
        }
        if (req.RequestUri.Query.Contains("includeGridData"))
        {
            var grid = Grid(Row(new JsonObject(), new JsonObject(), Dropdown("ONE_OF_RANGE", "=$E$2:$E$10")),
                Row(new JsonObject(), new JsonObject(), Dropdown("ONE_OF_RANGE", "=$E$2:$E$10")));
            return Task.FromResult(JsonReply(new JsonObject { ["sheets"] = new JsonArray(grid) }));
        }
        return Task.FromResult(JsonReply(JsonNode.Parse("""{"sheets":[{"properties":{"sheetId":7,"title":"Tab' mới","sheetType":"GRID","gridProperties":{"rowCount":100,"columnCount":5}}}]}""")!));
    }));
    var options = await svc.Sheets.NichesAsync(Job().Sheet!, default);
    Assert(options.SequenceEqual(["Vũ trụ", "Động vật"]) && rangeReads == 1 && gets == 3);
    var grid = Grid(Row(new JsonObject(), new JsonObject(), Dropdown("ONE_OF_RANGE", "='Nguồn'!A2:A")));
    grid["data"]![0]!["rowData"]![1]!["values"]![2]!["dataValidation"]!["resolvedValues"] = new JsonArray("Vũ trụ", "Động vật");
    var layout = SheetLayout.Parse(grid); layout.ValidateDestination(1, ["url", "name", "Vũ trụ", "Short"]);
    await Throws<NeedsActionException>(() => Sync(() => layout.ValidateDestination(1, ["url", "name", "Ngách khác", "Short"])));
});
await Test("Dropdown vùng nguồn tab khác và vùng có tên dùng đúng tham chiếu", async () => {
    foreach (var reference in new[] { "='Nguồn ngách'!$A$2:$A", "=Ngach" })
    {
        var svc = await Services(new ScriptedHandler(req => {
            Assert(req.Method == HttpMethod.Get);
            if (req.RequestUri!.AbsolutePath.Contains("/values/"))
            {
                Assert(Uri.UnescapeDataString(req.RequestUri.AbsolutePath).EndsWith("/" + reference[1..]));
                return Task.FromResult(JsonReply(JsonNode.Parse("""{"values":[["Vũ trụ","Động vật"]]}""")!));
            }
            if (req.RequestUri.Query.Contains("includeGridData"))
                return Task.FromResult(JsonReply(new JsonObject { ["sheets"] = new JsonArray(Grid(Row(new JsonObject(), new JsonObject(), Dropdown("ONE_OF_RANGE", reference)))) }));
            return Task.FromResult(JsonReply(JsonNode.Parse("""{"namedRanges":[{"name":"Ngach"}],"sheets":[{"properties":{"sheetId":7,"title":"Tab","sheetType":"GRID","gridProperties":{"rowCount":100,"columnCount":5}}}]}""")!));
        }));
        Assert((await svc.Sheets.NichesAsync(Job().Sheet!, default)).SequenceEqual(["Vũ trụ", "Động vật"]));
    }
});
await Test("Đổi bảng đọc lại ngách mới, không dùng cache bảng cũ", async () => {
    var reads = 0;
    var svc = await Services(new ScriptedHandler(req => {
        Assert(req.Method == HttpMethod.Get); var second = req.RequestUri!.AbsolutePath.Contains("/sheet-new");
        if (req.RequestUri.Query.Contains("includeGridData"))
        {
            reads++; return Task.FromResult(JsonReply(new JsonObject { ["sheets"] = new JsonArray(Grid(Row(new JsonObject(), new JsonObject(), Dropdown("ONE_OF_LIST", second ? "Ngách mới" : "Ngách cũ")))) }));
        }
        return Task.FromResult(JsonReply(JsonNode.Parse("""{"sheets":[{"properties":{"sheetId":7,"title":"Tab","sheetType":"GRID","gridProperties":{"rowCount":100,"columnCount":5}}}]}""")!));
    }));
    Assert((await svc.Sheets.NichesAsync(Job().Sheet!, default)).Single() == "Ngách cũ");
    Assert((await svc.Sheets.NichesAsync(Job().Sheet! with { SpreadsheetId = "sheet-new" }, default)).Single() == "Ngách mới" && reads == 2);
});
HttpResponseMessage VideoReply(string privacy = "private", bool kids = false) => JsonReply(JsonNode.Parse("""{"items":[{"id":"video-id","snippet":{"channelId":"channel-id"},"status":{"uploadStatus":"processed","privacyStatus":"private","selfDeclaredMadeForKids":false,"madeForKids":false},"processingDetails":{"processingStatus":"succeeded"}}]}""")!.Also(n => { n["items"]![0]!["status"]!["privacyStatus"] = privacy; n["items"]![0]!["status"]!["madeForKids"] = kids; }));
await Test("Phiên đã hoàn tất sau mất phản hồi: lấy ID, không gửi lại media", async () =>
{
    var puts = 0;
    var handler = new ScriptedHandler(async req => { if (req.Method == HttpMethod.Put) { puts++; Assert(req.Content!.Headers.ContentRange!.ToString() == "bytes */100"); Assert((await req.Content.ReadAsByteArrayAsync()).Length == 0); return JsonReply(new JsonObject { ["id"] = "video-id" }); } return VideoReply(); });
    var svc = await Services(handler); await svc.Secrets.WriteAsync("session", "https://www.googleapis.com/upload/session");
    var job = Job(); job.UploadSessionKey = "session"; job.MediaSendStarted = true;
    await svc.YouTube.UploadAndVerifyAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => { }), default); Assert(job.YouTubeId == "video-id" && puts == 1);
});
await Test("Upload tiếp từ byte Google xác nhận, lưu intent trước khi gửi", async () =>
{
    var puts = 0; var saved = false; var job = Job(); job.UploadSessionKey = "session"; job.MediaSendStarted = true; job.UploadedBytes = 10;
    var handler = new ScriptedHandler(async req =>
    {
        if (req.Method != HttpMethod.Put) return VideoReply();
        puts++;
        if (puts == 1) { var response = new HttpResponseMessage((HttpStatusCode)308); response.Headers.TryAddWithoutValidation("Range", "bytes=0-49"); return response; }
        Assert(saved); Assert(req.Content!.Headers.ContentRange!.From == 50); Assert((await req.Content.ReadAsByteArrayAsync()).SequenceEqual(awaitFile()[50..])); return JsonReply(new JsonObject { ["id"] = "video-id" });
    });
    var svc = await Services(handler); await svc.Secrets.WriteAsync("session", "https://www.googleapis.com/upload/session");
    await svc.YouTube.UploadAndVerifyAsync(job, () => { saved = true; return Task.CompletedTask; }, new DirectProgress<double>(_ => { }), default); Assert(puts == 2);
});
await Test("Phiên hết hạn sau khi gửi media không tạo upload mới", async () =>
{
    var calls = 0; var svc = await Services(new ScriptedHandler(req => { calls++; Assert(req.Method == HttpMethod.Put); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)); }));
    await svc.Secrets.WriteAsync("session", "https://www.googleapis.com/upload/session"); var job = Job(); job.UploadSessionKey = "session"; job.MediaSendStarted = true;
    await Throws<NeedsActionException>(() => svc.YouTube.UploadAndVerifyAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => { }), default)); Assert(calls == 1);
});
await Test("Private bị ép khi yêu cầu Unlisted không báo hoàn tất", async () => { var svc = await Services(new ScriptedHandler(_ => Task.FromResult(VideoReply()))); var job = Job(); job.YouTubeId = "video-id"; job.Privacy = "unlisted"; await Throws<NeedsActionException>(() => svc.YouTube.UploadAndVerifyAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => { }), default)); Assert(job.YouTubeId == "video-id"); });
await Test("Đối tượng trẻ em bị đổi không báo hoàn tất", async () => { var svc = await Services(new ScriptedHandler(_ => Task.FromResult(VideoReply(kids: true)))); var job = Job(); job.YouTubeId = "video-id"; await Throws<NeedsActionException>(() => svc.YouTube.UploadAndVerifyAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => { }), default)); });
await Test("YouTube đang xử lý vẫn xác nhận upload ngay sau một GET, không gửi media", async () => {
    var reads = 0;
    var svc = await Services(new ScriptedHandler(req => {
        Assert(req.Method == HttpMethod.Get); reads++;
        return Task.FromResult(JsonReply(JsonNode.Parse("""{"items":[{"snippet":{"channelId":"channel-id"},"status":{"privacyStatus":"private","selfDeclaredMadeForKids":false,"madeForKids":false},"processingDetails":{"processingStatus":"processing"}}]}""")!));
    }));
    var job = Job(); job.YouTubeId = "video-id";
    await svc.YouTube.UploadAndVerifyAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => { }), default);
    Assert(reads == 1 && job.YouTubeId == "video-id");
});
await Test("Metadata trẻ em chưa có thì tự chờ, không yêu cầu đối soát hoặc hoàn tất sai", async () => {
    var svc = await Services(new ScriptedHandler(_ => Task.FromResult(JsonReply(JsonNode.Parse("""{"items":[{"snippet":{"channelId":"channel-id"},"status":{"privacyStatus":"private","selfDeclaredMadeForKids":false},"processingDetails":{"processingStatus":"succeeded"}}]}""")!))));
    var job = Job(); job.YouTubeId = "video-id";
    await Throws<YouTubePendingException>(() => svc.YouTube.UploadAndVerifyAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => { }), default));
});
await Test("YouTube trả trạng thái upload thất bại vẫn dừng, giữ ID", async () => {
    var svc = await Services(new ScriptedHandler(_ => Task.FromResult(JsonReply(JsonNode.Parse("""{"items":[{"snippet":{"channelId":"channel-id"},"status":{"privacyStatus":"private","uploadStatus":"failed"}}]}""")!))));
    var job = Job(); job.YouTubeId = "video-id";
    await Throws<NeedsActionException>(() => svc.YouTube.UploadAndVerifyAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => { }), default));
    Assert(job.YouTubeId == "video-id");
});
await Test("Tự kiểm tra nhiều lần rồi hoàn tất, bảng ghi một lần và upload một lần", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "youtube-auto")); var f = new Fakes { PendingChecks = 13 };
    var c = new JobCoordinator(db, f, f, f, TimeSpan.FromMilliseconds(15)); var job = Job();
    await c.SubmitAsync(job); await FinishAutomatically(db, job.Id); await c.StopAsync();
    Assert(f.Writes == 1 && f.Uploads == 1 && f.Checks == 14 && f.Validations == 1);
    Assert((await db.ListAsync()).Single().NextYouTubeCheckAt is null);
});
await Test("Công việc đang chờ YouTube không chặn tải riêng; Dừng giữ ID và tắt tự kiểm tra", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "youtube-independent")); var f = new Fakes { PendingChecks = 1000 };
    var c = new JobCoordinator(db, f, f, f, TimeSpan.FromHours(1)); var full = Job();
    await c.SubmitAsync(full); await Finish(db, full.Id);
    Assert((await db.ListAsync()).Single().State == JobState.WaitingForYouTube);
    var single = Job(JobMode.DownloadOnly); single.Input = "https://www.douyin.com/video/7673782911702748458"; await c.SubmitAsync(single); await Finish(db, single.Id);
    Assert((await db.ListAsync()).Single(x => x.Id == single.Id).State == JobState.Complete);
    await c.StopAsync(); Assert((await db.ListAsync()).Single(x => x.Id == full.Id).State == JobState.Paused);
    await Task.Delay(30); Assert(f.Checks == 1 && f.Uploads == 1);
});
await Test("Thoát và mở lại tự xác minh ID cũ, không cần file local hoặc ghi/upload lại", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "youtube-restart")); var f = new Fakes { PendingChecks = 1 };
    var c = new JobCoordinator(db, f, f, f, TimeSpan.FromHours(1)); var job = Job();
    await c.SubmitAsync(job); await Finish(db, job.Id); await c.StopAsync(preservePendingYouTube: true);
    Assert((await db.ListAsync()).Single().State == JobState.WaitingForYouTube);
    var next = new Fakes { ChangedFile = true, FailSheet = true };
    var recovered = new JobCoordinator(db, next, next, next); await recovered.InitializeAsync(); await FinishAutomatically(db, job.Id); await recovered.StopAsync();
    Assert(next.Writes == 0 && next.Uploads == 0 && next.Validations == 0 && next.Checks == 1);
});
await Test("Bản cũ chỉ migrate timeout xử lý, không tự resume lỗi Private hoặc job đã dừng", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "youtube-migration"));
    var old = Job(); old.YouTubeId = "id"; old.SheetVerified = true; old.State = JobState.NeedsAction;
    old.Message = "Video đã có ID nhưng YouTube chưa xử lý xong. Bấm Tiếp tục sau để xác minh; app không upload lại."; await db.SaveAsync(old);
    var mismatch = Job(); mismatch.YouTubeId = "other-id"; mismatch.SheetVerified = true; mismatch.State = JobState.NeedsAction; mismatch.Message = "Chế độ sai"; await db.SaveAsync(mismatch);
    var stopped = Job(); stopped.YouTubeId = "stopped-id"; stopped.SheetVerified = true; stopped.State = JobState.Paused; await db.SaveAsync(stopped);
    var f = new Fakes(); var c = new JobCoordinator(db, f, f, f); await c.InitializeAsync(); await FinishAutomatically(db, old.Id); await c.StopAsync();
    var jobs = await db.ListAsync(); Assert(jobs.Single(x => x.Id == mismatch.Id).State == JobState.NeedsAction && jobs.Single(x => x.Id == stopped.Id).State == JobState.Paused);
    Assert(f.Checks == 1 && f.Writes == 0 && f.Uploads == 0);
});
await Test("Mạng lỗi khi xác minh ID cũ tự thử lại mà không upload", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "youtube-network")); var job = Job(); job.YouTubeId = "id"; job.SheetVerified = true; job.State = JobState.Paused; await db.SaveAsync(job);
    var f = new Fakes { FailCheckNetworkOnce = true }; var c = new JobCoordinator(db, f, f, f, TimeSpan.FromMilliseconds(15));
    await c.ResumeAsync(job.Id);
    await FinishAutomatically(db, job.Id); await c.StopAsync(); Assert(f.Checks == 2 && f.Uploads == 0 && f.Writes == 0);
});
await Test("Adapter HTTP với Google giả lập: đang processing vẫn hoàn tất ngay, một POST/PUT/GET", async () => {
    var posts = 0; var puts = 0; var reads = 0;
    var svc = await Services(new ScriptedHandler(req => {
        if (req.Method == HttpMethod.Post) {
            posts++; var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Location = new Uri("https://www.googleapis.com/upload/session"); return Task.FromResult(response);
        }
        if (req.Method == HttpMethod.Put) { puts++; return Task.FromResult(JsonReply(new JsonObject { ["id"] = "video-id" })); }
        Assert(req.Method == HttpMethod.Get);
        if (req.RequestUri!.AbsolutePath.EndsWith("/channels")) return Task.FromResult(JsonReply(JsonNode.Parse("""{"items":[{"id":"channel-id","snippet":{"title":"Kênh"}}]}""")!));
        reads++;
        var json = JsonNode.Parse("""{"items":[{"snippet":{"channelId":"channel-id"},"status":{"privacyStatus":"private","selfDeclaredMadeForKids":false,"madeForKids":false},"processingDetails":{"processingStatus":"processing"}}]}""")!;
        Assert(!req.RequestUri!.Query.Contains("processingDetails"));
        return Task.FromResult(JsonReply(json));
    }));
    var db = new LocalStore(Path.Combine(runRoot, "youtube-http-auto")); var f = new Fakes();
    var c = new JobCoordinator(db, f, f, svc.YouTube, TimeSpan.FromMilliseconds(15)); var job = Job();
    await c.SubmitAsync(job); await FinishAutomatically(db, job.Id); await c.StopAsync();
    Assert(posts == 1 && puts == 1 && reads == 1 && f.Writes == 1 && f.Validations == 1);
});
await Test("Video không còn tìm thấy sau 24 giờ cần kiểm tra, không tạo upload mới", async () => {
    var svc = await Services(new ScriptedHandler(req => { Assert(req.Method == HttpMethod.Get); return Task.FromResult(JsonReply(new JsonObject { ["items"] = new JsonArray() })); }));
    var job = Job(); job.YouTubeId = "video-id"; job.YouTubeVerificationStartedAt = DateTimeOffset.UtcNow.AddHours(-25);
    await Throws<NeedsActionException>(() => svc.YouTube.UploadAndVerifyAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => { }), default));
    Assert(job.YouTubeId == "video-id");
});
await Test("Upload mới gửi title/mô tả, không trẻ em và lưu URI trước media", async () =>
{
    var job = Job(); var saved = false; MemorySecrets? secrets = null;
    var svc = await Services(new ScriptedHandler(async request =>
    {
        if (request.Method == HttpMethod.Post) { var metadata = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!; Assert(metadata["status"]!["selfDeclaredMadeForKids"]!.GetValue<bool>() == false); Assert(metadata["snippet"]!["title"]!.GetValue<string>() == job.Title); var response = new HttpResponseMessage(HttpStatusCode.OK); response.Headers.Location = new Uri("https://www.googleapis.com/upload/session"); return response; }
        if (request.Method == HttpMethod.Put) { Assert(saved && job.UploadSessionKey is not null && await secrets!.ReadAsync(job.UploadSessionKey) is not null); Assert(job.MediaSendStarted); return JsonReply(new JsonObject { ["id"] = "video-id" }); }
        return VideoReply();
    })); secrets = svc.Secrets;
    await svc.YouTube.UploadAndVerifyAsync(job, () => { saved = true; return Task.CompletedTask; }, new DirectProgress<double>(_ => { }), default); Assert(job.YouTubeId == "video-id");
});

async Task<JsonNode> SheetReply(bool written)
{
    await Task.CompletedTask;
    var job = Job(); var grid = written ? Grid(Row(Value(job.Source!.CanonicalUrl), Value(job.Name), Value(job.Niche), Value(job.Type))) : Grid();
    return new JsonObject { ["sheets"] = new JsonArray(grid) };
}
await Test("Sheets chỉ cập nhật 4 userEnteredValue, không công thức", async () =>
{
    var written = false; var posts = 0; var job = Job();
    var svc = await Services(new ScriptedHandler(async request =>
    {
        if (request.Method == HttpMethod.Post)
        {
            Assert(job.SheetWriteStarted); var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!; var commands = body["requests"]!.AsArray(); Assert(commands.Count == 4);
            Assert(commands.All(c => c!["updateCells"]!["fields"]!.GetValue<string>() == "userEnteredValue"));
            Assert(commands.All(c => c!["updateCells"]!["rows"]![0]!["values"]![0]!["userEnteredValue"]?["stringValue"] is not null)); posts++; written = true; return JsonReply(new JsonObject());
        }
        if (request.RequestUri!.Query.Contains("includeGridData")) return JsonReply(await SheetReply(written));
        return JsonReply(JsonNode.Parse("""{"sheets":[{"properties":{"sheetId":7,"title":"Tên tab đã đổi","sheetType":"GRID","gridProperties":{"rowCount":100,"columnCount":5}}}]}""")!);
    }));
    await svc.Sheets.WriteAndVerifyAsync(job, () => Task.CompletedTask, default); Assert(job.SheetVerified && posts == 1);
});
await Test("Timeout ghi Sheets: retry tìm dòng đã có, không ghi lần hai", async () =>
{
    var job = Job(); job.SheetWriteStarted = true; var posts = 0;
    var svc = await Services(new ScriptedHandler(async request => { if (request.Method == HttpMethod.Post) posts++; if (request.RequestUri!.Query.Contains("includeGridData")) return JsonReply(await SheetReply(true)); return JsonReply(JsonNode.Parse("""{"sheets":[{"properties":{"sheetId":7,"title":"Tab","sheetType":"GRID","gridProperties":{"rowCount":100,"columnCount":5}}}]}""")!); }));
    await svc.Sheets.WriteAndVerifyAsync(job, () => Task.CompletedTask, default); Assert(job.SheetVerified && posts == 0);
});
await Test("Timeout ghi Sheets chưa tìm được dòng không tự append", async () =>
{
    var job = Job(); job.SheetWriteStarted = true; var posts = 0;
    var svc = await Services(new ScriptedHandler(async request => { if (request.Method == HttpMethod.Post) posts++; if (request.RequestUri!.Query.Contains("includeGridData")) return JsonReply(await SheetReply(false)); return JsonReply(JsonNode.Parse("""{"sheets":[{"properties":{"sheetId":7,"title":"Tab","sheetType":"GRID","gridProperties":{"rowCount":100,"columnCount":5}}}]}""")!); }));
    await Throws<NeedsActionException>(() => svc.Sheets.WriteAndVerifyAsync(job, () => Task.CompletedTask, default)); Assert(posts == 0);
});
string DesktopClient(string id = "123-test.apps.googleusercontent.com") => JsonSerializer.Serialize(new { installed = new { client_id = id, client_secret = "desktop-test-only" } });
await Test("Cấu hình sẵn chỉ nhận Google Desktop client hợp lệ", async () => {
    Assert(GoogleClientConfiguration.ParseDesktop(DesktopClient()).ClientId == "123-test.apps.googleusercontent.com");
    foreach (var json in new[] { "[]", "null", "broken", "{}", "{\"web\":{}}", "{\"type\":\"service_account\"}", DesktopClient("outside.example"), DesktopClient("bad\n.apps.googleusercontent.com") })
        await Throws<ArgumentException>(() => Sync(() => GoogleClientConfiguration.ParseDesktop(json)));
});
await Test("Bản cài thiếu cấu hình không giả lập kết nối Google", async () => {
    using var http = new HttpClient(new ScriptedHandler(_ => throw new Exception("Unexpected network")));
    var auth = new GoogleAuth(new MemorySecrets(), http);
    Assert(!await auth.HasClientAsync());
    await Throws<NeedsAuthException>(() => auth.ConfiguredClientAsync());
});
await Test("Có cấu hình sẵn thì không cần nhập JSON và không tự cấp quyền", async () => {
    var vault = new MemorySecrets(); using var http = new HttpClient(new ScriptedHandler(_ => throw new Exception("Unexpected network")));
    var client = GoogleClientConfiguration.ParseDesktop(DesktopClient()); var auth = new GoogleAuth(vault, http, client);
    Assert(await auth.HasClientAsync() && await auth.ConfiguredClientAsync() == client);
    Assert(await vault.ReadAsync("oauth-client") is null && await GoogleClientConfiguration.ReadBundledAsync(typeof(GoogleAuth).Assembly) is null);
});
await Test("Cập nhật cấu hình đóng gói không đổi project của token đang dùng", async () => {
    var vault = new MemorySecrets(); var oldClient = GoogleClientConfiguration.ParseDesktop(DesktopClient("123-old.apps.googleusercontent.com"));
    await vault.WriteAsync("oauth-client", JsonSerializer.Serialize(oldClient));
    await vault.WriteAsync("youtube-old", JsonSerializer.Serialize(new OAuthToken("expired", "old-refresh", DateTimeOffset.UtcNow.AddHours(-1), GoogleAuth.YouTubeScopes)));
    var calls = 0; using var http = new HttpClient(new ScriptedHandler(async request => {
        calls++; Assert(request.RequestUri!.Host == "oauth2.googleapis.com");
        var form = await request.Content!.ReadAsStringAsync(); Assert(form.Contains("client_id=123-old.apps.googleusercontent.com") && form.Contains("refresh_token=old-refresh"));
        return JsonReply(JsonNode.Parse("""{"access_token":"renewed","expires_in":3600}""")!);
    }));
    var auth = new GoogleAuth(vault, http, GoogleClientConfiguration.ParseDesktop(DesktopClient("123-new.apps.googleusercontent.com")));
    Assert(await auth.ConfiguredClientAsync() == oldClient && await auth.AccessTokenAsync("youtube-old", default) == "renewed" && calls == 1);
});
await Test("Kết nối lại giữ khóa dùng chung cho video mới và sau khi mở lại app", async () => {
    var directory = Path.Combine(runRoot, "google-reconnect-persistent"); var db = new LocalStore(directory);
    var vault = new DpapiSecretStore(Path.Combine(directory, "secrets")); var job = Job();
    await db.SaveSettingsAsync(new() { Sheet = job.Sheet, Channel = job.Channel }); await db.SaveAsync(job);
    using var http = new HttpClient(new ScriptedHandler(_ => throw new Exception("Unexpected network")));
    var auth = new GoogleAuth(vault, http);
    foreach (var key in new[] { job.Sheet!.CredentialId, job.Channel!.CredentialId })
    {
        await vault.WriteAsync(key, JsonSerializer.Serialize(new OAuthToken("expired", "old-refresh", DateTimeOffset.UtcNow.AddHours(-1), "scope")));
        await vault.WriteAsync("new-grant", JsonSerializer.Serialize(new OAuthToken("renewed", "new-refresh", DateTimeOffset.UtcNow.AddHours(1), "scope")));
        await auth.AdoptVerifiedGrantAsync(key, "new-grant", default);
        Assert(await vault.ReadAsync("new-grant") is null);
    }
    var reopened = new LocalStore(directory); var defaults = await reopened.SettingsAsync();
    var restartedAuth = new GoogleAuth(new DpapiSecretStore(Path.Combine(directory, "secrets")), http);
    var next = Job(); next.Sheet = defaults.Sheet; next.Channel = defaults.Channel; await reopened.SaveAsync(next);
    foreach (var saved in await reopened.ListAsync())
    {
        Assert(await restartedAuth.AccessTokenAsync(saved.Sheet!.CredentialId, default) == "renewed");
        Assert(await restartedAuth.AccessTokenAsync(saved.Channel!.CredentialId, default) == "renewed");
    }
    await Throws<ArgumentException>(() => auth.AdoptVerifiedGrantAsync("same", "same", default));
});
await Test("Nhiều lần đọc đồng thời chỉ làm mới Google một lần và giữ refresh token", async () => {
    var vault = new MemorySecrets(); await vault.WriteAsync("saved", JsonSerializer.Serialize(new OAuthToken("expired", "long-lived", DateTimeOffset.UtcNow.AddHours(-1), GoogleAuth.DriveScope)));
    var calls = 0; using var http = new HttpClient(new ScriptedHandler(async _ => { Interlocked.Increment(ref calls); await Task.Delay(20); return JsonReply(JsonNode.Parse("""{"access_token":"renewed","expires_in":3600}""")!); }));
    var auth = new GoogleAuth(vault, http, GoogleClientConfiguration.ParseDesktop(DesktopClient()));
    var tokens = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => auth.AccessTokenAsync("saved", default)));
    var saved = JsonSerializer.Deserialize<OAuthToken>((await vault.ReadAsync("saved"))!)!;
    Assert(calls == 1 && tokens.All(t => t == "renewed") && saved.RefreshToken == "long-lived" && saved.Scope == GoogleAuth.DriveScope);
});
await Test("Google làm mới lỗi tạm thời không yêu cầu đăng nhập lại và giữ quyền cũ", async () => {
    foreach (var scenario in new[] { (HttpStatusCode.ServiceUnavailable, "server_error", typeof(HttpRequestException)), (HttpStatusCode.TooManyRequests, "rate_limit_exceeded", typeof(RetryLaterException)), (HttpStatusCode.BadRequest, "invalid_client", typeof(NeedsActionException)), (HttpStatusCode.BadRequest, "invalid_grant", typeof(NeedsAuthException)) })
    {
        var vault = new MemorySecrets(); var original = JsonSerializer.Serialize(new OAuthToken("expired", "saved-refresh", DateTimeOffset.UtcNow.AddHours(-1), GoogleAuth.DriveScope)); await vault.WriteAsync("saved", original);
        using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(new HttpResponseMessage(scenario.Item1) { Content = new StringContent(JsonSerializer.Serialize(new { error = scenario.Item2 })) })));
        var auth = new GoogleAuth(vault, http, GoogleClientConfiguration.ParseDesktop(DesktopClient())); Exception? error = null;
        try { await auth.AccessTokenAsync("saved", default); } catch (Exception ex) { error = ex; }
        Assert(error?.GetType() == scenario.Item3 && await vault.ReadAsync("saved") == original);
    }
});
await Test("Google trả 401 tự làm mới và đọc lại đúng một lần", async () => {
    var vault = new MemorySecrets(); await vault.WriteAsync("saved", JsonSerializer.Serialize(new OAuthToken("rejected", "saved-refresh", DateTimeOffset.UtcNow.AddHours(1), GoogleAuth.DriveScope)));
    var reads = 0; var refreshes = 0; using var http = new HttpClient(new ScriptedHandler(request => {
        if (request.RequestUri!.Host == "oauth2.googleapis.com") { refreshes++; return Task.FromResult(JsonReply(JsonNode.Parse("""{"access_token":"renewed","expires_in":3600}""")!)); }
        reads++; Assert(request.Headers.Authorization?.Parameter == (reads == 1 ? "rejected" : "renewed"));
        return Task.FromResult(reads == 1 ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : JsonReply(new JsonObject()));
    }));
    var api = new GoogleTransport(new GoogleAuth(vault, http, GoogleClientConfiguration.ParseDesktop(DesktopClient())), http);
    await api.JsonAsync("saved", HttpMethod.Get, "https://sheets.googleapis.com/v4/spreadsheets/test", null, default);
    Assert(reads == 2 && refreshes == 1);
});
await Test("Google vẫn trả 401 sau làm mới thì dừng và không lặp vô hạn", async () => {
    var vault = new MemorySecrets(); await vault.WriteAsync("saved", JsonSerializer.Serialize(new OAuthToken("rejected", "saved-refresh", DateTimeOffset.UtcNow.AddHours(1), GoogleAuth.DriveScope)));
    var reads = 0; var refreshes = 0; using var http = new HttpClient(new ScriptedHandler(request => {
        if (request.RequestUri!.Host == "oauth2.googleapis.com") { refreshes++; return Task.FromResult(JsonReply(JsonNode.Parse("""{"access_token":"renewed","expires_in":3600}""")!)); }
        reads++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }));
    var api = new GoogleTransport(new GoogleAuth(vault, http, GoogleClientConfiguration.ParseDesktop(DesktopClient())), http);
    await Throws<NeedsAuthException>(() => api.JsonAsync("saved", HttpMethod.Get, "https://sheets.googleapis.com/v4/spreadsheets/test", null, default));
    Assert(reads == 2 && refreshes == 1);
});
await Test("Google trả 401 khi ghi không tự gửi lại thao tác ghi", async () => {
    var vault = new MemorySecrets(); await vault.WriteAsync("saved", JsonSerializer.Serialize(new OAuthToken("rejected", "saved-refresh", DateTimeOffset.UtcNow.AddHours(1), GoogleAuth.DriveScope)));
    var writes = 0; var refreshes = 0; using var http = new HttpClient(new ScriptedHandler(request => {
        if (request.RequestUri!.Host == "oauth2.googleapis.com") { refreshes++; return Task.FromResult(JsonReply(JsonNode.Parse("""{"access_token":"renewed","expires_in":3600}""")!)); }
        writes++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
    }));
    var auth = new GoogleAuth(vault, http, GoogleClientConfiguration.ParseDesktop(DesktopClient())); var api = new GoogleTransport(auth, http);
    await Throws<HttpRequestException>(() => api.JsonAsync("saved", HttpMethod.Post, "https://sheets.googleapis.com/v4/spreadsheets/test:batchUpdate", new { }, default));
    Assert(writes == 1 && refreshes == 1 && await auth.AccessTokenAsync("saved", default) == "renewed");
});
await Test("Nhập cấu hình riêng không thay project đang giữ quyền cũ", async () => {
    var vault = new MemorySecrets(); var file = Path.Combine(runRoot, "desktop-client.json");
    await File.WriteAllTextAsync(file, DesktopClient()); using var http = new HttpClient(new ScriptedHandler(_ => throw new Exception("Unexpected network")));
    var auth = new GoogleAuth(vault, http); await auth.ImportClientAsync(file); Assert(await auth.HasClientAsync());
    await File.WriteAllTextAsync(file, DesktopClient("123-other.apps.googleusercontent.com"));
    await Throws<NeedsActionException>(() => auth.ImportClientAsync(file));
    Assert((await auth.ConfiguredClientAsync()).ClientId == "123-test.apps.googleusercontent.com");
});
await Test("Wizard không lưu đích trước khi hoàn thành và xác nhận", async () => {
    var service = new SetupService(); var saves = 0;
    var draft = new GoogleConnectionDraft(service, (_, _, _) => { saves++; return Task.CompletedTask; });
    await Throws<NeedsActionException>(() => draft.CommitAsync(default));
    await draft.SelectSheetAsync(default); Assert(draft.Step == ConnectionStep.Sheet && saves == 0);
    await Throws<NeedsActionException>(() => draft.SelectChannelAsync(default));
    await draft.ContinueSheetAsync(7, default); await draft.SelectChannelAsync(default);
    await Throws<NeedsActionException>(() => draft.CommitAsync(default)); Assert(saves == 0);
    draft.ContinueChannel(); await draft.CommitAsync(default); await draft.CancelAsync();
    Assert(saves == 1 && service.Forgotten.Count == 0 && draft.Step == ConnectionStep.Complete);
});
await Test("Wizard hỗ trợ bảng và kênh thuộc hai tài khoản độc lập", async () => {
    var service = new SetupService(); SheetTarget? savedSheet = null; ChannelTarget? savedChannel = null;
    var draft = new GoogleConnectionDraft(service, (sheet, channel, _) => { savedSheet = sheet; savedChannel = channel; return Task.CompletedTask; });
    await draft.SelectSheetAsync(default); await draft.ContinueSheetAsync(7, default); await draft.SelectChannelAsync(default); draft.ContinueChannel(); await draft.CommitAsync(default);
    Assert(savedSheet!.CredentialId == "sheets-account-A" && savedChannel!.CredentialId == "youtube-account-B" && savedSheet.SheetId == 7);
    Assert(service.Checked.Single().SheetId == 7);
});
await Test("Tab không hợp lệ dừng wizard trước bước chọn YouTube", async () => {
    var service = new SetupService { FailCheck = true }; var draft = new GoogleConnectionDraft(service, (_, _, _) => throw new Exception("Unexpected save"));
    await draft.SelectSheetAsync(default);
    await Throws<NeedsActionException>(() => draft.ContinueSheetAsync(999, default)); Assert(service.Checked.Count == 0);
    await Throws<NeedsActionException>(() => draft.ContinueSheetAsync(7, default));
    Assert(draft.Step == ConnectionStep.Sheet && service.ChannelCalls == 0); await draft.CancelAsync();
});
await Test("Hủy wizard sau lỗi kênh chỉ xóa quyền mới, giữ đích và quyền cũ", async () => {
    var old = Job(); var service = new SetupService { FailChannel = true }; var saves = 0;
    var draft = new GoogleConnectionDraft(service, (_, _, _) => { saves++; return Task.CompletedTask; }, old.Sheet, old.Channel);
    await draft.SelectSheetAsync(default); await draft.ContinueSheetAsync(7, default);
    await Throws<NeedsActionException>(() => draft.SelectChannelAsync(default)); await draft.CancelAsync();
    Assert(saves == 0 && service.Forgotten.SequenceEqual(new[] { "sheets-account-A" }));
    Assert(old.Sheet!.CredentialId == "sheets-test" && old.Channel!.CredentialId == "youtube-test");
});
await Test("Hủy tại bước xác nhận xóa cả hai quyền mới, không lưu đích", async () => {
    var old = Job(); var service = new SetupService(); var saves = 0;
    var draft = new GoogleConnectionDraft(service, (_, _, _) => { saves++; return Task.CompletedTask; }, old.Sheet, old.Channel);
    await draft.SelectSheetAsync(default); await draft.ContinueSheetAsync(7, default); await draft.SelectChannelAsync(default); draft.ContinueChannel();
    await draft.CancelAsync(); Assert(saves == 0 && service.Forgotten.Order().SequenceEqual(new[] { "sheets-account-A", "youtube-account-B" }.Order()));
});
await Test("Đổi lựa chọn trong wizard dọn quyền nháp bị thay, không xóa quyền đang dùng", async () => {
    var service = new SetupService(); var draft = new GoogleConnectionDraft(service, (_, _, _) => Task.CompletedTask);
    await draft.SelectSheetAsync(default); service.Selection = service.Selection with { CredentialId = "sheets-second" }; await draft.SelectSheetAsync(default);
    await draft.ContinueSheetAsync(7, default); await draft.SelectChannelAsync(default); draft.ContinueChannel(); draft.Back();
    service.Channel = service.Channel with { CredentialId = "youtube-second" }; await draft.SelectChannelAsync(default); draft.ContinueChannel();
    await draft.CommitAsync(default); await draft.CancelAsync();
    Assert(service.Forgotten.Order().SequenceEqual(new[] { "sheets-account-A", "youtube-account-B" }.Order()));
});
await Test("Lưu kết nối thất bại giữ nháp để thử lại, không báo hoàn tất", async () => {
    var service = new SetupService(); var saves = 0;
    var draft = new GoogleConnectionDraft(service, (_, _, _) => ++saves == 1 ? throw new IOException("Save failed") : Task.CompletedTask);
    await draft.SelectSheetAsync(default); await draft.ContinueSheetAsync(7, default); await draft.SelectChannelAsync(default); draft.ContinueChannel();
    await Throws<IOException>(() => draft.CommitAsync(default)); Assert(draft.Step == ConnectionStep.Confirm && service.Forgotten.Count == 0);
    await draft.CommitAsync(default); await draft.CancelAsync(); Assert(saves == 2 && service.Forgotten.Count == 0);
});
await Test("Đổi đích đã lưu không sửa snapshot công việc cũ", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "wizard-db")); var job = Job(); await db.SaveAsync(job);
    var service = new SetupService(); var draft = new GoogleConnectionDraft(service, (sheet, channel, ct) => db.SaveSettingsAsync(new() { Sheet = sheet, Channel = channel }, ct), job.Sheet, job.Channel);
    await draft.SelectSheetAsync(default); await draft.ContinueSheetAsync(7, default); await draft.SelectChannelAsync(default); draft.ContinueChannel(); await draft.CommitAsync(default);
    Assert((await db.SettingsAsync()).Channel!.ChannelId == "channel-account-B");
    var stored = (await db.ListAsync()).Single(); Assert(stored.Sheet == job.Sheet && stored.Channel == job.Channel);
});
await Test("Hủy kiểm tra tab không chuyển bước hoặc lưu kết nối", async () => {
    var service = new SetupService(); var draft = new GoogleConnectionDraft(service, (_, _, _) => throw new Exception("Unexpected save"));
    await draft.SelectSheetAsync(default); using var cancel = new CancellationTokenSource(); cancel.Cancel();
    await Throws<OperationCanceledException>(() => draft.ContinueSheetAsync(7, cancel.Token)); Assert(draft.Step == ConnectionStep.Sheet); await draft.CancelAsync();
});
await Test("Mạng tải chặn miền giả, localhost, IP, HTTP, userinfo và port lạ", async () => {
    foreach (var url in new[] { "http://www.douyin.com/a", "https://www.douyin.com.evil.test/a", "https://localhost/a", "https://127.0.0.1/a", "https://192.168.1.1/a", "https://user@www.douyin.com/a", "https://www.douyin.com:444/a", "https://p3.douyinpic.com/a#fragment" })
        await Throws<NeedsActionException>(() => Sync(() => DouyinHttp.Validate(url)));
    Assert(DouyinHttp.Allowed("https://v3.douyinvod.com/video.mp4") && DouyinHttp.Allowed("https://p3.douyinpic.com/cover.webp"));
});
await Test("IP nội bộ, mapped IPv6, multicast và documentation không được kết nối", () => Sync(() => {
    foreach (var ip in new[] { "0.0.0.0", "10.1.2.3", "127.0.0.1", "100.64.0.1", "172.31.0.1", "192.168.1.1", "169.254.169.254", "198.18.0.1", "192.0.2.1", "203.0.113.1", "224.0.0.1", "255.255.255.255", "::1", "::ffff:127.0.0.1", "fe80::1", "fc00::1", "ff02::1", "2001:db8::1", "2002:7f00:1::1" }) Assert(!DouyinHttp.PublicAddress(IPAddress.Parse(ip)), ip);
    Assert(DouyinHttp.PublicAddress(IPAddress.Parse("8.8.8.8")) && DouyinHttp.PublicAddress(IPAddress.Parse("2606:4700:4700::1111")));
}));
await Test("Redirect ngoài miền bị chặn trước khi gửi yêu cầu tiếp theo", async () => {
    var calls = 0; using var http = new HttpClient(new ScriptedHandler(_ => {
        calls++; var response = new HttpResponseMessage(HttpStatusCode.Redirect); response.Headers.Location = new Uri("https://127.0.0.1/private"); return Task.FromResult(response);
    }));
    await Throws<NeedsActionException>(async () => { using var response = await DouyinHttp.GetAsync(http, "https://www.douyin.com/a", null, default); }); Assert(calls == 1);
});
await Test("Redirect hợp lệ giữ Range, không mang cookie/token tới CDN", async () => {
    var calls = 0; using var http = new HttpClient(new ScriptedHandler(request => {
        calls++; Assert(request.Headers.Authorization is null && !request.Headers.Contains("Cookie") && request.Headers.Range!.Ranges.Single().From == 5);
        var response = new HttpResponseMessage(calls == 1 ? HttpStatusCode.Redirect : HttpStatusCode.PartialContent);
        if (calls == 1) response.Headers.Location = new Uri("https://v3.douyinvod.com/b"); return Task.FromResult(response);
    }));
    using var reply = await DouyinHttp.GetAsync(http, "https://www.douyin.com/a", "https://www.douyin.com/video/123", default, new System.Net.Http.Headers.RangeHeaderValue(5, null));
    Assert(calls == 2 && reply.StatusCode == HttpStatusCode.PartialContent);
});
await Test("Vòng redirect bị giới hạn, không chạy vô tận", async () => {
    var calls = 0; using var http = new HttpClient(new ScriptedHandler(_ => { calls++; var reply = new HttpResponseMessage(HttpStatusCode.Redirect); reply.Headers.Location = new Uri("/loop", UriKind.Relative); return Task.FromResult(reply); }));
    await Throws<HttpRequestException>(async () => { using var response = await DouyinHttp.GetAsync(http, "https://www.douyin.com/a", null, default); }); Assert(calls == 6);
});
await Test("Tool hash thật được chấp nhận; binary khác bị chặn trước chạy", async () => {
    Assert(ToolIntegrity.Verify(Path.GetFullPath("tools/ffprobe.exe")).EndsWith("ffprobe.exe"));
    var folder = Path.Combine(runRoot, "tampered-tool"); Directory.CreateDirectory(folder);
    var path = Path.Combine(folder, "ffprobe.exe"); await File.WriteAllTextAsync(path, "tampered executable");
    await Throws<NeedsActionException>(() => Sync(() => ToolIntegrity.Verify(path)));
});
await Test("ACL thư mục credential không kế thừa quyền Everyone/Users", async () => {
    var folder = Path.Combine(runRoot, "private-acl"); var vault = new DpapiSecretStore(folder); await vault.WriteAsync("test", "secret");
    var directoryAcl = System.IO.FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(folder));
    var fileAcl = System.IO.FileSystemAclExtensions.GetAccessControl(new FileInfo(Path.Combine(folder, "test.dpapi")));
    Assert(directoryAcl.AreAccessRulesProtected && fileAcl.AreAccessRulesProtected);
    var allowed = new[] { System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value, "S-1-5-18" };
    foreach (System.Security.AccessControl.FileSystemAccessRule rule in directoryAcl.GetAccessRules(true, true, typeof(System.Security.Principal.SecurityIdentifier))) Assert(allowed.Contains(rule.IdentityReference.Value) && !rule.IsInherited);
});
await Test("Không đổi ACL của thư mục gốc hệ thống/người dùng", async () => {
    await Throws<ArgumentException>(() => Sync(() => PrivateFiles.EnsureDirectory(Path.GetPathRoot(runRoot)!)));
    await Throws<ArgumentException>(() => Sync(() => PrivateFiles.EnsureDirectory(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData))));
});
await Test("Schema 4 plaintext chuyển nguyên checkpoint sang DPAPI và thu gọn DB", async () => {
    var folder = Path.Combine(runRoot, "legacy-encryption"); Directory.CreateDirectory(folder); var job = Job(); job.Name = "SensitiveName123"; job.State = JobState.Paused;
    var settings = new AppSettings { Sheet = job.Sheet, Channel = job.Channel, FullDownloadFolder = "D:\\SensitiveDestination123" };
    using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(folder, "jobs.db") + ";Pooling=False")) {
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE jobs(id TEXT PRIMARY KEY, requestId TEXT NOT NULL UNIQUE, created TEXT NOT NULL, payload TEXT NOT NULL); CREATE TABLE settings(id INTEGER PRIMARY KEY, payload TEXT NOT NULL); PRAGMA user_version=4; INSERT INTO jobs VALUES($id,$request,$created,$payload); INSERT INTO settings VALUES(1,$settings);";
        command.Parameters.AddWithValue("$id", job.Id); command.Parameters.AddWithValue("$request", job.RequestId); command.Parameters.AddWithValue("$created", job.Created.ToString("O")); command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(job, LocalStore.Json)); command.Parameters.AddWithValue("$settings", JsonSerializer.Serialize(settings, LocalStore.Json)); command.ExecuteNonQuery();
    }
    var store = new LocalStore(folder); var restored = (await store.ListAsync()).Single(); Assert(restored.Id == job.Id && restored.Name == job.Name && restored.State == JobState.Paused && restored.Channel == job.Channel);
    Assert((await store.SettingsAsync()).VideoFolder == settings.FullDownloadFolder);
    var bytes = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(folder, "jobs.db"))); Assert(!bytes.Contains("SensitiveName123") && !bytes.Contains("SensitiveDestination123") && !bytes.Contains(job.Input));
});
await Test("Payload bị thay plaintext sau nâng cấp không được đọc", async () => {
    var folder = Path.Combine(runRoot, "plaintext-tamper"); var store = new LocalStore(folder); var job = Job(); await store.SaveAsync(job);
    using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(folder, "jobs.db")); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE jobs SET payload=$payload"; command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(job, LocalStore.Json)); command.ExecuteNonQuery();
    await Throws<InvalidDataException>(() => store.ListAsync());
});
await Test("Hoán đổi payload công việc mã hóa bị phát hiện", async () => {
    var folder = Path.Combine(runRoot, "row-tamper"); var store = new LocalStore(folder); var a = Job(); var b = Job(); await store.SaveAsync(a); await store.SaveAsync(b);
    using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(folder, "jobs.db")); connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE jobs SET payload=(SELECT payload FROM jobs WHERE id=$a) WHERE id=$b"; command.Parameters.AddWithValue("$a", a.Id); command.Parameters.AddWithValue("$b", b.Id); command.ExecuteNonQuery();
    await Throws<InvalidDataException>(() => store.ListAsync());
});
await Test("Junction chặn cả thao tác tải và thư mục secret", async () => {
    var real = Path.Combine(runRoot, "download-link-real"); Directory.CreateDirectory(real);
    var link = Path.Combine(runRoot, "download-link");
    await CreateJunctionFixtureAsync(link, real);
    var job = Job(); job.Folder = link;
    await Throws<NeedsActionException>(() => Sync(() => DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "link-state"))));
    await Throws<NeedsActionException>(() => new DpapiSecretStore(link).WriteAsync("test", "secret"));
    Assert(!Directory.EnumerateFileSystemEntries(real).Any()); Directory.Delete(link, recursive: false);
});
HttpResponseMessage MediaReply(byte[] data, long total, long? from = null, string tag = "\"source-v1\"") {
    var response = new HttpResponseMessage(from is null ? HttpStatusCode.OK : HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data) };
    response.Content.Headers.ContentLength = from is null ? total : total - from; response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(tag);
    if (from is not null) response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from.Value, total - 1, total);
    return response;
}
await Test("Bộ tải nội bộ giữ đúng bytes/tên và không tạo cookie/source JSON", async () => {
    var job = OwnedJob("native-download"); job.DownloadFormatId = "web-1"; var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "native-state")); var path = Path.Combine(directory, "video.mp4");
    using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(MediaReply(awaitFile(), 100)))); var vault = new MemorySecrets();
    await new MediaDownload(http, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(awaitFile()) && !File.Exists(path + ".part") && await vault.ReadAsync("download-" + job.Id) is null);
    Assert(Directory.EnumerateFiles(directory).Select(Path.GetFileName).SequenceEqual(["video.mp4"]));
});
await Test("Tải gián đoạn nối đúng Range/ETag và hoàn tất một file", async () => {
    var job = OwnedJob("native-resume"); job.DownloadFormatId = "web-1"; var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "resume-state")); var path = Path.Combine(directory, "video.mp4"); var vault = new MemorySecrets(); var calls = 0;
    using (var first = new HttpClient(new ScriptedHandler(_ => { calls++; if (calls == 1) return Task.FromResult(MediaReply(awaitFile(), 100)); if (calls > 2) throw new HttpRequestException("Offline"); return Task.FromResult(MediaReply(awaitFile()[..40], 100)); })))
        await Throws<NeedsActionException>(() => new MediaDownload(first, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default));
    Assert(new FileInfo(path + ".part").Length == 40 && !File.Exists(path));
    using var second = new HttpClient(new ScriptedHandler(request => { Assert(request.Headers.Range!.Ranges.Single().From == 40 && request.Headers.GetValues("If-Range").Single() == "\"source-v1\""); return Task.FromResult(MediaReply(awaitFile()[40..], 100, 40)); }));
    await new MediaDownload(second, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default); Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(awaitFile()));
});
await Test("Tiến độ tải giữ 37,5% khi dừng, mở lại và nối đúng byte mà không đọc Douyin", async () => {
    var job = OwnedJob("progress-download"); job.Media = null; job.State = JobState.Paused; job.DownloadFormatId = "web-1"; job.DownloadExtension = "mp4";
    var temp = Path.Combine(runRoot, "progress-source", "temp"); var state = Path.Combine(runRoot, "progress-source", "download-state");
    var directory = DownloadPolicy.ReserveDirectory(job, state); var path = Path.Combine(directory, Rules.FileStem(job.Name) + ".mp4");
    var data = new byte[1024 * 1024]; for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);
    var vault = new MemorySecrets(); var db = new LocalStore(Path.Combine(runRoot, "progress-download-db"));
    using var cancel = new CancellationTokenSource();
    using (var first = new HttpClient(new ScriptedHandler(_ => Task.FromResult(MediaReply(data, data.Length)))))
        await Throws<OperationCanceledException>(() => new MediaDownload(first, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path,
            new DirectProgress<double>(p => { if (p >= 37.5) cancel.Cancel(); }), cancel.Token, () => db.SaveAsync(job)));
    var saved = (await db.ListAsync()).Single(); Assert(saved.Progress == 37.5 && saved.DisplayProgress == 37.5 && saved.DownloadedBytes == new FileInfo(path + ".part").Length);
    // Old versions stored 0%; recover from the existing private receipt and owned .part.
    saved.Progress = 0; saved.ProgressAction = JobActions.None; saved.DownloadedBytes = 0; saved.DownloadTotalBytes = 0; await db.SaveAsync(saved);
    var reader = new FakeVideoBrowser(new JsonObject()); var source = new DouyinSource("unused", temp, vault, reader);
    var fakes = new Fakes(); var coordinator = new JobCoordinator(db, source, fakes, fakes); await coordinator.InitializeAsync();
    saved = (await db.ListAsync()).Single(); Assert(saved.DisplayProgress == 37.5 && saved.State == JobState.Paused && reader.Reads == 0);
    using var second = new HttpClient(new ScriptedHandler(request => {
        var offset = request.Headers.Range!.Ranges.Single().From!.Value; Assert(offset == 393216);
        return Task.FromResult(MediaReply(data[(int)offset..], data.Length, offset));
    }));
    var reports = new List<double>();
    await new MediaDownload(second, vault).DownloadAsync(saved, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(reports.Add), default, () => db.SaveAsync(saved));
    Assert(reports[0] == 37.5 && reports[^1] == 100 && (await db.ListAsync()).Single().DisplayProgress == 100 && File.ReadAllBytes(path).SequenceEqual(data));
});
await Test("Tiến độ upload lưu byte đã xác nhận trước checkpoint, mở lại và tiếp tục từ 50%", async () => {
    var job = Job(); var path = Path.Combine(runRoot, "progress-upload.mp4"); await File.WriteAllBytesAsync(path, new byte[16 * 1024 * 1024]);
    job.Media = job.Media! with { Path = path, Length = new FileInfo(path).Length }; job.State = JobState.Paused; job.PendingActions = JobActions.UploadYouTube;
    job.UploadSessionKey = "upload-progress"; job.MediaSendStarted = true;
    var db = new LocalStore(Path.Combine(runRoot, "progress-upload-db")); using var cancel = new CancellationTokenSource();
    var queries = 0; var chunks = 0; long accepted = 0;
    var svc = await Services(new ScriptedHandler(request => {
        if (request.Method == HttpMethod.Put)
        {
            var range = request.Content!.Headers.ContentRange!;
            if (range.From is null) queries++;
            else { Assert(range.From == accepted); chunks++; accepted += range.To!.Value - range.From.Value + 1; }
            if (accepted == job.Media.Length) return Task.FromResult(JsonReply(JsonNode.Parse("""{"id":"progress-video"}""")!));
            var reply = new HttpResponseMessage((HttpStatusCode)308); if (accepted > 0) reply.Headers.TryAddWithoutValidation("Range", "bytes=0-" + (accepted - 1)); return Task.FromResult(reply);
        }
        Assert(request.Method == HttpMethod.Get);
        return Task.FromResult(JsonReply(JsonNode.Parse("""{"items":[{"id":"progress-video","snippet":{"channelId":"channel-id"},"status":{"uploadStatus":"uploaded","privacyStatus":"private","selfDeclaredMadeForKids":false,"madeForKids":false}}]}""")!));
    }));
    await svc.Secrets.WriteAsync(job.UploadSessionKey, "https://www.googleapis.com/upload/progress-fixture");
    await Throws<OperationCanceledException>(() => svc.YouTube.UploadAndVerifyAsync(job, async () => {
        await db.SaveAsync(job); if (job.UploadedBytes == job.Media.Length / 2) cancel.Cancel();
    }, new DirectProgress<double>(_ => {}), cancel.Token));
    var saved = (await db.ListAsync()).Single(); Assert(saved.Progress == 50 && saved.DisplayProgress == 50 && chunks == 1);
    saved.Progress = 0; saved.ProgressAction = JobActions.None; await db.SaveAsync(saved);
    var fakes = new Fakes(); var coordinator = new JobCoordinator(db, fakes, fakes, fakes); await coordinator.InitializeAsync();
    saved = (await db.ListAsync()).Single(); Assert(saved.DisplayProgress == 50 && saved.Progress == 50);
    var reports = new List<double>(); await svc.YouTube.UploadAndVerifyAsync(saved, () => db.SaveAsync(saved), new DirectProgress<double>(reports.Add), default);
    Assert(reports[0] == 50 && reports[^1] == 100 && chunks == 2 && queries == 2 && saved.YouTubeId == "progress-video" && (await db.ListAsync()).Single().Progress == 100);
});
await Test("Tiến độ truy vấn upload phản ánh offset mới và giữ nguyên khi tiền kiểm cần đăng nhập", async () => {
    var job = Job(); job.State = JobState.Paused; job.PendingActions = JobActions.UploadYouTube; job.UploadSessionKey = "upload-query"; job.MediaSendStarted = true; job.UploadedBytes = 20;
    var db = new LocalStore(Path.Combine(runRoot, "progress-query-db")); using var cancel = new CancellationTokenSource();
    var svc = await Services(new ScriptedHandler(request => {
        Assert(request.Method == HttpMethod.Put && request.Content!.Headers.ContentRange!.From is null);
        var reply = new HttpResponseMessage((HttpStatusCode)308); reply.Headers.TryAddWithoutValidation("Range", "bytes=0-40"); return Task.FromResult(reply);
    }));
    await svc.Secrets.WriteAsync(job.UploadSessionKey, "https://www.googleapis.com/upload/query-fixture");
    await Throws<OperationCanceledException>(() => svc.YouTube.UploadAndVerifyAsync(job, async () => { await db.SaveAsync(job); cancel.Cancel(); }, new DirectProgress<double>(_ => {}), cancel.Token));
    var saved = (await db.ListAsync()).Single(); Assert(saved.DisplayProgress == 41 && saved.Progress == 41);
    var fakes = new Fakes { FailUploadPreflight = true }; var coordinator = new JobCoordinator(db, fakes, fakes, fakes);
    await coordinator.ResumeAsync(saved.Id); await Finish(db, saved.Id); await coordinator.StopAsync();
    saved = (await db.ListAsync()).Single(); Assert(saved.State == JobState.NeedsAuth && saved.DisplayProgress == 41 && saved.Progress == 41 && fakes.Uploads == 0);
});
await Test("Tiến độ không nhận partial khác nguồn làm dữ liệu đã tải", async () => {
    var job = OwnedJob("progress-foreign"); job.Media = null; job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "progress-foreign-state")); var path = Path.Combine(directory, "video.mp4"); await File.WriteAllBytesAsync(path + ".part", new byte[50]);
    var secrets = new MemorySecrets(); await secrets.WriteAsync("download-" + job.Id, JsonSerializer.Serialize(new { SourceId = "other-source", FormatId = "web-1", Path = path, Validator = "\"v1\"", Length = 100 }));
    await MediaDownload.RestoreProgressAsync(job, path, secrets); Assert(job.ProgressAction == JobActions.None && job.DownloadedBytes == 0);
});
await Test("Tiến độ video đã có ID là upload 100% dù file local không còn, không gọi dịch vụ lúc mở", async () => {
    var job = Job(); job.Media = null; job.YouTubeId = "known-video"; job.State = JobState.Paused; job.PendingActions = JobActions.UploadYouTube;
    var db = new LocalStore(Path.Combine(runRoot, "progress-known-id")); await db.SaveAsync(job);
    var fakes = new Fakes(); var coordinator = new JobCoordinator(db, fakes, fakes, fakes); await coordinator.InitializeAsync();
    var saved = (await db.ListAsync()).Single(); Assert(saved.DisplayProgress == 100 && saved.ProgressAction == JobActions.UploadYouTube && saved.State == JobState.Paused && fakes.Checks == 0 && fakes.Uploads == 0);
});
await Test("Tiến độ checkpoint tải có giới hạn 5 giây, không ghi SQLite theo từng byte", async () => {
    var job = OwnedJob("progress-checkpoint"); job.Media = null; job.DownloadFormatId = "web-1"; var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "progress-checkpoint-state")); var path = Path.Combine(directory, "video.mp4");
    using var http = new HttpClient(new ScriptedHandler(request => {
        if (request.Method == HttpMethod.Head) return Task.FromResult(MediaReply(awaitFile(), 100));
        var reply = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new PacedReadStream(awaitFile(), TimeSpan.FromMilliseconds(600))) };
        reply.Content.Headers.ContentLength = 100; reply.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"source-v1\""); return Task.FromResult(reply);
    }));
    var checkpoints = new List<double>();
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default, () => { checkpoints.Add(job.Progress); return Task.CompletedTask; });
    Assert(checkpoints.Any(p => p > 0 && p < 100) && checkpoints[^1] == 100 && checkpoints.Count < 8);
});
await Test("Partial không có validator được tải lại, không nối bytes cũ", async () => {
    var job = OwnedJob("native-old-partial"); job.DownloadFormatId = "web-1"; var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "old-partial-state")); var path = Path.Combine(directory, "video.mp4"); await File.WriteAllTextAsync(path + ".part", "old media bytes");
    using var http = new HttpClient(new ScriptedHandler(request => { if (request.Headers.Range is not null) Assert(request.Headers.Range.Ranges.Single().From == 0); return Task.FromResult(MediaReply(awaitFile(), 100)); }));
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default); Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(awaitFile()));
});
await Test("Range sai bị từ chối trước khi ghi partial", async () => {
    var job = OwnedJob("native-invalid-range"); job.DownloadFormatId = "web-1"; var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "range-state")); var path = Path.Combine(directory, "video.mp4");
    using var http = new HttpClient(new ScriptedHandler(request => Task.FromResult(request.Headers.Range is null ? MediaReply(awaitFile(), 100) : MediaReply(awaitFile()[5..], 100, 5))));
    await Throws<NeedsActionException>(() => new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default)); Assert(!File.Exists(path) && !File.Exists(path + ".part"));
});
await Test("DNS trả IP nội bộ bị chặn ngay trước socket, kể cả miền hợp lệ", async () => {
    var calls = 0; using var http = new HttpClient(DouyinHttp.Handler(resolve: (_, _) => { calls++; return Task.FromResult(new[] { IPAddress.Loopback }); }));
    await Throws<HttpRequestException>(async () => { using var response = await DouyinHttp.GetAsync(http, "https://v3.douyinvod.com/a", null, default); }); Assert(calls == 1);
});
await Test("Range bridge dùng nguồn cố định, chặn route lạ và chuyển đúng bytes", async () => {
    var calls = 0; using var upstream = new HttpClient(new ScriptedHandler(request => {
        calls++; Assert(request.RequestUri!.Host == "v3.douyinvod.com" && request.Headers.Range!.Ranges.Single().From == 5);
        return Task.FromResult(MediaReply(awaitFile()[5..], 100, 5));
    }));
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    await using var bridge = new MediaRangeBridge("https://v3.douyinvod.com/fixed", "https://www.douyin.com/", limit.Token, upstream);
    using var local = new HttpClient(new HttpClientHandler { UseProxy = false });
    using var invalid = await local.GetAsync(bridge.Url + "/https://evil.test", limit.Token); Assert(invalid.StatusCode == HttpStatusCode.Forbidden && calls == 0);
    using var request = new HttpRequestMessage(HttpMethod.Get, bridge.Url); request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(5, null);
    using var response = await local.SendAsync(request, limit.Token); Assert(response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange!.From == 5 && (await response.Content.ReadAsByteArrayAsync(limit.Token)).SequenceEqual(awaitFile()[5..])); Assert(calls == 1);
});
await Test("Migration lỗi JSON rollback toàn bộ, giữ schema và plaintext gốc", async () => {
    var folder = Path.Combine(runRoot, "migration-rollback"); Directory.CreateDirectory(folder); var job = Job();
    using var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(folder, "jobs.db") + ";Pooling=False"); connection.Open();
    using var command = connection.CreateCommand(); command.CommandText = "CREATE TABLE jobs(id TEXT PRIMARY KEY, requestId TEXT NOT NULL UNIQUE, created TEXT NOT NULL, payload TEXT NOT NULL); CREATE TABLE settings(id INTEGER PRIMARY KEY, payload TEXT NOT NULL); PRAGMA user_version=4; INSERT INTO jobs VALUES($id,$request,$created,$payload); INSERT INTO settings VALUES(1,'not-json');";
    command.Parameters.AddWithValue("$id", job.Id); command.Parameters.AddWithValue("$request", job.RequestId); command.Parameters.AddWithValue("$created", job.Created.ToString("O")); var original = JsonSerializer.Serialize(job, LocalStore.Json); command.Parameters.AddWithValue("$payload", original); command.ExecuteNonQuery();
    await Throws<JsonException>(() => Sync(() => _ = new LocalStore(folder)));
    command.CommandText = "SELECT payload FROM jobs"; Assert((string)command.ExecuteScalar()! == original);
    command.CommandText = "PRAGMA user_version"; Assert(Convert.ToInt32(command.ExecuteScalar()) == 4);
});
await Test("Range bridge phục vụ lượt seek khi kết nối đầu vẫn đang đọc", async () => {
    using var upstream = new HttpClient(new ScriptedHandler(request => {
        if (request.Headers.Range is not null) return Task.FromResult(MediaReply(awaitFile()[5..], 100, 5));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new WaitingReadStream()) }; response.Content.Headers.ContentLength = 100; return Task.FromResult(response);
    }));
    using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await using var bridge = new MediaRangeBridge("https://v3.douyinvod.com/fixed", "https://www.douyin.com/", limit.Token, upstream);
    using var local = new HttpClient(new HttpClientHandler { UseProxy = false });
    using var first = await local.GetAsync(bridge.Url, HttpCompletionOption.ResponseHeadersRead, limit.Token);
    using var request = new HttpRequestMessage(HttpMethod.Get, bridge.Url); request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(5, null);
    using var second = await local.SendAsync(request, limit.Token); Assert((await second.Content.ReadAsByteArrayAsync(limit.Token)).SequenceEqual(awaitFile()[5..]));
});
await Test("CDN không hỗ trợ HEAD dùng GET headers rồi tải đúng Range", async () => {
    var job = OwnedJob("native-head-fallback"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "head-fallback-state")); var path = Path.Combine(directory, "video.mp4");
    var methods = new List<string>();
    using var http = new HttpClient(new ScriptedHandler(request => {
        methods.Add(request.Method.Method);
        if (request.Method == HttpMethod.Head) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.MethodNotAllowed));
        return Task.FromResult(request.Headers.Range is null ? MediaReply(awaitFile(), 100) : MediaReply(awaitFile(), 100, 0));
    }));
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert(methods.SequenceEqual(new[] { "HEAD", "GET", "GET" }) && (await File.ReadAllBytesAsync(path)).SequenceEqual(awaitFile()));
});
await Test("Tải vẫn tiếp tục khi tổng thời gian vượt timeout nhưng dữ liệu liên tục về", async () => {
    var job = OwnedJob("native-slow-transfer"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "slow-transfer-state")); var path = Path.Combine(directory, "video.mp4");
    using var http = new HttpClient(new ScriptedHandler(request => {
        if (request.Method == HttpMethod.Head) return Task.FromResult(MediaReply(awaitFile(), 100));
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new PacedReadStream(awaitFile(), TimeSpan.FromMilliseconds(60))) };
        response.Content.Headers.ContentLength = 100;
        response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"source-v1\"");
        return Task.FromResult(response);
    }));
    await new MediaDownload(http, new MemorySecrets(), TimeSpan.FromMilliseconds(200)).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(awaitFile()));
});
await Test("Video lớn chia đoạn tối đa 32 MiB; CDN bỏ ETag ở 206 vẫn dùng If-Range", async () => {
    var data = new byte[32 * 1024 * 1024 + 101]; for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 251);
    var job = OwnedJob("native-chunks"); job.DownloadFormatId = "web-1"; var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "chunks-state")); var path = Path.Combine(directory, "video.mp4"); var chunks = 0;
    using var http = new HttpClient(new ScriptedHandler(request => {
        if (request.Headers.Range is null) return Task.FromResult(MediaReply(data, data.Length));
        Assert(request.Headers.GetValues("If-Range").Single() == "\"source-v1\""); var range = request.Headers.Range.Ranges.Single();
        var from = (int)range.From!.Value; var to = (int)Math.Min(range.To!.Value, data.Length - 1); Assert(to - from + 1 <= 32 * 1024 * 1024); Interlocked.Increment(ref chunks);
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data[from..(to + 1)]) }; response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, data.Length); return Task.FromResult(response);
    }));
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert(chunks == 9 && (await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
await Test("CDN đổi validator giữa các đoạn bị chặn trước khi nối bytes mới", async () => {
    var data = new byte[32 * 1024 * 1024 + 101]; var job = OwnedJob("native-changed-validator"); job.DownloadFormatId = "web-1"; var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "changed-validator-state")); var path = Path.Combine(directory, "video.mp4");
    using var http = new HttpClient(new ScriptedHandler(request => {
        if (request.Headers.Range is null) return Task.FromResult(MediaReply(data, data.Length));
        var range = request.Headers.Range.Ranges.Single(); var from = (int)range.From!.Value; var to = (int)Math.Min(range.To!.Value, data.Length - 1);
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data[from..(to + 1)]) }; response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, data.Length); response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(from == 0 ? "\"source-v1\"" : "\"source-v2\""); return Task.FromResult(response);
    }));
    await Throws<NeedsActionException>(() => new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default)); Assert(!File.Exists(path) && new FileInfo(path + ".part").Length == 4 * 1024 * 1024);
});
byte[] AccelerationData() { var data = new byte[21 * 1024 * 1024 + 101]; for (var i = 0; i < data.Length; i++) data[i] = (byte)(i % 251); return data; }
HttpResponseMessage AccelerationReply(HttpRequestMessage request, byte[] data) {
    if (request.Method == HttpMethod.Head) return MediaReply([], data.Length);
    var range = request.Headers.Range!.Ranges.Single(); var from = checked((int)range.From!.Value); var to = (int)Math.Min(range.To!.Value, data.Length - 1);
    var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(data[from..(to + 1)]) };
    response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(from, to, data.Length);
    response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"source-v1\""); return response;
}
await Test("Tải song song dùng bốn kết nối tái sử dụng được, ghi đúng vị trí và giữ nguyên toàn bộ bytes", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-bytes"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-bytes-state")); var path = Path.Combine(directory, "video.mp4");
    var active = 0; var peak = 0; var getCount = 0; var reports = new List<long>();
    using var http = new HttpClient(new ScriptedHandler(async request => {
        if (request.Method == HttpMethod.Head) return AccelerationReply(request, data);
        Assert(request.Headers.ConnectionClose != true && request.Headers.GetValues("If-Range").Single() == "\"source-v1\"");
        var current = Interlocked.Increment(ref active); peak = Math.Max(peak, current); Interlocked.Increment(ref getCount);
        try { var start = request.Headers.Range!.Ranges.Single().From!.Value; await Task.Delay(start == 4L * 1024 * 1024 ? 80 : 20); return AccelerationReply(request, data); }
        finally { Interlocked.Decrement(ref active); }
    }));
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path,
        new DirectProgress<double>(_ => reports.Add(job.DownloadedBytes)), default);
    Assert(peak == 4 && getCount == 6 && reports.SequenceEqual(reports.Order()) && reports[^1] == data.Length);
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(data) && !File.Exists(path + ".part"));
});
await Test("Tải song song dừng giữa phần đang nhận, mở lại giữ cả vùng byte sau khoảng trống", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-pause"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-pause-state")); var path = Path.Combine(directory, "video.mp4"); var vault = new MemorySecrets();
    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var savedBytes = 0L;
    using (var first = new HttpClient(new TokenScriptedHandler(async (request, ct) => {
        if (request.Headers.Range?.Ranges.Single().From == 4L * 1024 * 1024) await Task.Delay(Timeout.Infinite, ct);
        return AccelerationReply(request, data);
    })))
        await Throws<OperationCanceledException>(() => new MediaDownload(first, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path,
            new DirectProgress<double>(_ => { if (job.DownloadedBytes > 4L * 1024 * 1024) cancel.Cancel(); }), cancel.Token,
            () => { savedBytes = job.DownloadedBytes; return Task.CompletedTask; }));
    Assert(savedBytes > 4L * 1024 * 1024 && new FileInfo(path + ".part").Length > savedBytes && !File.Exists(path));
    var receipt = JsonNode.Parse((await vault.ReadAsync("download-" + job.Id))!)!;
    Assert(receipt["Validator"]!.ToString() == ""); // Older builds cannot interpret a file with gaps as a prefix.
    var savedRanges = receipt["Segments"]!["Ranges"]!.AsArray().Select(r => (Start: r!["Start"]!.GetValue<long>(), End: r["End"]!.GetValue<long>())).ToArray();
    job.DownloadedBytes = 0; await MediaDownload.RestoreProgressAsync(job, path, vault); Assert(job.DownloadedBytes == savedBytes);
    using var second = new HttpClient(new ScriptedHandler(request => {
        Assert(request.Method != HttpMethod.Head);
        var range = request.Headers.Range!.Ranges.Single();
        Assert(savedRanges.All(r => range.To < r.Start || range.From >= r.End));
        return Task.FromResult(AccelerationReply(request, data));
    }));
    await new MediaDownload(second, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(data) && await vault.ReadAsync("download-" + job.Id) is null);
});
await Test("Tải song song CDN bỏ Range thì quay về một luồng, không nối trùng dữ liệu", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-no-range"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-no-range-state")); var path = Path.Combine(directory, "video.mp4"); var ignored = false;
    using var http = new HttpClient(new ScriptedHandler(request => {
        if (request.Method == HttpMethod.Head) return Task.FromResult(AccelerationReply(request, data));
        if (request.Headers.Range!.Ranges.Single().From > 0 || ignored) { ignored = true; return Task.FromResult(MediaReply(data, data.Length)); }
        return Task.FromResult(AccelerationReply(request, data));
    }));
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert(ignored && (await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
foreach (var mismatch in new[] { "tag", "range", "total", "length" })
await Test("Tải song song chặn phản hồi sai " + mismatch + " trước khi ghi phần lỗi", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-mismatch-" + mismatch); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-mismatch-state-" + mismatch)); var path = Path.Combine(directory, "video.mp4");
    using var http = new HttpClient(new ScriptedHandler(request => {
        var reply = AccelerationReply(request, data);
        if (request.Headers.Range?.Ranges.Single().From > 0)
        {
            var range = reply.Content.Headers.ContentRange!;
            if (mismatch == "tag") reply.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"source-v2\"");
            if (mismatch == "range") reply.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(range.From!.Value + 1, range.To!.Value, range.Length!.Value);
            if (mismatch == "total") reply.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(range.From!.Value, range.To!.Value, range.Length!.Value + 1);
            if (mismatch == "length") reply.Content.Headers.ContentLength = 1;
        }
        return Task.FromResult(reply);
    }));
    await Throws<NeedsActionException>(() => new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default));
    Assert(!File.Exists(path) && new FileInfo(path + ".part").Length == 4L * 1024 * 1024);
});
await Test("Tải song song 429 không bị lỗi hủy của kết nối khác che mất", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-rate-limit"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-rate-limit-state")); var path = Path.Combine(directory, "video.mp4");
    var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var started = 0;
    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var http = new HttpClient(new TokenScriptedHandler(async (request, ct) => {
        var start = request.Headers.Range?.Ranges.Single().From ?? 0;
        if (request.Method == HttpMethod.Head || start == 0) return AccelerationReply(request, data);
        if (Interlocked.Increment(ref started) == 4) barrier.TrySetResult();
        await barrier.Task.WaitAsync(ct);
        if (start == 8L * 1024 * 1024) { var reply = new HttpResponseMessage(HttpStatusCode.TooManyRequests); reply.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(17)); return reply; }
        await Task.Delay(Timeout.Infinite, ct); throw new Exception("Unreachable");
    }));
    try { await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), budget.Token); throw new Exception("Expected 429"); }
    catch (RetryLaterException ex) { Assert(ex.DependencyKey == "douyin-cdn" && ex.RetryAt > DateTimeOffset.UtcNow.AddSeconds(10)); }
    Assert(started == 4 && new FileInfo(path + ".part").Length == 4L * 1024 * 1024);
});
await Test("Tải song song lỗi mạng giảm từ bốn xuống hai kết nối và vẫn đúng file", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-pressure"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-pressure-state")); var path = Path.Combine(directory, "video.mp4");
    var phase = 0; var active = 0; var peakAfterRetry = 0;
    using var http = new HttpClient(new ScriptedHandler(async request => {
        if (request.Method == HttpMethod.Head || request.Headers.Range!.Ranges.Single().From == 0) return AccelerationReply(request, data);
        var start = request.Headers.Range!.Ranges.Single().From!.Value;
        if (phase == 0) { await Task.Delay(20); if (start == 4L * 1024 * 1024) { phase = 1; throw new HttpRequestException("Connection reset"); } return AccelerationReply(request, data); }
        var current = Interlocked.Increment(ref active); peakAfterRetry = Math.Max(peakAfterRetry, current);
        try { await Task.Delay(30); return AccelerationReply(request, data); } finally { Interlocked.Decrement(ref active); }
    }));
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert(phase == 1 && peakAfterRetry == 2 && (await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
await Test("Tải song song If-Range mạnh cho phép CDN bỏ ETag và đổi Last-Modified", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-no-etag"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-no-etag-state")); var path = Path.Combine(directory, "video.mp4");
    using var http = new HttpClient(new ScriptedHandler(request => {
        var reply = AccelerationReply(request, data);
        if (request.Method != HttpMethod.Head) { reply.Headers.ETag = null; reply.Content.Headers.LastModified = DateTimeOffset.UtcNow; }
        return Task.FromResult(reply);
    }));
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
await Test("Tải song song kết nối chậm được mở lại tối đa hai lần, nối đúng byte", async () => {
    var data = awaitFile(); var job = OwnedJob("parallel-slow-reconnect"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-slow-reconnect-state")); var path = Path.Combine(directory, "video.mp4"); var offsets = new List<long>();
    using var http = new HttpClient(new ScriptedHandler(request => {
        if (request.Method == HttpMethod.Head) return Task.FromResult(MediaReply([], data.Length));
        var start = request.Headers.Range!.Ranges.Single().From!.Value; offsets.Add(start); Assert(request.Headers.ConnectionClose == (offsets.Count == 1 ? (bool?)null : true));
        var reply = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new StreamContent(new PacedReadStream(data[(int)start..], TimeSpan.FromMilliseconds(20))) };
        reply.Content.Headers.ContentLength = data.Length - start; reply.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, data.Length - 1, data.Length);
        reply.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"source-v1\""); return Task.FromResult(reply);
    }));
    await new MediaDownload(http, new MemorySecrets(), slowTransferWindow: TimeSpan.FromMilliseconds(1)).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert(offsets.SequenceEqual(new long[] { 0, 10, 20 }) && (await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
await Test("Tải song song phần chậm mở lại tối đa hai lần, giữ bytes đã ghi trong từng phần", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-slow-batch"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-slow-batch-state")); var path = Path.Combine(directory, "video.mp4");
    var starts = new List<long>();
    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var http = new HttpClient(new ScriptedHandler(request => {
        if (request.Method == HttpMethod.Head || request.Headers.Range!.Ranges.Single().From == 0) return Task.FromResult(AccelerationReply(request, data));
        var range = request.Headers.Range!.Ranges.Single(); var start = (int)range.From!.Value;
        if (start < 8 * 1024 * 1024) starts.Add(start);
        var reply = AccelerationReply(request, data);
        if (starts.Count <= 2)
        {
            var to = (int)Math.Min(range.To!.Value, data.Length - 1);
            reply.Content.Dispose(); reply.Content = new StreamContent(new PacedReadStream(data[start..(to + 1)], TimeSpan.FromMilliseconds(20)));
            reply.Content.Headers.ContentLength = to - start + 1; reply.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(start, to, data.Length);
        }
        return Task.FromResult(reply);
    }));
    await new MediaDownload(http, new MemorySecrets(), slowTransferWindow: TimeSpan.FromMilliseconds(100)).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), budget.Token);
    // A sibling can trigger reconnect before this worker writes its first byte;
    // its next request may legitimately start at the same offset.
    Assert(starts.Count == 3 && starts.SequenceEqual(starts.Order()), "Slow request offsets: " + string.Join(",", starts));
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
await Test("Tải song song thiếu ETag mạnh vẫn tải tuần tự và kiểm tra Last-Modified", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-date-only"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-date-only-state")); var path = Path.Combine(directory, "video.mp4"); var gets = 0;
    using var http = new HttpClient(new ScriptedHandler(request => {
        var reply = AccelerationReply(request, data); reply.Headers.ETag = null; reply.Content.Headers.LastModified = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
        if (request.Method != HttpMethod.Head) { gets++; Assert(request.Headers.Range!.Ranges.Single().From == 0); }
        return Task.FromResult(reply);
    }));
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert(gets == 1 && (await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
await Test("Tải song song luồng rảnh nhận phần mới khi một phần trước còn chờ", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-pipeline"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-pipeline-state")); var path = Path.Combine(directory, "video.mp4");
    var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var advanced = false;
    using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    using var http = new HttpClient(new TokenScriptedHandler(async (request, ct) => {
        var start = request.Headers.Range?.Ranges.Single().From ?? 0;
        if (request.Method != HttpMethod.Head && start == 4L * 1024 * 1024) await release.Task.WaitAsync(ct);
        if (start >= 20L * 1024 * 1024) { advanced = true; release.TrySetResult(); }
        return AccelerationReply(request, data);
    }));
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), budget.Token);
    Assert(advanced && (await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
await Test("Tải song song mở lại chỉ tin bản đồ checkpoint, không tin độ dài hoặc byte chưa checkpoint", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-crash"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-crash-state")); var path = Path.Combine(directory, "video.mp4"); var vault = new MemorySecrets();
    var saved = new[] { (Start: 0L, End: 4L * 1024 * 1024), (Start: 8L * 1024 * 1024, End: 8L * 1024 * 1024 + 131072) };
    var partial = Enumerable.Repeat((byte)255, data.Length).ToArray();
    foreach (var range in saved) Array.Copy(data, range.Start, partial, range.Start, range.End - range.Start);
    await File.WriteAllBytesAsync(path + ".part", partial);
    await vault.WriteAsync("download-" + job.Id, JsonSerializer.Serialize(new { SourceId = job.Source!.Id, FormatId = job.DownloadFormatId, Path = path, Validator = "", Length = data.Length,
        Segments = new { Validator = "\"source-v1\"", Version = 1, Ranges = saved.Select(r => new { r.Start, r.End }).ToArray() } }));
    await MediaDownload.RestoreProgressAsync(job, path, vault); Assert(job.DownloadedBytes == saved.Sum(r => r.End - r.Start) && job.Progress < 100);
    using var http = new HttpClient(new ScriptedHandler(request => {
        Assert(request.Method != HttpMethod.Head); var range = request.Headers.Range!.Ranges.Single();
        Assert(saved.All(r => range.To < r.Start || range.From >= r.End));
        return Task.FromResult(AccelerationReply(request, data));
    }));
    await new MediaDownload(http, vault).DownloadAsync(job, "https://v3.douyinvod.com/refreshed-url.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
foreach (var invalid in new[] { "overlap", "beyond-file", "version", "weak-tag", "null-range" })
await Test("Tải song song từ chối bản đồ checkpoint lỗi " + invalid, async () => {
    var job = OwnedJob("parallel-map-" + invalid); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-map-state-" + invalid)); var path = Path.Combine(directory, "video.mp4"); var vault = new MemorySecrets();
    await File.WriteAllBytesAsync(path + ".part", new byte[50]);
    var receipt = JsonNode.Parse(JsonSerializer.Serialize(new { SourceId = job.Source!.Id, FormatId = job.DownloadFormatId, Path = path, Validator = "", Length = 100,
        Segments = new { Validator = "\"source-v1\"", Version = 1, Ranges = new[] { new { Start = 0, End = 40 } } } }))!;
    var map = receipt["Segments"]!;
    if (invalid == "overlap") map["Ranges"]!.AsArray().Add(new JsonObject { ["Start"] = 20, ["End"] = 50 });
    if (invalid == "beyond-file") map["Ranges"]![0]!["End"] = 60;
    if (invalid == "version") map["Version"] = 2;
    if (invalid == "weak-tag") map["Validator"] = "W/\"source-v1\"";
    if (invalid == "null-range") map["Ranges"]!.AsArray().Add((JsonNode?)null);
    await vault.WriteAsync("download-" + job.Id, receipt.ToJsonString());
    await MediaDownload.RestoreProgressAsync(job, path, vault); Assert(job.DownloadedBytes == 0);
    using var http = new HttpClient(new ScriptedHandler(request => {
        if (request.Method != HttpMethod.Head) Assert(request.Headers.Range!.Ranges.Single().From == 0);
        return Task.FromResult(AccelerationReply(request, awaitFile()));
    }));
    await new MediaDownload(http, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(awaitFile()));
});
await Test("Tải song song dừng ở byte cuối, mở lại hoàn tất file mà không tải lại", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-last-byte"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-last-byte-state")); var path = Path.Combine(directory, "video.mp4"); var vault = new MemorySecrets();
    using var cancel = new CancellationTokenSource();
    using (var first = new HttpClient(new ScriptedHandler(request => Task.FromResult(AccelerationReply(request, data)))))
        await Throws<OperationCanceledException>(() => new MediaDownload(first, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path,
            new DirectProgress<double>(_ => { if (job.DownloadedBytes == data.Length) cancel.Cancel(); }), cancel.Token));
    Assert(!File.Exists(path));
    using var second = new HttpClient(new ScriptedHandler(_ => throw new Exception("Complete ranges must not be downloaded again")));
    await new MediaDownload(second, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
await Test("Tải song song giữ các phần khác khi một phản hồi trả thừa byte", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-overrun"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-overrun-state")); var path = Path.Combine(directory, "video.mp4"); var vault = new MemorySecrets();
    using var http = new HttpClient(new ScriptedHandler(request => {
        var reply = AccelerationReply(request, data);
        if (request.Headers.Range?.Ranges.Single().From == 4L * 1024 * 1024)
        {
            var range = reply.Content.Headers.ContentRange!;
            reply.Content.Dispose(); reply.Content = new StreamContent(new MemoryStream(new byte[4 * 1024 * 1024 + 1]));
            reply.Content.Headers.ContentRange = range;
        }
        return Task.FromResult(reply);
    }));
    await Throws<NeedsActionException>(() => new MediaDownload(http, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default));
    Assert(!File.Exists(path));
    var receipt = JsonNode.Parse((await vault.ReadAsync("download-" + job.Id))!)!;
    Assert(receipt["Segments"]!["Ranges"]!.AsArray().All(r => r!["End"]!.GetValue<long>() <= 4L * 1024 * 1024 || r["Start"]!.GetValue<long>() >= 8L * 1024 * 1024));
    using var retry = new HttpClient(new ScriptedHandler(request => Task.FromResult(AccelerationReply(request, data))));
    await new MediaDownload(retry, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
await Test("Tải song song lỗi mạng giữa phần giữ byte đã ghi, lần thử lại chỉ xin phần thiếu", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-mid-reset"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-mid-reset-state")); var path = Path.Combine(directory, "video.mp4");
    var starts = new System.Collections.Concurrent.ConcurrentQueue<long>(); var reset = 0;
    using var http = new HttpClient(new ScriptedHandler(request => {
        var reply = AccelerationReply(request, data);
        if (request.Method == HttpMethod.Head) return Task.FromResult(reply);
        var range = request.Headers.Range!.Ranges.Single(); var start = range.From!.Value; starts.Enqueue(start);
        if (start == 4L * 1024 * 1024 && Interlocked.Increment(ref reset) == 1)
        {
            var contentRange = reply.Content.Headers.ContentRange!;
            reply.Content.Dispose(); reply.Content = new StreamContent(new ResetReadStream(data[(int)start..((int)range.To!.Value + 1)]));
            reply.Content.Headers.ContentLength = contentRange.To - contentRange.From + 1; reply.Content.Headers.ContentRange = contentRange;
        }
        return Task.FromResult(reply);
    }));
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert(starts.Count(s => s == 4L * 1024 * 1024) == 1 && starts.Contains(4L * 1024 * 1024 + 131072));
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
await Test("Tải song song giữ giới hạn hai kết nối sau lỗi ở phần xác nhận đầu", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-probe-reset"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-probe-reset-state")); var path = Path.Combine(directory, "video.mp4");
    var requests = 0; var active = 0; var peak = 0;
    using var http = new HttpClient(new ScriptedHandler(async request => {
        if (request.Method == HttpMethod.Head) return AccelerationReply(request, data);
        if (Interlocked.Increment(ref requests) == 1) throw new HttpRequestException("First connection reset");
        var count = Interlocked.Increment(ref active); peak = Math.Max(peak, count);
        try { await Task.Delay(20); return AccelerationReply(request, data); }
        finally { Interlocked.Decrement(ref active); }
    }));
    await new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default);
    Assert(peak == 2 && (await File.ReadAllBytesAsync(path)).SequenceEqual(data));
});
await Test("Tải song song checkpoint định kỳ ghi dữ liệu và receipt trước khi lưu tiến độ", async () => {
    var data = AccelerationData(); var job = OwnedJob("parallel-durable"); job.DownloadFormatId = "web-1";
    var directory = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "parallel-durable-state")); var path = Path.Combine(directory, "video.mp4");
    var vault = new DpapiSecretStore(Path.Combine(runRoot, "parallel-durable-secrets"));
    using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(12)); var periodic = false; var checkpoints = 0;
    using var http = new HttpClient(new ScriptedHandler(request => {
        var reply = AccelerationReply(request, data);
        if (request.Headers.Range?.Ranges.Single().From > 0)
        {
            var range = reply.Content.Headers.ContentRange!;
            reply.Content.Dispose(); reply.Content = new StreamContent(new ThrottledReadStream(data[(int)range.From!.Value..((int)range.To!.Value + 1)], TimeSpan.FromMilliseconds(180)));
            reply.Content.Headers.ContentLength = range.To - range.From + 1; reply.Content.Headers.ContentRange = range;
        }
        return Task.FromResult(reply);
    }));
    await Throws<OperationCanceledException>(() => new MediaDownload(http, vault).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), cancel.Token, async () => {
        checkpoints++;
        if (job.DownloadedBytes <= 4L * 1024 * 1024) return;
        var receipt = JsonNode.Parse((await vault.ReadAsync("download-" + job.Id))!)!;
        var saved = receipt["Segments"]!["Ranges"]!.AsArray().Sum(r => r!["End"]!.GetValue<long>() - r["Start"]!.GetValue<long>());
        Assert(saved == job.DownloadedBytes);
        periodic = true; cancel.Cancel();
    }));
    Assert(periodic && checkpoints < 8);
    var savedBytes = job.DownloadedBytes; job.DownloadedBytes = 0;
    await MediaDownload.RestoreProgressAsync(job, path, vault); Assert(job.DownloadedBytes == savedBytes);
});
var diagnosticIndex = Array.IndexOf(args, "--diagnose-quality");

await Test("Duration JSON không cần format/bìa; kiểm tra ID, kiểu và ngưỡng ms", () => Sync(() => {
    var src = new SourceVideo("7659702794928147739", "https://www.douyin.com/video/7659702794928147739");
    foreach (var ms in new[] { 240000L, 240001L, 300000L, 300001L, 2439215L })
    {
        var node = JsonNode.Parse($"{{\"aweme_id\":\"{src.Id}\",\"video\":{{\"duration\":{ms}}}}}")!;
        var metadata = DouyinWebMetadata.ParseDuration(node, src, "fixture"); Assert(metadata?.DurationMilliseconds == ms);
        Assert(Rules.VideoTypeMilliseconds(ms) == (ms <= 300000 ? "Short" : "Long"));
    }
    foreach (var value in new[] { "0", "-1", "null", "\"240000\"", "true", "1.5", "1e100" }) Assert(DouyinWebMetadata.ParseDuration(JsonNode.Parse($"{{\"aweme_id\":\"{src.Id}\",\"video\":{{\"duration\":{value}}}}}"), src, "fixture") is null);
    Assert(DouyinWebMetadata.ParseDuration(JsonNode.Parse("{\"aweme_id\":\"other\",\"video\":{\"duration\":240000}}"), src, "fixture") is null);
}));
foreach (var actions in new[] { JobActions.Download, JobActions.WriteSheet, JobActions.Download | JobActions.WriteSheet, JobActions.Download | JobActions.UploadYouTube, JobActions.All })
await Test("Chạy đúng tổ hợp " + actions, async () => {
    var db = new LocalStore(Path.Combine(runRoot, "combination-" + (int)actions)); var f = new WorkflowFakes(Path.Combine(runRoot, "files-" + (int)actions)); var c = new JobCoordinator(db, f, f, f);
    var job = Job(); job.Actions = actions; job.Media = null; job.Metadata = null; job.Source = null; job.DownloadDirectory = null;
    if (!actions.HasFlag(JobActions.WriteSheet)) { job.Sheet = null; job.Niche = ""; }
    if (!actions.HasFlag(JobActions.UploadYouTube)) job.Channel = null;
    if (!actions.HasFlag(JobActions.Download)) job.Folder = "";
    await c.SubmitAsync(job); await FinishAutomatically(db, job.Id); await c.StopAsync(); var done = (await db.ListAsync()).Single();
    Assert(done.State == JobState.Complete && done.Work == JobActions.None, done.Message);
    Assert(f.Downloads == (actions.HasFlag(JobActions.Download) ? 1 : 0) && f.Writes == (actions.HasFlag(JobActions.WriteSheet) ? 1 : 0) && f.Uploads == (actions.HasFlag(JobActions.UploadYouTube) ? 1 : 0));
    Assert(f.MetadataReads == (actions.HasFlag(JobActions.WriteSheet) ? 1 : 0));
    Assert(f.SheetChecks == f.Writes && f.ChannelChecks == f.Uploads);
    if (actions == JobActions.WriteSheet) Assert(done.Media is null && done.DownloadDirectory is null && f.Validations == 0 && !Directory.Exists(f.Root));
});
await Test("Thiếu/sai duration không tải file hay ghi bảng; retry metadata", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "duration-retry")); var f = new WorkflowFakes(Path.Combine(runRoot, "no-duration-files")) { FailMetadata = true }; var c = new JobCoordinator(db, f, f, f);
    var job = Job(); job.Actions = JobActions.WriteSheet; job.Media = null; job.Metadata = null; job.Folder = "";
    await c.SubmitAsync(job); await Finish(db, job.Id); Assert(f.Writes == 0 && f.Downloads == 0 && !Directory.Exists(f.Root));
    f.FailMetadata = false; await c.ResumeAsync(job.Id); await FinishAutomatically(db, job.Id); await c.StopAsync(); Assert(f.Writes == 1 && f.MetadataReads == 2);
});
await Test("Đăng đơn không hợp lệ; core bắt buộc tải", () => Throws<ArgumentException>(() => Sync(() => { var job = Job(); job.Actions = JobActions.UploadYouTube; Rules.Validate(job); })));
await Test("Bổ sung Bảng/YouTube giữ file và không làm lại kết quả; xóa và làm lại từng phần", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "add-delete-cycle")); var f = new WorkflowFakes(Path.Combine(runRoot, "cycle-files")); var c = new JobCoordinator(db, f, f, f);
    var job = Job(); job.Actions = JobActions.Download; job.Media = null; job.Metadata = null; await c.SubmitAsync(job); await FinishAutomatically(db, job.Id);
    var settings = new AppSettings { VideoFolder = job.Folder, Sheet = job.Sheet, Channel = job.Channel };
    var first = (await db.ListAsync()).Single(); Assert(first.Addable == (JobActions.WriteSheet | JobActions.UploadYouTube));
    await c.AddActionsAsync(job.Id, JobActions.WriteSheet, settings, job.Niche); await FinishAutomatically(db, job.Id);
    await c.AddActionsAsync(job.Id, JobActions.UploadYouTube, settings, job.Niche); await FinishAutomatically(db, job.Id);
    Assert(f.Downloads == 1 && f.Writes == 1 && f.Uploads == 1); Assert((await db.ListAsync()).Single().Addable == JobActions.None);
    await Throws<NeedsActionException>(() => c.AddActionsAsync(job.Id, JobActions.WriteSheet, settings, job.Niche));
    foreach (var action in new[] { JobActions.WriteSheet, JobActions.UploadYouTube, JobActions.Download })
    {
        var before = (await db.ListAsync()).Single(); var uploadId = before.YouTubeAttemptId;
        await c.RemoveJobAsync(job.Id, action == JobActions.WriteSheet, action == JobActions.UploadYouTube, action == JobActions.Download); await FinishAutomatically(db, job.Id);
        var deleted = (await db.ListAsync()).Single(); Assert(!deleted.RemovedFromHistory && deleted.Addable == action);
        await c.AddActionsAsync(job.Id, action, settings, job.Niche); await FinishAutomatically(db, job.Id);
        var redone = (await db.ListAsync()).Single(); Assert(redone.Results == JobActions.All && redone.Receipts.Count == (action == JobActions.WriteSheet ? 1 : action == JobActions.UploadYouTube ? 2 : 3));
        if (action == JobActions.UploadYouTube) Assert(redone.YouTubeAttemptId != uploadId);
    }
    Assert(f.Downloads == 2 && f.Writes == 2 && f.Uploads == 2);
    await c.RemoveJobAsync(job.Id, true, true, true, true); await FinishAutomatically(db, job.Id); await c.StopAsync(); Assert((await db.ListAsync()).Single().RemovedFromHistory);
});
await Test("Chỉ bảng → thêm đăng tự tải, không ghi bảng lại; đích mới chỉ áp dụng phần mới", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "sheet-add-upload")); var f = new WorkflowFakes(Path.Combine(runRoot, "sheet-upload-files")); var c = new JobCoordinator(db, f, f, f);
    var job = Job(); job.Actions = JobActions.WriteSheet; job.Media = null; job.Metadata = null; await c.SubmitAsync(job); await FinishAutomatically(db, job.Id);
    var settings = new AppSettings { VideoFolder = runRoot, Sheet = job.Sheet! with { SpreadsheetId = "new-sheet" }, Channel = job.Channel! with { ChannelId = "new-channel" } };
    await c.AddActionsAsync(job.Id, JobActions.UploadYouTube, settings, job.Niche); await FinishAutomatically(db, job.Id); await c.StopAsync();
    var done = (await db.ListAsync()).Single(); Assert(done.Sheet?.SpreadsheetId == "sheet-id" && done.Channel?.ChannelId == "new-channel" && f.Writes == 1 && f.Downloads == 1 && f.Uploads == 1);
});
await Test("Video trùng mọi trạng thái và đích, kể cả ẩn còn receipt", async () => {
    foreach (var state in Enum.GetValues<JobState>())
    {
        var db = new LocalStore(Path.Combine(runRoot, "dedup-" + state)); var f = new WorkflowFakes(runRoot); var c = new JobCoordinator(db, f, f, f);
        var old = Job(); old.State = state; old.Actions = JobActions.Download; await db.SaveAsync(old);
        var next = Job(); next.Name = "Tên khác"; next.Channel = next.Channel! with { ChannelId = "another" }; next.Input += "?from=share";
        await Throws<DuplicateJobException>(() => c.SubmitAsync(next)); Assert((await db.ListAsync()).Count == 1 && f.Downloads == 0 && f.Writes == 0 && f.Uploads == 0);
    }
});
await Test("Hai lần gửi đồng thời chỉ tạo một job cho cùng video", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "dedup-concurrent")); var f = new WorkflowFakes(runRoot); var c = new JobCoordinator(db, f, f, f);
    var a = Job(); a.Actions = JobActions.WriteSheet; var b = Job(); b.Actions = JobActions.WriteSheet;
    var results = await Task.WhenAll(new[] { a, b }.Select(async job => { try { await c.SubmitAsync(job); return true; } catch (DuplicateJobException) { return false; } }));
    Assert(results.Count(x => x) == 1); await c.StopAsync(); Assert((await db.ListAsync()).Count == 1);
});
await Test("Lựa chọn theo checkpoint: chưa làm, đã có, tải dở, chưa rõ và đã xóa", () => Sync(() => {
    var job = Job(); job.Actions = JobActions.All; job.State = JobState.Paused; job.Media = null; job.Metadata = null; job.DownloadDirectory = runRoot;
    Assert(job.Results == JobActions.Download && job.Addable == JobActions.None);
    job.SheetWriteStarted = true; Assert(job.UnknownResult && job.Addable == JobActions.None && !job.Results.HasFlag(JobActions.WriteSheet));
    job.SheetDeleted = true; job.YouTubeDeleted = true; job.FolderDeleted = true; Assert(!job.UnknownResult && job.Results == JobActions.None && job.Addable == JobActions.All);
}));
await Test("Không xóa lịch sử khi còn kết quả/chưa rõ; xóa lịch sử job trống", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "history-policy")); var f = new WorkflowFakes(runRoot); var c = new JobCoordinator(db, f, f, f);
    var job = Job(); job.Actions = JobActions.WriteSheet; job.State = JobState.Complete; job.SheetVerified = true; await db.SaveAsync(job);
    await Throws<NeedsActionException>(() => c.RemoveJobAsync(job.Id, removeHistory: true));
    await c.RemoveJobAsync(job.Id, deleteSheet: true, removeHistory: true); await FinishAutomatically(db, job.Id); Assert((await db.ListAsync()).Single().RemovedFromHistory);
    var empty = Job(); empty.Media = null; empty.Source = null; empty.Actions = JobActions.Download; empty.State = JobState.Paused; await db.SaveAsync(empty);
    await c.RemoveJobAsync(empty.Id, removeHistory: true); await FinishAutomatically(db, empty.Id); await c.StopAsync(); Assert((await db.ListAsync()).Single(x => x.Id == empty.Id).RemovedFromHistory);
});
await Test("File local bị mất: bổ sung đăng bắt buộc tải lại, giữ dòng đã ghi", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "missing-local")); var f = new WorkflowFakes(Path.Combine(runRoot, "missing-local-files")); var c = new JobCoordinator(db, f, f, f);
    var job = Job(); job.Actions = JobActions.Download | JobActions.WriteSheet; job.Media = null; job.Metadata = null;
    await c.SubmitAsync(job); await FinishAutomatically(db, job.Id);
    var old = (await db.ListAsync()).Single(); File.Delete(old.Media!.Path);
    Assert(old.MissingDownload && old.Addable.HasFlag(JobActions.Download));
    await c.AddActionsAsync(job.Id, JobActions.UploadYouTube, new AppSettings { VideoFolder = runRoot, Sheet = job.Sheet, Channel = job.Channel }, job.Niche);
    await FinishAutomatically(db, job.Id); await c.StopAsync();
    Assert(f.Downloads == 2 && f.Writes == 1 && f.Uploads == 1);
});

await Test("Video đã ẩn/xóa hết cần xác nhận thay thế, record còn kết quả thì chặn", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "replacement")); var f = new WorkflowFakes(runRoot); var c = new JobCoordinator(db, f, f, f);
    var old = Job(); old.Actions = JobActions.All; old.State = JobState.Complete; old.RemovedFromHistory = true; old.FolderDeleted = true; old.SheetDeleted = true; old.YouTubeDeleted = true; await db.SaveAsync(old);
    var next = Job(); next.Actions = JobActions.WriteSheet; next.Metadata = null;
    await Throws<DuplicateJobException>(() => c.SubmitAsync(next)); Assert((await db.ListAsync()).Count == 1);
    await c.SubmitAsync(next, old.Id); await FinishAutomatically(db, next.Id); await c.StopAsync();
    Assert((await db.ListAsync()).Count == 2 && (await db.ListAsync()).Single(x => x.Id == old.Id).RemovedFromHistory);
});
await Test("Migrate schema 5 giữ session/offset, partial, deleted receipt và mode cũ", async () => {
    var folder = Path.Combine(runRoot, "migrate-5"); var store = new LocalStore(folder);
    var legacy = Job(); legacy.Mode = JobMode.Full; legacy.State = JobState.Paused; legacy.YouTubeId = null; legacy.UploadSessionKey = "upload-old"; legacy.UploadedBytes = 817; legacy.MediaSendStarted = true;
    legacy.DownloadDirectory = Path.Combine(runRoot, "existing-owned-dir"); legacy.SheetVerified = true; legacy.SheetDeleted = true; await store.SaveAsync(legacy);
    using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(folder, "jobs.db"))) { connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=5"; command.ExecuteNonQuery(); }
    var migrated = (await new LocalStore(folder).ListAsync()).Single();
    Assert(migrated.Id == legacy.Id && migrated.RequestId == legacy.RequestId && migrated.Actions == JobActions.All && migrated.UploadSessionKey == "upload-old" && migrated.UploadedBytes == 817 && migrated.MediaSendStarted && migrated.DownloadDirectory == legacy.DownloadDirectory && migrated.SheetDeleted);
});
var channelSec = "MS4wLjABAAAA6tEmt0QOezhs44TtuyFExpYwaBQGh4M51Nj456G76k7gomWJzhrjZVECMX1Hx97M";
SourceChannelMetadata ChannelFixture() => new(channelSec, "硬核Uncle Car", "Hardcore Uncle Car", 123456,
    Enumerable.Range(0, 10).Select(i => new ChannelVideo((7673782911702748458L - i).ToString(), 1800000000 - i, i == 0 ? 300001 : 300000)).ToArray(), DateTimeOffset.UtcNow);
JsonNode ChannelGrid(SourceChannelMetadata data, bool present, string niche = "Vũ trụ") => new JsonObject {
    ["properties"] = new JsonObject { ["sheetId"] = 8, ["gridProperties"] = new JsonObject { ["rowCount"] = 100, ["columnCount"] = 6 } },
    ["data"] = new JsonArray(new JsonObject { ["rowData"] = present ? new JsonArray(Row(Rules.ChannelHeaders.Select(Value).Append(new JsonObject()).ToArray()),
        Row(Value(data.Url), Value(data.EnglishName), new JsonObject { ["userEnteredValue"] = new JsonObject { ["numberValue"] = data.Followers } }, Value(niche), Value(data.ContentType), Value("Giữ ô khác")))
        : new JsonArray(Row(Rules.ChannelHeaders.Select(Value).Append(new JsonObject()).ToArray())) })
};
JsonNode BothTabs() => JsonNode.Parse("""{"sheets":[{"properties":{"sheetId":7,"title":"Video","sheetType":"GRID","gridProperties":{"rowCount":100,"columnCount":5}}},{"properties":{"sheetId":8,"title":"Kênh nguồn","sheetType":"GRID","gridProperties":{"rowCount":100,"columnCount":6}}}]}""")!;
await Test("Chọn cột theo vị trí: tiêu đề tùy ý/đổi tên, không nhận cột trùng hoặc ngoài tab", async () => {
    var grid = Grid(); var cells = grid["data"]![0]!["rowData"]![0]!["values"]!.AsArray();
    for (var i = 0; i < cells.Count; i++) cells[i] = Value("Tiêu đề riêng " + i);
    var mapping = new SheetColumnMapping(0, [4, 1, 3, 0]);
    Assert(SheetLayout.Parse(grid, mapping: mapping).Columns.SequenceEqual(mapping.Columns));
    await Throws<NeedsActionException>(() => Sync(() => SheetLayout.Parse(grid)));
    await Throws<NeedsActionException>(() => Sync(() => SheetLayout.Parse(grid, mapping: new(0, [0, 0, 1, 2]))));
    await Throws<NeedsActionException>(() => Sync(() => SheetLayout.Parse(grid, mapping: new(0, [0, 1, 2, 5]))));
});
await Test("Đọc lựa chọn cột chỉ 30 hàng, tên tùy ý, gợi ý lưu sẵn theo vị trí", async () => {
    var grid = Grid(); var values = grid["data"]![0]!["rowData"]![0]!["values"]!.AsArray();
    for (var i = 0; i < values.Count; i++) values[i] = Value("Tên mới " + i);
    var svc = await Services(new ScriptedHandler(request => {
        if (!request.RequestUri!.Query.Contains("includeGridData")) return Task.FromResult(JsonReply(BothTabs()));
        Assert(Uri.UnescapeDataString(request.RequestUri.Query).Contains("!A1:E30"));
        return Task.FromResult(JsonReply(new JsonObject { ["sheets"] = new JsonArray(grid.DeepClone()) }));
    }));
    var target = Job().Sheet! with { Columns = new(0, [4, 1, 3, 0]) };
    var choices = await svc.Sheets.ColumnOptionsAsync(target, false, null, default);
    Assert(choices.Options.Count == 5 && choices.Options[4].Letter == "E" && choices.Options[4].Header == "Tên mới 4" && choices.Suggested!.Columns.SequenceEqual([4, 1, 3, 0]));
    var manual = await svc.Sheets.ColumnOptionsAsync(target with { Columns = null }, false, null, default);
    Assert(manual.Suggested is null && manual.Options.Count == 5);
});
await Test("Mốc tùy chọn cùng áp dụng video/kênh; cấu hình cũ thiếu trường vẫn 5 phút", async () => {
    var job = Job(); job.ShortMaxMilliseconds = 120000; job.Metadata = job.Metadata! with { DurationMilliseconds = 120000 };
    Assert(job.Type == "Short"); job.Metadata = job.Metadata with { DurationMilliseconds = 120001 }; Assert(job.Type == "Long");
    var channel = ChannelFixture() with { LatestVideos = [new("1", 2, 120000), new("2", 1, 120001)] };
    Assert(channel.Classify(120000) == "All" && channel.Classify(180000) == "Short");
    Assert(JsonSerializer.Deserialize<VideoJob>("{}")!.ShortMaxMilliseconds == 300000 && JsonSerializer.Deserialize<AppSettings>("{}")!.ShortMaxMilliseconds == 300000);
    job.ShortMaxMilliseconds = 0; await Throws<ArgumentException>(() => Sync(() => Rules.Validate(job)));
});
await Test("Atomic hai tab với cột đảo thứ tự, tiêu đề tự chọn và mốc 2 phút; retry sau đổi tên không POST lại", async () => {
    var job = Job(); job.ShortMaxMilliseconds = 120000; job.Metadata = job.Metadata! with { DurationMilliseconds = 150000 };
    job.Sheet = job.Sheet! with { SourceChannelSheetId = 8, Columns = new(0, [4, 1, 3, 0]), SourceChannelColumns = new(0, [4, 3, 2, 1, 0]) };
    var channel = ChannelFixture(); var written = false; var posts = 0;
    JsonNode Reordered(bool source)
    {
        var grid = source ? ChannelGrid(channel, false) : Grid(); var map = source ? job.Sheet.SourceChannelColumns! : job.Sheet.Columns!;
        var count = source ? 6 : 5; var rows = grid["data"]![0]!["rowData"]!.AsArray(); rows.Clear();
        rows.Add(Row(Enumerable.Range(0, count).Select(i => Value((written ? "Đổi tên " : "Tên riêng ") + i)).ToArray()));
        if (written)
        {
            var physical = Enumerable.Range(0, count).Select(_ => (JsonNode)new JsonObject()).ToArray();
            var logical = source ? new[] { channel.Url, channel.EnglishName, channel.Followers.ToString(), job.Niche, "Long" } : new[] { job.Source!.CanonicalUrl, job.Name, job.Niche, "Long" };
            for (var i = 0; i < map.Columns.Length; i++) physical[map.Columns[i]] = source && i == 2 ? new JsonObject { ["userEnteredValue"] = new JsonObject { ["numberValue"] = channel.Followers } } : Value(logical[i]);
            rows.Add(Row(physical));
        }
        return grid;
    }
    var svc = await Services(new ScriptedHandler(async request => {
        if (request.Method == HttpMethod.Post)
        {
            posts++; var requests = JsonNode.Parse(await request.Content!.ReadAsStringAsync())!["requests"]!.AsArray();
            Assert(requests.Count == 9 && requests.Take(4).Select(r => r!["updateCells"]!["range"]!["startColumnIndex"]!.GetValue<int>()).SequenceEqual([4, 1, 3, 0]));
            Assert(requests[3]!["updateCells"]!["rows"]![0]!["values"]![0]!["userEnteredValue"]!["stringValue"]!.ToString() == "Long");
            Assert(requests[8]!["updateCells"]!["rows"]![0]!["values"]![0]!["userEnteredValue"]!["stringValue"]!.ToString() == "Long");
            written = true; return JsonReply(new JsonObject());
        }
        if (!request.RequestUri!.Query.Contains("includeGridData")) return JsonReply(BothTabs());
        return JsonReply(new JsonObject { ["sheets"] = new JsonArray(Reordered(Uri.UnescapeDataString(request.RequestUri.Query).Contains("'Kênh nguồn'!"))) });
    }), new FixedChannelReader(channel));
    await svc.Sheets.WriteAndVerifyAsync(job, () => Task.CompletedTask, default); Assert(job.SheetVerified && posts == 1);
    job.SheetVerified = false; await svc.Sheets.WriteAndVerifyAsync(job, () => Task.CompletedTask, default); Assert(posts == 1 && job.SheetVerified);
});
await Test("粉丝 chỉ lấy follower_count của đúng profile, không lấy like/placeholder feed", () => Sync(() => {
    var data = JsonNode.Parse("{\"status_code\":0,\"user\":{\"sec_uid\":\"" + channelSec + "\",\"nickname\":\"Kênh\",\"follower_count\":123,\"total_favorited\":999}}")!;
    Assert(DouyinChannelReader.Profile(data, channelSec)?.Followers == 123);
    Assert(DouyinChannelReader.Profile(data, "other") is null);
    data["user"]!["follower_count"] = "123"; Assert(DouyinChannelReader.Profile(data, channelSec) is null);
    data["user"]!.AsObject().Remove("follower_count"); Assert(DouyinChannelReader.Profile(data, channelSec) is null);
    data["user"]!["follower_count"] = 0; Assert(DouyinChannelReader.Profile(data, channelSec)?.Followers == 0);
}));
await Test("Kênh phân loại 10 mới nhất, bỏ ảnh, xử lý ghim và biên 5 phút", () => Sync(() => {
    var all = ChannelFixture(); Assert(all.ContentType == "All");
    Assert((all with { LatestVideos = all.LatestVideos.Select(v => v with { DurationMilliseconds = 300000 }).ToArray() }).ContentType == "Short");
    Assert((all with { LatestVideos = all.LatestVideos.Select(v => v with { DurationMilliseconds = 300001 }).ToArray() }).ContentType == "Long");
    var oldPinned = new ChannelVideo("7600000000000000000", 1, 500000);
    var latest = DouyinChannelReader.Latest(all.LatestVideos.Reverse().Append(oldPinned).Concat(all.LatestVideos));
    Assert(latest.Count == 10 && latest[0].PublishedAt == 1800000000 && !latest.Contains(oldPinned));
    Assert(DouyinChannelReader.Latest(all.LatestVideos.Take(3)).Count == 3);
}));
await Test("Không bỏ video mới thiếu duration để lấy video cũ thay thế", async () => {
    var sample = ChannelFixture().LatestVideos.ToArray(); sample[0] = sample[0] with { DurationMilliseconds = 0 };
    await Throws<NeedsActionException>(() => Sync(() => DouyinChannelReader.Latest(sample)));
    await Throws<NeedsActionException>(() => Sync(() => DouyinChannelReader.Latest(sample.Concat([sample[1] with { DurationMilliseconds = 1 }]))));
});
await Test("Danh sách kênh bị chặn/rỗng/sai tác giả không coi là thành công", async () => {
    foreach (var body in new[] { "{\"status_code\":0,\"has_more\":0,\"max_cursor\":0,\"aweme_list\":[]}", "{\"status_code\":1}" })
        await Throws<NeedsActionException>(() => Sync(() => DouyinChannelReader.Posts(JsonNode.Parse(body)!, channelSec)));
    var item = new JsonObject { ["aweme_id"] = "7673782911702748458", ["author"] = new JsonObject { ["sec_uid"] = "wrong" }, ["create_time"] = 123, ["video"] = new JsonObject { ["duration"] = 1 } };
    var page = new JsonObject { ["status_code"] = 0, ["has_more"] = 0, ["max_cursor"] = 0, ["aweme_list"] = new JsonArray(item) };
    await Throws<NeedsActionException>(() => Sync(() => DouyinChannelReader.Posts(page, channelSec)));
    item["author"]!["sec_uid"] = channelSec; item["aweme_type"] = 68; item["images"] = new JsonArray(new JsonObject());
    Assert(DouyinChannelReader.Posts(page, channelSec).Videos.Count == 0);
});
JsonNode BrowserPage(IEnumerable<ChannelVideo> videos, bool more, long cursor) => new JsonObject {
    ["status_code"] = 0, ["has_more"] = more ? 1 : 0, ["max_cursor"] = cursor,
    ["aweme_list"] = new JsonArray(videos.Select(v => (JsonNode)new JsonObject { ["aweme_id"] = v.Id,
        ["author"] = new JsonObject { ["sec_uid"] = channelSec }, ["create_time"] = v.PublishedAt,
        ["is_top"] = v.IsPinned ? 1 : 0, ["video"] = new JsonObject { ["duration"] = v.DurationMilliseconds } }).ToArray())
};
string BrowserProfile() => new JsonObject { ["status_code"] = 0, ["user"] = new JsonObject {
    ["sec_uid"] = channelSec, ["nickname"] = "Channel", ["follower_count"] = 28344 } }.ToJsonString();
Uri PostUri(long cursor = 0) => new("https://www.douyin.com/aweme/v1/web/aweme/post/?sec_user_id=" + channelSec + "&max_cursor=" + cursor);

await Test("Nút từng dòng đổi trạng thái; tải lại bìa chỉ với file đã có, không chạy/xóa/chưa rõ", () => Sync(() => {
    var job = Job(); job.State = JobState.Running; job.DownloadDirectory = runRoot;
    Assert(job.CanPause && !job.CanResume && job.PauseResumeLabel == "Tạm dừng" && !job.CanRefreshCovers);
    job.State = JobState.Paused; Assert(!job.CanPause && job.CanResume && job.PauseResumeLabel == "Tiếp tục" && job.CanRefreshCovers);
    job.State = JobState.Complete; Assert(!job.CanTogglePause && job.CanSupplement);
    job.DeletionRequested = true; Assert(!job.CanRefreshCovers); job.DeletionRequested = false;
    job.MediaSendStarted = true; Assert(!job.CanRefreshCovers);
}));
await Test("Danh sách hai tab và chọn kênh theo ID sau đổi tên tab", async () => {
    var tabs = BothTabs(); tabs["sheets"]![1]!["properties"]!["title"] = "Nguồn đổi tên";
    var job = Job(); job.Sheet = job.Sheet! with { SourceChannelSheetId = 8, SourceChannelTabName = "Tên cũ" };
    var svc = await Services(new ScriptedHandler(req => Task.FromResult(!req.RequestUri!.Query.Contains("includeGridData") ? JsonReply(tabs)
        : JsonReply(new JsonObject { ["sheets"] = new JsonArray(ChannelGrid(ChannelFixture(), false)) }))), new FixedChannelReader(ChannelFixture()));
    var choices = await svc.Sheets.TabsAsync(job.Sheet, default); Assert(choices.Count == 2 && choices[1].Id == 8);
    await svc.Sheets.PrepareAsync(job, () => Task.CompletedTask, default);
    Assert(job.SourceChannelSheet?.SheetId == 8 && job.SourceChannelSheet.TabName == "Nguồn đổi tên" && job.Sheet.SheetId == 7);
});
await Test("Wizard lưu hai tab riêng, chặn chọn cùng tab trước lưu", async () => {
    var service = new SetupService();
    service.Selection = new("new-sheet", "Bảng thử", [new(8, "Kênh nguồn"), new(7, "Video")], "new-credential");
    var draft = new GoogleConnectionDraft(service, (_, _, _) => Task.CompletedTask);
    await draft.SelectSheetAsync(default);
    Assert(draft.Sheet?.SheetId == 7 && draft.Sheet.SourceChannelSheetId == 8);
    await Throws<NeedsActionException>(() => draft.ContinueSheetAsync(7, default, 7));
    await draft.ContinueSheetAsync(7, default, 8);
    Assert(draft.Sheet?.SourceChannelSheetId == 8);
    await draft.CancelAsync();
});
await Test("Bộ đọc video xác thực ID/thời lượng, giữ rendition nguồn và không ghi file", () => Sync(() => {
    var src = new SourceVideo("123", "https://www.douyin.com/video/123");
    var root = DetailReply(); root["status_code"] = 0; var item = root["aweme_detail"]!; item["video"]!["duration"] = 300001L; item["video"]!["play_addr"]!["uri"] = "fixture-source";
    var url = new Uri("https://www.douyin.com/aweme/v1/web/aweme/detail/?aweme_id=123");
    var info = DouyinBrowserCapture.VideoMetadata(url, root.ToJsonString(), src);
    Assert(info is not null && info["id"]!.ToString() == "123" && info["source_duration_ms"]!.GetValue<long>() == 300001);
    Assert(info!["formats"]!.AsArray().Any(f => f!["format_id"]!.ToString().StartsWith("download_addr-original")));
    Assert(DouyinBrowserCapture.VideoMetadata(new Uri(url + "&aweme_id=other"), root.ToJsonString(), src) is null);
    item["aweme_id"] = "wrong"; Assert(DouyinBrowserCapture.VideoMetadata(url, root.ToJsonString(), src) is null);
}));
await Test("Douyin báo video không tồn tại: nhận đúng thông báo, không suy từ văn bản khác", () => Sync(() => {
    Assert(DouyinBrowserCapture.IsUnavailableNotice(" 你要观看的视频不存在 "));
    Assert(DouyinBrowserCapture.IsUnavailableNotice("The video you want to watch does not exist."));
    Assert(!DouyinBrowserCapture.IsUnavailableNotice("你要观看的视频不存在 nhưng đây là bình luận"));
    Assert(!DouyinBrowserCapture.IsUnavailableNotice("请登录"));
}));
await Test("WebView capture đúng video/chủ kênh; ghim cũ không thay 10 mới nhất", () => Sync(() => {
    var job = Job(); var capture = new DouyinBrowserCapture(job.Source!, null);
    var detail = new JsonObject { ["status_code"] = 0, ["aweme_detail"] = new JsonObject { ["aweme_id"] = job.Source!.Id, ["author"] = new JsonObject { ["sec_uid"] = channelSec } } }.ToJsonString();
    Assert(!capture.Accept(new Uri("https://evil.example/aweme/v1/web/aweme/detail/?aweme_id=" + job.Source.Id), detail));
    Assert(!capture.Accept(new Uri("https://www.douyin.com/aweme/v1/web/aweme/detail/?aweme_id=other"), detail));
    Assert(capture.Accept(new Uri("https://www.douyin.com/aweme/v1/web/aweme/detail/?aweme_id=" + job.Source.Id), detail));
    Assert(capture.SecUid == channelSec && capture.Result() is null);
    var videos = ChannelFixture().LatestVideos;
    var pinned = new ChannelVideo("7600000000000000000", 1, 999999, true);
    Assert(capture.Accept(PostUri(123), BrowserPage(videos.Skip(5), false, 0).ToJsonString()));
    Assert(capture.Result() is null); // A response to a later page cannot substitute for the first page.
    Assert(capture.Accept(PostUri(), BrowserPage(new[] { pinned }.Concat(videos.Take(5)), true, 123).ToJsonString()));
    Assert(capture.Result() is null);
    Assert(capture.Accept(new Uri("https://www.douyin.com/aweme/v1/web/user/profile/other/?sec_user_id=" + channelSec), BrowserProfile()));
    var ready = capture.Result()!; Assert(ready.Followers == 28344 && ready.Videos.Count == 10 && ready.Videos.All(v => v.Id != pinned.Id));
    Assert(!capture.Accept(PostUri(), BrowserPage(videos, false, 0).ToJsonString()));
}));
await Test("WebView capture không nhận sai query/kênh hay video thiếu duration", async () => {
    var capture = new DouyinBrowserCapture(Job().Source!, channelSec);
    Assert(!capture.Accept(new Uri("https://www.douyin.com/aweme/v1/web/user/profile/other/?sec_user_id=wrong"), BrowserProfile()));
    Assert(!capture.Accept(new Uri("https://www.douyin.com/aweme/v1/web/user/profile/other/?sec_user_id=" + channelSec + "&sec_user_id=wrong"), BrowserProfile()));
    var page = BrowserPage(ChannelFixture().LatestVideos, false, 0); page["aweme_list"]![0]!["author"]!["sec_uid"] = "wrong";
    await Throws<NeedsActionException>(() => Sync(() => capture.Accept(PostUri(), page.ToJsonString())));
    var missing = ChannelFixture().LatestVideos.Select((v, i) => i == 0 ? v with { DurationMilliseconds = 0 } : v);
    capture.Accept(PostUri(), BrowserPage(missing, false, 0).ToJsonString());
    capture.Accept(new Uri("https://www.douyin.com/aweme/v1/web/user/profile/other/?sec_user_id=" + channelSec), BrowserProfile());
    await Throws<NeedsActionException>(() => Sync(() => capture.Result()));
});

await Test("Dịch tên ngoài Latinh; tiếng Việt giữ nguyên, không gửi cookie/OAuth", async () => {
    var calls = 0; using var http = new HttpClient(new ScriptedHandler(req => {
        calls++; Assert(req.RequestUri!.Host == "translate.googleapis.com" && req.RequestUri.Query.Contains("tl=en"));
        Assert(req.Headers.Authorization is null && !req.Headers.Contains("Cookie"));
        return Task.FromResult(JsonReply(JsonNode.Parse("""[[["Hardcore Uncle Car","硬核Uncle Car",null,null]],null,"zh-CN"]""")!));
    }));
    var translator = new ChannelNameTranslator(http);
    Assert(await translator.EnglishAsync("Kênh khoa học", default) == "Kênh khoa học" && calls == 0);
    Assert(await translator.EnglishAsync("硬核Uncle Car", default) == "Hardcore Uncle Car" && calls == 1);
    Assert(ChannelNameTranslator.NeedsTranslation("Привет") && ChannelNameTranslator.NeedsTranslation("日本") && !ChannelNameTranslator.NeedsTranslation("Café Việt Nam"));
});
await Test("Dịch bị giới hạn/malformed/không dịch thì dừng, không dùng tên gốc", async () => {
    foreach (var kind in new[] { 0, 1, 2 })
    {
        using var http = new HttpClient(new ScriptedHandler(req => Task.FromResult(kind == 0 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            : JsonReply(JsonNode.Parse(kind == 1 ? "{}" : "[[[\"硬核\"]]]")!))));
        await Throws<NeedsActionException>(() => new ChannelNameTranslator(http).EnglishAsync("硬核", default));
    }
});
await Test("Thư mục trùng kể cả rỗng dừng, chỉ resume thư mục sở hữu", async () => {
    var job = Job(); job.Name = "Trùng rỗng"; job.Folder = Path.Combine(runRoot, "empty-collision"); var path = Path.Combine(job.Folder, Rules.FileStem(job.Name)); Directory.CreateDirectory(path);
    var state = Path.Combine(runRoot, "empty-collision-state");
    await Throws<NeedsActionException>(() => Sync(() => DownloadPolicy.ReserveDirectory(job, state)));
    Assert(!Directory.Exists(state) && !Directory.EnumerateFileSystemEntries(path).Any());
    Directory.Delete(path); DownloadPolicy.ReserveDirectory(job, state); DownloadPolicy.CheckDirectory(job, state);
    var other = Job(); other.Name = job.Name; other.Folder = job.Folder;
    await Throws<NeedsActionException>(() => Sync(() => DownloadPolicy.CheckDirectory(other, state)));
});
await Test("Sheets video đã có dừng kể cả bốn ô trùng, không nhận là kết quả mới", async () => {
    var job = Job(); var posts = 0;
    var svc = await Services(new ScriptedHandler(async req => { if (req.Method == HttpMethod.Post) posts++; return req.RequestUri!.Query.Contains("includeGridData") ? JsonReply(await SheetReply(true)) : JsonReply(BothTabs()); }));
    await Throws<NeedsActionException>(() => svc.Sheets.CheckDuplicateAsync(job, default));
    await Throws<NeedsActionException>(() => svc.Sheets.WriteAndVerifyAsync(job, () => Task.CompletedTask, default));
    Assert(posts == 0 && !job.SheetVerified && !job.SheetWriteStarted);
});
foreach (var update in new[] { false, true })
await Test("Atomic video + Kênh nguồn " + (update ? "update cùng dòng" : "thêm mới") + "; retry không ghi hai lần", async () => {
    var job = Job(); var sourceChannel = ChannelFixture(); var reader = new FixedChannelReader(sourceChannel); var written = false; var posts = 0;
    var old = sourceChannel with { Followers = 1, EnglishName = "Old name" };
    var svc = await Services(new ScriptedHandler(async req => {
        if (req.Method == HttpMethod.Post)
        {
            posts++; Assert(job.SheetWriteStarted && job.SourceChannelSheet?.SheetId == 8 && job.SourceChannelUpdated == update);
            var requests = JsonNode.Parse(await req.Content!.ReadAsStringAsync())!["requests"]!.AsArray(); Assert(requests.Count == 9);
            Assert(requests.All(x => x!["updateCells"]!["fields"]!.ToString() == "userEnteredValue"));
            Assert(requests.Count(x => x!["updateCells"]!["range"]!["sheetId"]!.GetValue<int>() == 8) == 5);
            Assert(requests[6]!["updateCells"]!["rows"]![0]!["values"]![0]!["userEnteredValue"]!["numberValue"]!.GetValue<long>() == sourceChannel.Followers);
            Assert(requests.All(x => x!["updateCells"]!["range"]!["startColumnIndex"]!.GetValue<int>() < 5));
            written = true; return JsonReply(new JsonObject());
        }
        if (!req.RequestUri!.Query.Contains("includeGridData")) return JsonReply(BothTabs());
        var channelTab = Uri.UnescapeDataString(req.RequestUri.Query).Contains("'Kênh nguồn'!");
        return JsonReply(new JsonObject { ["sheets"] = new JsonArray(channelTab ? ChannelGrid(written ? sourceChannel : old, update || written)
            : written ? Grid(Row(Value(job.Source!.CanonicalUrl), Value(job.Name), Value(job.Niche), Value(job.Type))) : Grid()) });
    }), reader);
    await svc.Sheets.PrepareAsync(job, () => Task.CompletedTask, default);
    await svc.Sheets.WriteAndVerifyAsync(job, () => Task.CompletedTask, default); Assert(job.SheetVerified && posts == 1 && reader.Reads == 1);
    job.SheetVerified = false; await svc.Sheets.WriteAndVerifyAsync(job, () => Task.CompletedTask, default);
    Assert(posts == 1 && job.SourceChannelUpdated == update && job.SourceChannelMessage.Contains(update ? "cập nhật" : "thêm kênh mới"));
});
await Test("Kênh trùng nhiều dòng/công thức cập nhật bị chặn", async () => {
    var data = ChannelFixture(); var grid = ChannelGrid(data, true); var layout = SheetLayout.Parse(grid, Rules.ChannelHeaders);
    Assert(layout.FindChannel(data.SecUid).SequenceEqual([1]));
    grid["data"]![0]!["rowData"]!.AsArray().Add(grid["data"]![0]!["rowData"]![1]!.DeepClone());
    Assert(SheetLayout.Parse(grid, Rules.ChannelHeaders).FindChannel(data.SecUid).Count == 2);
    grid["data"]![0]!["rowData"]![1]!["values"]![1]!["userEnteredValue"] = new JsonObject { ["formulaValue"] = "=NOW()" };
    await Throws<NeedsActionException>(() => Sync(() => SheetLayout.Parse(grid, Rules.ChannelHeaders).ValidateDestination(1, [data.Url, data.EnglishName, "123456", "Vũ trụ", "All"], overwrite: true)));
});
await Test("YouTube trùng tiêu đề ở trang sau/private, không POST upload", async () => {
    var job = Job(); job.Title = "Tên Việt"; var pages = 0; var posts = 0;
    var svc = await Services(new ScriptedHandler(req => {
        Assert(req.Method == HttpMethod.Get); if (req.Method != HttpMethod.Get) posts++;
        var path = req.RequestUri!.AbsolutePath;
        if (path.EndsWith("channels")) return Task.FromResult(JsonReply(JsonNode.Parse("""{"items":[{"id":"channel-id","contentDetails":{"relatedPlaylists":{"uploads":"uploads-id"}}}]}""")!));
        if (path.EndsWith("playlistItems")) { pages++; Assert(pages != 2 || req.RequestUri.Query.Contains("pageToken=next")); return Task.FromResult(JsonReply(JsonNode.Parse(pages == 1 ? """{"items":[{"contentDetails":{"videoId":"first"}}],"nextPageToken":"next"}""" : """{"items":[{"contentDetails":{"videoId":"private-id"}}]}""")!)); }
        return Task.FromResult(JsonReply(new JsonObject { ["items"] = new JsonArray(new JsonObject { ["id"] = pages == 1 ? "first" : "private-id", ["snippet"] = new JsonObject { ["channelId"] = "channel-id", ["title"] = pages == 1 ? "Khác" : " tên việt " } }) }));
    }), checkTitles: true);
    await Throws<NeedsActionException>(() => svc.YouTube.CheckDuplicateAsync(job, default)); Assert(pages == 2 && posts == 0 && job.YouTubeId is null);
    job.YouTubeId = "own-id"; await svc.YouTube.CheckDuplicateAsync(job, default); Assert(pages == 2);
});
foreach (var destination in new[] { JobActions.Download, JobActions.WriteSheet, JobActions.UploadYouTube })
await Test("Trùng " + destination + " dừng trước mọi tải/ghi/đăng", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "duplicate-destination-" + destination)); var f = new WorkflowFakes(Path.Combine(runRoot, "duplicate-files-" + destination)) { DuplicateAt = destination };
    var c = new JobCoordinator(db, f, f, f); var job = Job(); job.Media = null; job.Metadata = null;
    await c.SubmitAsync(job); await Finish(db, job.Id); await c.StopAsync(); var done = (await db.ListAsync()).Single();
    Assert(done.State == JobState.NeedsAction && done.Message.Contains("trùng") && f.Downloads == 0 && f.Writes == 0 && f.Uploads == 0 && f.MetadataReads == 0);
});

await Test("Schema 6→8 giữ intent bốn ô cũ, không tự ghi thêm kênh khi đối soát", async () => {
    var folder = Path.Combine(runRoot, "migrate-6-channel"); var db = new LocalStore(folder); var job = Job(); job.Actions = JobActions.All;
    job.SheetWriteStarted = true; job.SheetTypeValue = "Long"; job.UploadSessionKey = "upload-preserved"; await db.SaveAsync(job);
    var node = JsonSerializer.SerializeToNode(job, LocalStore.Json)!.AsObject(); node.Remove("IncludeSourceChannel"); node.Remove("SourceChannel"); node.Remove("SourceChannelSheet");
    var encrypted = "dpapi-v1:" + Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(node.ToJsonString()), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
    using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(folder, "jobs.db"))) {
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=6; UPDATE jobs SET payload=$payload"; command.Parameters.AddWithValue("$payload", encrypted); command.ExecuteNonQuery();
    }
    var upgraded = (await new LocalStore(folder).ListAsync()).Single();
    Assert(!upgraded.IncludeSourceChannel && upgraded.SheetWriteStarted && upgraded.SheetTypeValue == "Long" && upgraded.UploadSessionKey == "upload-preserved" && upgraded.SourceChannel is null);
});
await Test("Schema 7→8 giữ intent/checkpoint; thiếu mốc và lựa chọn cột dùng giá trị cũ", async () => {
    var folder = Path.Combine(runRoot, "migrate-7-columns"); var db = new LocalStore(folder); var job = Job();
    job.SheetWriteStarted = true; job.SheetTypeValue = "Long"; job.SheetRow = 9; job.YouTubeId = "existing-video";
    await db.SaveAsync(job); var node = JsonSerializer.SerializeToNode(job, LocalStore.Json)!.AsObject(); node.Remove("ShortMaxMilliseconds");
    node["Sheet"]!.AsObject().Remove("Columns"); node["Sheet"]!.AsObject().Remove("SourceChannelColumns");
    var encrypted = "dpapi-v1:" + Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(Encoding.UTF8.GetBytes(node.ToJsonString()), null, System.Security.Cryptography.DataProtectionScope.CurrentUser));
    using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + Path.Combine(folder, "jobs.db"))) {
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=7; UPDATE jobs SET payload=$payload"; command.Parameters.AddWithValue("$payload", encrypted); command.ExecuteNonQuery();
    }
    var upgraded = (await new LocalStore(folder).ListAsync()).Single();
    Assert(upgraded.Id == job.Id && upgraded.SheetWriteStarted && upgraded.SheetRow == 9 && upgraded.SheetTypeValue == "Long" && upgraded.YouTubeId == "existing-video");
    Assert(upgraded.ShortMaxMilliseconds == 300000 && upgraded.Sheet!.Columns is null && upgraded.Sheet.SourceChannelColumns is null);
});
var translateIndex = Array.IndexOf(args, "--live-translation");
if (translateIndex >= 0) { var original = args[translateIndex + 1]; var english = await new ChannelNameTranslator().EnglishAsync(original, default); await File.WriteAllTextAsync(Path.Combine(root, "live-translation.json"), JsonSerializer.Serialize(new { original, english, googleOAuthCalled = false, cookieSent = false })); Console.WriteLine("PASS live free translation: " + english); }
await Test("Dữ liệu video dùng bộ đọc phiên app, không đọc secret; sai ID/thời lượng bị chặn", async () => {
    var src = Job().Source!; var vault = new MemorySecrets();
    var fixture = new JsonObject { ["id"] = src.Id, ["source_duration_ms"] = 300001L };
    var browser = new FakeVideoBrowser(fixture);
    var source = new DouyinSource("no-tools", Path.Combine(runRoot, "session-only", "temp"), vault, browser);
    var result = await source.ReadMetadataAsync(src, default);
    Assert(result.SourceId == src.Id && result.DurationMilliseconds == 300001 && browser.Reads == 1 && !browser.ReadCovers && vault.Reads.Count == 0);
    Assert(!Directory.Exists(Path.Combine(runRoot, "session-only")));
    fixture["id"] = "wrong"; await Throws<NeedsActionException>(() => source.ReadMetadataAsync(src, default));
    fixture["id"] = src.Id; fixture["source_duration_ms"] = 0L; await Throws<NeedsActionException>(() => source.ReadMetadataAsync(src, default));
    using var cancel = new CancellationTokenSource(); cancel.Cancel(); var reads = browser.Reads;
    await Throws<OperationCanceledException>(() => source.ReadMetadataAsync(src, cancel.Token)); Assert(browser.Reads == reads);
});
await Test("Kênh dùng trực tiếp bộ đọc phiên app, dịch tên và hủy trước đọc", async () => {
    var job = Job(); var browser = new FakeDouyinBrowser(new(channelSec, "Channel", 28344, ChannelFixture().LatestVideos));
    var reader = new DouyinChannelReader(new ChannelNameTranslator(), browser);
    var result = await reader.ReadAsync(job, default);
    Assert(result.Followers == 28344 && result.LatestVideos.Count == 10 && result.EnglishName == "Channel" && browser.Reads == 1 && browser.Hint is null);
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    await Throws<OperationCanceledException>(() => reader.ReadAsync(job, cancel.Token)); Assert(browser.Reads == 1);
});
await Test("Bìa Jingxuan đúng ID được bổ sung; không đổi rendition, từ chối nguồn khác", () => Sync(() => {
    var src = new SourceVideo("123", "https://www.douyin.com/video/123");
    var info = DouyinWebMetadata.Parse(DetailReply()["aweme_detail"]!, src)!;
    var original = info["formats"]!.ToJsonString();
    var selected = new JsonObject { ["status_code"] = 0, ["aweme_detail"] = new JsonObject { ["aweme_id"] = "123", ["video"] = new JsonObject {
        ["y_cover"] = new JsonObject { ["url_list"] = new JsonArray("https://p3.douyinpic.com/vertical.webp") },
        ["y_cover_hor"] = new JsonObject { ["url_list"] = new JsonArray("https://p3.douyinpic.com/horizontal.webp") } } } };
    var uri = new Uri("https://www.douyin.com/aweme/v1/web/aweme/detail/?aweme_id=123&aid=568863");
    Assert(DouyinBrowserCapture.MergeVideoCovers(uri, selected.ToJsonString(), src, info) && DouyinBrowserCapture.HasBothCovers(info));
    Assert(info["formats"]!.ToJsonString() == original);
    var count = info["thumbnails"]!.AsArray().Count;
    DouyinBrowserCapture.MergeVideoCovers(uri, selected.ToJsonString(), src, info); Assert(info["thumbnails"]!.AsArray().Count == count);
    selected["aweme_detail"]!["aweme_id"] = "other"; Assert(!DouyinBrowserCapture.MergeVideoCovers(uri, selected.ToJsonString(), src, info));
    Assert(!DouyinBrowserCapture.MergeVideoCovers(new Uri("https://evil.test/aweme/v1/web/aweme/detail/?aweme_id=123"), selected.ToJsonString(), src, info));
}));
await Test("Job/cài đặt cũ bỏ tham chiếu cookie; giữ checkpoint Google và dữ liệu công việc", () => Sync(() => {
    var old = JsonSerializer.SerializeToNode(Job(), LocalStore.Json)!; old["CookieCredentialId"] = "legacy-cookie";
    old["UploadSessionKey"] = "upload-existing"; old["YouTubeId"] = "youtube-existing"; old["SheetRow"] = 12;
    var job = JsonSerializer.Deserialize<VideoJob>(old.ToJsonString(), LocalStore.Json)!;
    var encoded = JsonSerializer.Serialize(job, LocalStore.Json);
    Assert(!encoded.Contains("CookieCredentialId") && job.UploadSessionKey == "upload-existing" && job.YouTubeId == "youtube-existing" && job.SheetRow == 12);
    var settings = JsonSerializer.Deserialize<AppSettings>("{\"CookieCredentialId\":\"legacy\",\"ShortMaxMilliseconds\":150000}", LocalStore.Json)!;
    Assert(settings.ShortMaxMilliseconds == 150000 && !JsonSerializer.Serialize(settings, LocalStore.Json).Contains("CookieCredentialId"));
}));
JsonNode PublicItem(SourceVideo src, bool posters = true)
{
    var item = DetailReply(src.Id)["aweme_detail"]!.DeepClone(); item["video"]!["duration"] = 254200L; item["video"]!["play_addr"]!["uri"] = "fixture-source";
    item["author"] = new JsonObject { ["sec_uid"] = channelSec };
    if (posters) item["video"]!["y_cover_hor"] = new JsonObject { ["url_list"] = new JsonArray("https://p3.douyinpic.com/horizontal.webp") };
    return item;
}
HttpResponseMessage PublicFeed(SourceVideo src, bool posters = true) => JsonReply(new JsonObject { ["status_code"] = 0, ["aweme_list"] = new JsonArray(PublicItem(src, posters)) });
await Test("Feed công khai đủ dữ liệu: không mở browser/tài khoản, không đăng ký khách khi chỉ đọc thời lượng", async () => {
    var src = Job().Source!; var requests = 0;
    using var http = new HttpClient(new ScriptedHandler(req => { requests++; Assert(req.Method == HttpMethod.Get && !req.Headers.Contains("Cookie") && req.Headers.Authorization is null); return Task.FromResult(PublicFeed(src)); }));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero);
    var guest = new RoutingBrowser(); var account = new RoutingBrowser();
    var reader = new DouyinDataReader(pub, guest, guest, account, account);
    var first = await reader.ReadVideoMetadataAsync(src, default, false); var second = await reader.ReadVideoMetadataAsync(src, default, false);
    Assert(first["source_duration_ms"]!.GetValue<long>() == 254200 && requests == 1 && second["id"]!.ToString() == src.Id && account.VideoReads == 0 && guest.VideoReads == 0);
});
await Test("Phiên khách cũ còn hạn được dùng lại sau mỗi lần mở app; không đọc cookie tài khoản", async () => {
    var src = Job().Source!; var secrets = new MemorySecrets(); var requests = 0;
    await secrets.WriteAsync("guest-douyin", JsonSerializer.Serialize(new { Token = "legacy-public-ticket", Expires = DateTimeOffset.UtcNow.AddHours(8) }));
    await secrets.WriteAsync("douyin-app-session", "account-must-not-read");
    using var http = new HttpClient(new ScriptedHandler(req => { requests++; Assert(req.Method == HttpMethod.Get); Assert(req.Headers.GetValues("Cookie").Single() == "ttwid=legacy-public-ticket"); return Task.FromResult(JsonReply(new JsonObject { ["status_code"] = 0, ["aweme_detail"] = PublicItem(src) })); }));
    for (var i = 0; i < 2; i++) { using var pub = new DouyinPublicReader(http, TimeSpan.Zero, secrets); Assert(await pub.ReadAsync(src, true, default) is not null); }
    Assert(requests == 2 && secrets.Reads.SequenceEqual(new[] { "guest-douyin", "guest-douyin" }));
});
await Test("Phiên khách hết hạn/hỏng chỉ đăng ký một lần rồi lưu bản mới cho lần mở sau", async () => {
    foreach (var saved in new[] { "bad-json", JsonSerializer.Serialize(new { Token = "expired", Expires = DateTimeOffset.UtcNow.AddSeconds(-1) }), JsonSerializer.Serialize(new { Token = "bad\r\nCookie", Expires = DateTimeOffset.UtcNow.AddHours(1) }) }) {
        var src = Job().Source!; var secrets = new MemorySecrets(); await secrets.WriteAsync("guest-douyin", saved); var registrations = 0;
        using var http = new HttpClient(new ScriptedHandler(req => {
            if (req.Method == HttpMethod.Post) { registrations++; var reply = new HttpResponseMessage(HttpStatusCode.OK); reply.Headers.TryAddWithoutValidation("Set-Cookie", "ttwid=new-public-ticket; Path=/"); return Task.FromResult(reply); }
            Assert(req.Headers.GetValues("Cookie").Single() == "ttwid=new-public-ticket"); return Task.FromResult(JsonReply(new JsonObject { ["status_code"] = 0, ["aweme_detail"] = PublicItem(src) }));
        }));
        for (var i = 0; i < 2; i++) { using var pub = new DouyinPublicReader(http, TimeSpan.Zero, secrets); Assert(await pub.ReadAsync(src, true, default) is not null); }
        Assert(registrations == 1 && secrets.Reads.All(key => key == "guest-douyin"));
    }
});
await Test("Đăng ký khách thất bại vẫn dùng feed và không đăng ký lại khi mở app ngay sau đó", async () => {
    var src = Job().Source!; var secrets = new MemorySecrets(); var registrations = 0;
    using var http = new HttpClient(new ScriptedHandler(req => {
        if (req.Method == HttpMethod.Post) { registrations++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)); }
        if (req.RequestUri!.Host == "www.douyin.com") { Assert(!req.Headers.Contains("Cookie")); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }); }
        Assert(!req.Headers.Contains("Cookie")); return Task.FromResult(PublicFeed(src));
    }));
    for (var i = 0; i < 2; i++) { using var pub = new DouyinPublicReader(http, TimeSpan.Zero, secrets); Assert(await pub.ReadAsync(src, true, default) is not null); }
    Assert(registrations == 1 && secrets.Reads.All(key => key == "guest-douyin"));
});
await Test("Chỉ đọc metadata công khai không mở kho phiên khách hoặc tài khoản", async () => {
    var src = Job().Source!; var secrets = new MemorySecrets();
    using var http = new HttpClient(new ScriptedHandler(req => Task.FromResult(PublicFeed(src))));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero, secrets); Assert(await pub.ReadAsync(src, false, default) is not null); Assert(secrets.Reads.Count == 0);
});
await Test("Guest web mất UIFID: feed miễn phí giữ original/2 bìa, không dùng session tài khoản; cache tránh lặp", async () => {
    var src = Job().Source!; var registered = 0; var web = 0; var feed = 0;
    using var http = new HttpClient(new ScriptedHandler(req => {
        Assert(req.Headers.Authorization is null);
        if (req.Method == HttpMethod.Post) { registered++; var result = new HttpResponseMessage(HttpStatusCode.OK); result.Headers.TryAddWithoutValidation("Set-Cookie", ["ttwid=public-visitor; Path=/", "sessionid=must-not-use; Path=/"]); return Task.FromResult(result); }
        if (req.RequestUri!.Host == "www.douyin.com") { web++; Assert(req.Headers.GetValues("Cookie").Single() == "ttwid=public-visitor"); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Blocked by ArgusSecurityPlugin Uifid Not Found") }); }
        feed++; Assert(!req.Headers.Contains("Cookie")); return Task.FromResult(PublicFeed(src));
    }));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser(); var account = new RoutingBrowser();
    var reader = new DouyinDataReader(pub, guest, guest, account, account);
    var info = await reader.ReadVideoMetadataAsync(src, default); await reader.ReadVideoMetadataAsync(src, default);
    Assert(registered == 1 && web == 1 && feed == 1 && guest.VideoReads == 0 && account.VideoReads == 0 && DouyinBrowserCapture.HasBothCovers(info));
    Assert(info["formats"]!.AsArray().Any(f => f!["format_id"]!.ToString().StartsWith("download_addr-original")) && pub.Author(src) == channelSec);
});
foreach (var scenario in new[] { "registration-blocked", "web-blocked", "first-feed-blocked", "web-no-formats", "no-duration" })
await Test("Tải công khai không cần tài khoản: " + scenario, async () => {
    var src = Job().Source!; var feedCalls = 0; var requests = 0;
    using var http = new HttpClient(new ScriptedHandler(req => {
        requests++; Assert(!req.Headers.Contains("Authorization"));
        if (req.Method == HttpMethod.Post) return Task.FromResult(new HttpResponseMessage(scenario == "registration-blocked" ? HttpStatusCode.Forbidden : HttpStatusCode.OK));
        if (req.RequestUri!.Host == "www.douyin.com") {
            if (scenario is "web-blocked" or "first-feed-blocked") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            if (scenario == "web-no-formats") return Task.FromResult(JsonReply(new JsonObject { ["status_code"] = 0, ["aweme_detail"] = new JsonObject { ["aweme_id"] = src.Id, ["duration"] = 240000 } }));
            return Task.FromResult(JsonReply(new JsonObject()));
        }
        Assert(!req.Headers.Contains("Cookie")); feedCalls++;
        if (scenario == "first-feed-blocked" && feedCalls == 1) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        var item = PublicItem(src);
        if (scenario == "no-duration") { item["duration"] = null; item["video"]!["duration"] = null; }
        return Task.FromResult(JsonReply(new JsonObject { ["status_code"] = 0, ["aweme_list"] = new JsonArray(item) }));
    }));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser(); var account = new RoutingBrowser();
    var info = await new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(src, default);
    Assert(info["formats"]!.AsArray().Count > 0 && guest.VideoReads == 0 && account.VideoReads == 0 && guest.ChannelReads == 0 && account.ChannelReads == 0);
    Assert(feedCalls == (scenario == "first-feed-blocked" ? 2 : 1) && requests <= 4);
    if (scenario == "no-duration") Assert(info["source_duration_ms"] is null);
});
await Test("Đọc thời lượng bằng khách trước không ngăn tải qua nguồn công khai sau", async () => {
    var src = Job().Source!; var available = false; var requests = 0;
    using var http = new HttpClient(new ScriptedHandler(req => {
        requests++; if (req.Method == HttpMethod.Post) return Task.FromResult(JsonReply(new JsonObject()));
        if (!available) return Task.FromResult(JsonReply(new JsonObject { ["captcha"] = new JsonObject() }));
        if (req.RequestUri!.Host == "www.douyin.com") return Task.FromResult(JsonReply(new JsonObject()));
        return Task.FromResult(PublicFeed(src));
    }));
    using var pub = new DouyinPublicReader(http,TimeSpan.Zero);
    var guest = new RoutingBrowser { Info = new JsonObject { ["id"] = src.Id, ["source_duration_ms"] = 100L } }; var account = new RoutingBrowser();
    var reader = new DouyinDataReader(pub,guest,guest,account,account);
    await reader.ReadVideoMetadataAsync(src,default,false); Assert(guest.VideoReads == 1);
    available = true; var previous = requests; var info = await reader.ReadVideoMetadataAsync(src,default,true);
    Assert(info["formats"]!.AsArray().Count > 0 && requests > previous && guest.VideoReads == 1 && account.VideoReads == 0);
});
await Test("Tải gặp 429 không thử nguồn tiếp hoặc phiên tài khoản", async () => {
    var requests = 0;
    using var http = new HttpClient(new ScriptedHandler(_ => { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)); }));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser(); var account = new RoutingBrowser();
    await Throws<NeedsActionException>(() => new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(Job().Source!, default));
    Assert(requests == 1 && guest.VideoReads == 0 && account.VideoReads == 0);
});
await Test("Nguồn công khai thiếu bìa: thử Select đúng ID, không dùng tài khoản, không thay rendition", async () => {
    var src = Job().Source!; var calls = 0;
    using var http = new HttpClient(new ScriptedHandler(req => {
        calls++; if (req.Method == HttpMethod.Post || req.RequestUri!.Host == "www.douyin.com") return Task.FromResult(JsonReply(new JsonObject()));
        var selected = req.RequestUri.Query.Contains("aid=568863"); var item = PublicItem(src, selected);
        if (selected) { item["video"]!["play_addr"] = null; item["video"]!["download_addr"] = new JsonObject { ["uri"] = "do-not-replace-formats" }; }
        return Task.FromResult(JsonReply(new JsonObject { ["status_code"] = 0, ["aweme_list"] = new JsonArray(PublicItem(new("wrong", src.CanonicalUrl)), item) }));
    }));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser(); var account = new RoutingBrowser();
    var info = await new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(src, default);
    Assert(DouyinBrowserCapture.HasBothCovers(info) && calls == 4 && account.VideoReads == 0 && info["formats"]!.ToJsonString().Contains("4k.mp4") && !info["formats"]!.ToJsonString().Contains("do-not-replace-formats"));
});
await Test("Bìa tùy chọn không đủ không kích hoạt phiên tài khoản", async () => {
    var src = Job().Source!;
    using var http = new HttpClient(new ScriptedHandler(req => Task.FromResult(req.Method == HttpMethod.Post || req.RequestUri!.Host == "www.douyin.com" ? JsonReply(new JsonObject()) : PublicFeed(src, false))));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser(); var account = new RoutingBrowser();
    var info = await new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(src, default);
    Assert(!DouyinBrowserCapture.HasBothCovers(info) && account.VideoReads == 0 && guest.VideoReads == 0);
});
foreach (var code in new[] { HttpStatusCode.TooManyRequests })
await Test("HTTP " + code + " dừng/cooldown, không mở tài khoản hoặc thử endpoint tiếp", async () => {
    var requests = 0; using var http = new HttpClient(new ScriptedHandler(_ => { requests++; return Task.FromResult(new HttpResponseMessage(code)); }));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser(); var account = new RoutingBrowser();
    var reader = new DouyinDataReader(pub, guest, guest, account, account);
    await Throws<NeedsActionException>(() => reader.ReadVideoMetadataAsync(Job().Source!, default, false));
    await Throws<NeedsActionException>(() => reader.ReadVideoMetadataAsync(Job().Source!, default, false));
    Assert(requests == 1 && guest.VideoReads == 0 && account.VideoReads == 0);
});
await Test("HTTP 403 hoặc JSON CAPTCHA mở bộ đọc khách, xác minh xong giữ dữ liệu; không dùng account", async () => {
    foreach (var forbidden in new[] { false, true }) {
        var src = Job().Source!; var requests = 0;
        using var http = new HttpClient(new ScriptedHandler(_ => { requests++; return Task.FromResult(forbidden ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Browser verification required") } : JsonReply(new JsonObject { ["captcha"] = new JsonObject() })); }));
        using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser { Info = new JsonObject { ["id"] = src.Id, ["source_duration_ms"] = 100L } }; var account = new RoutingBrowser();
        var reader = new DouyinDataReader(pub, guest, guest, account, account);
        Assert((await reader.ReadVideoMetadataAsync(src, default, false))["id"]!.ToString() == src.Id);
        await reader.ReadVideoMetadataAsync(src, default, false);
        Assert(requests == 1 && guest.VideoReads == 1 && account.VideoReads == 0);
    }
});
await Test("Nguồn bìa tùy chọn yêu cầu xác minh không mở browser/account khi video đã đủ dữ liệu", async () => {
    var src = Job().Source!;
    using var http = new HttpClient(new ScriptedHandler(req => {
        if (req.Method == HttpMethod.Post) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        if (req.RequestUri!.Host == "www.douyin.com") return Task.FromResult(JsonReply(new JsonObject()));
        if (req.RequestUri!.Query.Contains("aid=568863")) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Browser verification") });
        return Task.FromResult(PublicFeed(src, false));
    }));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser(); var account = new RoutingBrowser();
    var info = await new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(src, default);
    Assert(info["formats"]!.AsArray().Count > 0 && !DouyinBrowserCapture.HasBothCovers(info) && guest.VideoReads == 0 && account.VideoReads == 0);
});
await Test("HTTP cần xác minh nhưng người dùng đóng cửa sổ khách: không tự dùng tài khoản hoặc lặp HTTP", async () => {
    var requests = 0; using var http = new HttpClient(new ScriptedHandler(_ => { requests++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Browser verification") }); }));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser { Failure = new DouyinTerminalException("User closed") }; var account = new RoutingBrowser();
    var reader = new DouyinDataReader(pub, guest, guest, account, account);
    for (var i = 0; i < 2; i++) await Throws<DouyinTerminalException>(() => reader.ReadVideoMetadataAsync(Job().Source!, default, false));
    Assert(requests == 1 && guest.VideoReads == 2 && account.VideoReads == 0);
});
await Test("Tài khoản báo giới hạn khi là phương án cuối: dừng các lượt kế tiếp, không retry", async () => {
    using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser(); var account = new RoutingBrowser { Failure = new DouyinTerminalException("HTTP 429", true) };
    var reader = new DouyinDataReader(pub, guest, guest, account, account);
    await Throws<DouyinTerminalException>(() => reader.ReadVideoMetadataAsync(Job().Source!, default, false));
    await Throws<NeedsActionException>(() => reader.ReadVideoMetadataAsync(Job().Source!, default, false));
    Assert(account.VideoReads == 1 && guest.VideoReads == 0);
});
await Test("429 từ browser giữ Retry-After dài hơn mức chờ mặc định", async () => {
    var retryAt = DateTimeOffset.UtcNow.AddHours(2);
    using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(JsonReply(new JsonObject { ["captcha"] = new JsonObject() }))));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser { Failure = new DouyinTerminalException("HTTP 429", true, retryAt) }; var account = new RoutingBrowser();
    await Throws<DouyinTerminalException>(() => new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(Job().Source!, default, false));
    try { pub.EnsureAllowed(); throw new Exception("Rate limit lost"); } catch (NeedsActionException ex) { Assert(ex.Message.Contains(retryAt.ToLocalTime().ToString("HH:mm:ss"))); }
    Assert(account.VideoReads == 0 && guest.VideoReads == 1);
});
await Test("Chuyển ttwid khách sang browser không đăng ký mới, không đọc kho account", async () => {
    var secrets = new MemorySecrets(); await secrets.WriteAsync("guest-douyin", JsonSerializer.Serialize(new { Token = "guest-ticket", Expires = DateTimeOffset.UtcNow.AddHours(1) }));
    using var http = new HttpClient(new ScriptedHandler(_ => throw new Exception("Unexpected HTTP"))); using var pub = new DouyinPublicReader(http, TimeSpan.Zero, secrets);
    Assert(await pub.CachedGuestTokenAsync(default) == "guest-ticket" && await pub.CachedGuestTokenAsync(default) == "guest-ticket");
    Assert(secrets.Reads.SequenceEqual(new[] { "guest-douyin" }));
});
await Test("HTTP 200 trang xác minh HTML không bị nhầm thành metadata rỗng", async () => {
    var src = Job().Source!;
    using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<!doctype html><html><title>验证中心</title><body>请完成验证</body></html>") })));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero);
    var guest = new RoutingBrowser { Info = new JsonObject { ["id"] = src.Id, ["source_duration_ms"] = 100L } }; var account = new RoutingBrowser();
    await new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(src, default, false);
    Assert(guest.VideoReads == 1 && account.VideoReads == 0);
});
await Test("HTTP trống/sai ID/UIFID/lỗi mạng tự thử khách rồi tài khoản khi chưa có dữ liệu đúng video", async () => {
    var src = Job().Source!;
    foreach (var failure in new[] { "empty", "wrong-id", "uifid", "network", "403" }) {
        using var http = new HttpClient(new ScriptedHandler(req => {
            if (req.Method == HttpMethod.Post) return Task.FromResult(JsonReply(new JsonObject()));
            if (failure == "network") throw new HttpRequestException("offline");
            if (failure == "uifid" && req.RequestUri!.Host == "www.douyin.com") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Uifid Not Found") });
            if (failure == "403") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            return Task.FromResult(failure == "wrong-id" ? PublicFeed(new("wrong", src.CanonicalUrl)) : JsonReply(new JsonObject()));
        }));
        using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser(); var account = new RoutingBrowser();
        await Throws<NeedsActionException>(() => new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(src, default));
        Assert(guest.VideoReads == 1 && account.VideoReads == 1 && guest.ChannelReads == 0 && account.ChannelReads == 0);
    }
});
await Test("Alien 02: HTTP thiếu UIFID/feed sai ID, khách thiếu dữ liệu thì tự dùng tài khoản đúng video và cache", async () => {
    foreach (var download in new[] { false, true }) foreach (var missing in new[] { "timeout", "wrong-id", "empty" }) {
        var src = new SourceVideo("7686859235216428328", "https://www.douyin.com/video/7686859235216428328");
        var requests = 0;
        using var http = new HttpClient(new ScriptedHandler(req => {
            requests++; Assert(req.Headers.Authorization is null);
            if (req.Method == HttpMethod.Post) return Task.FromResult(JsonReply(new JsonObject()));
            return Task.FromResult(req.RequestUri!.Host == "www.douyin.com"
                ? new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("Uifid Not Found") }
                : PublicFeed(new("wrong-id", src.CanonicalUrl)));
        }));
        using var pub = new DouyinPublicReader(http, TimeSpan.Zero);
        var info = DouyinWebMetadata.Parse(PublicItem(src, false), src)!; info["source_duration_ms"] = 1189563L;
        var guest = new RoutingBrowser { Info = missing == "wrong-id" ? new JsonObject { ["id"] = "wrong", ["source_duration_ms"] = 100L } : new JsonObject(),
            Failure = missing == "timeout" ? new NeedsActionException("Guest metadata timeout") : null };
        var account = new RoutingBrowser { Info = info }; var notices = new List<string>();
        var reader = new DouyinDataReader(pub, guest, guest, account, account, notices.Add);
        Assert((await reader.ReadVideoMetadataAsync(src, default, download))["id"]!.ToString() == src.Id);
        var firstRequests = requests; await reader.ReadVideoMetadataAsync(src, default, download);
        Assert(account.VideoReads == 1 && guest.VideoReads == 1 && requests == firstRequests && notices.Count == 2);
        Assert(!DouyinBrowserCapture.HasBothCovers(info)); // Missing optional covers do not invalidate required video data.
    }
});
await Test("Douyin tự chuyển phiên vẫn từ chối tài khoản sai ID hoặc thiếu dữ liệu bắt buộc, không cache kết quả sai", async () => {
    foreach (var download in new[] { false, true }) foreach (var wrongId in new[] { false, true }) {
        var src = Job().Source!;
        using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(JsonReply(new JsonObject()))));
        using var pub = new DouyinPublicReader(http, TimeSpan.Zero);
        var info = wrongId ? new JsonObject { ["id"] = "other", ["source_duration_ms"] = 100L } : new JsonObject { ["id"] = src.Id };
        var guest = new RoutingBrowser(); var account = new RoutingBrowser { Info = info };
        var reader = new DouyinDataReader(pub, guest, guest, account, account);
        for (var i = 0; i < 2; i++) await Throws<NeedsActionException>(() => reader.ReadVideoMetadataAsync(src, default, download));
        Assert(account.VideoReads == 2 && guest.VideoReads == 2);
    }
});
await Test("Douyin tự chuyển phiên không vượt 429, CAPTCHA, video bị xóa hoặc hủy bởi người dùng", async () => {
    foreach (var failure in new Exception[] { new DouyinTerminalException("CAPTCHA", true), new DouyinTerminalException("Video unavailable"),
        new RetryLaterException("429", DateTimeOffset.UtcNow.AddHours(1), "douyin-metadata"), new OperationCanceledException() }) {
        using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(JsonReply(new JsonObject()))));
        using var pub = new DouyinPublicReader(http, TimeSpan.Zero);
        var guest = new RoutingBrowser { Failure = failure }; var account = new RoutingBrowser();
        try { await new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(Job().Source!, default, false); throw new Exception("Expected guest error"); }
        catch (Exception ex) when (ReferenceEquals(ex, failure)) { }
        Assert(guest.VideoReads == 1 && account.VideoReads == 0);
    }
});
await Test("Douyin tự chuyển phiên: tài khoản bị giới hạn sau khách thiếu dữ liệu thì dừng lượt kế tiếp", async () => {
    var requests = 0;
    using var http = new HttpClient(new ScriptedHandler(_ => { requests++; return Task.FromResult(JsonReply(new JsonObject())); }));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero);
    var guest = new RoutingBrowser { Failure = new NeedsActionException("Guest metadata timeout") };
    var account = new RoutingBrowser { Failure = new DouyinTerminalException("429", true, DateTimeOffset.UtcNow.AddHours(1)) };
    var reader = new DouyinDataReader(pub, guest, guest, account, account);
    await Throws<DouyinTerminalException>(() => reader.ReadVideoMetadataAsync(Job().Source!, default, false)); var firstRequests = requests;
    await Throws<RetryLaterException>(() => reader.ReadVideoMetadataAsync(Job().Source!, default, false));
    Assert(account.VideoReads == 1 && guest.VideoReads == 1 && requests == firstRequests);
});
await Test("Chỉ yêu cầu đăng nhập rõ ràng mới mở phiên tài khoản; CAPTCHA dùng khách", async () => {
    var src = Job().Source!;
    foreach (var login in new[] { false, true }) {
        using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(login ? new HttpResponseMessage(HttpStatusCode.Unauthorized) : JsonReply(new JsonObject { ["captcha"] = new JsonObject() }))));
        using var pub = new DouyinPublicReader(http, TimeSpan.Zero);
        var info = new JsonObject { ["id"] = src.Id, ["source_duration_ms"] = 100L };
        var guest = new RoutingBrowser { Info = info }; var account = new RoutingBrowser { Info = info };
        var notices = new List<string>(); var reader = new DouyinDataReader(pub, guest, guest, account, account, notices.Add);
        await reader.ReadVideoMetadataAsync(src, default, false);
        Assert(notices.Count == 1 && guest.VideoReads == (login ? 0 : 1) && account.VideoReads == (login ? 1 : 0));
    }
});
await Test("Xác minh khách xong vẫn thiếu metadata thì tự thử tài khoản, không hỏi duyệt", async () => {
    using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(JsonReply(new JsonObject { ["captcha"] = new JsonObject() }))));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser(); var account = new RoutingBrowser();
    await Throws<NeedsActionException>(() => new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(Job().Source!, default, false));
    Assert(guest.VideoReads == 1 && account.VideoReads == 1);
});
await Test("Web HTTP Select bổ sung bìa tác giả và giữ chất lượng trước đó, không browser", async () => {
    var src = Job().Source!; var selected = 0;
    using var http = new HttpClient(new ScriptedHandler(req => {
        if (req.Method == HttpMethod.Post) { var reply = JsonReply(new JsonObject()); reply.Headers.TryAddWithoutValidation("Set-Cookie", "ttwid=guest-ticket; Path=/"); return Task.FromResult(reply); }
        Assert(req.RequestUri!.Host == "www.douyin.com"); var select = req.RequestUri.Query.Contains("aid=568863"); if (select) selected++;
        return Task.FromResult(JsonReply(new JsonObject { ["status_code"] = 0, ["aweme_detail"] = PublicItem(src, select) }));
    }));
    using var pub = new DouyinPublicReader(http, TimeSpan.Zero); var guest = new RoutingBrowser(); var account = new RoutingBrowser();
    var info = await new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(src, default);
    Assert(DouyinBrowserCapture.HasBothCovers(info) && selected == 1 && guest.VideoReads == 0 && account.VideoReads == 0);
});
await Test("Web HTTP rỗng làm mới khách một lần, lưu giới hạn sau restart, không làm mới UIFID", async () => {
    var src = Job().Source!; var secrets = new MemorySecrets(); var registrations = 0; var web = 0;
    using var http = new HttpClient(new ScriptedHandler(req => {
        if (req.Method == HttpMethod.Post) { registrations++; var reply = JsonReply(new JsonObject()); reply.Headers.TryAddWithoutValidation("Set-Cookie", "ttwid=ticket-" + registrations + "; Path=/"); return Task.FromResult(reply); }
        if (req.RequestUri!.Host == "www.douyin.com") { web++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("") }); }
        return Task.FromResult(JsonReply(new JsonObject()));
    }));
    for (var run = 0; run < 2; run++) { using var pub = new DouyinPublicReader(http, TimeSpan.Zero, secrets); Assert(await pub.ReadAsync(src, true, default) is null); }
    Assert(registrations == 2 && web == 3 && secrets.Reads.All(x => x == "guest-douyin"));
});
await Test("Khách CAPTCHA hoặc người dùng đóng: không dùng account thay thế; CAPTCHA tạm ngừng truy cập", async () => {
    foreach (var blocked in new[] { false, true }) {
        using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(JsonReply(new JsonObject { ["captcha"] = new JsonObject() })))); using var pub = new DouyinPublicReader(http, TimeSpan.Zero);
        var guest = new RoutingBrowser { Failure = new DouyinTerminalException("Stopped", blocked) }; var account = new RoutingBrowser();
        await Throws<DouyinTerminalException>(() => new DouyinDataReader(pub, guest, guest, account, account).ReadVideoMetadataAsync(Job().Source!, default, false));
        Assert(account.VideoReads == 0); if (blocked) await Throws<NeedsActionException>(() => Sync(pub.EnsureAllowed)); else pub.EnsureAllowed();
    }
});
await Test("Kênh công khai trước, account cuối; dùng author đã đọc, không gửi cookie qua API", async () => {
    var src = Job().Source!;
    foreach (var guestMissing in new[] { false, true }) {
        using var http = new HttpClient(new ScriptedHandler(_ => Task.FromResult(PublicFeed(src)))); using var pub = new DouyinPublicReader(http, TimeSpan.Zero);
        await pub.ReadAsync(src, false, default);
        var snapshot = new DouyinChannelSnapshot(channelSec, "Channel", 28344, ChannelFixture().LatestVideos);
        var guest = new RoutingBrowser { Channel = guestMissing ? null : snapshot }; var account = new RoutingBrowser { Channel = snapshot };
        var result = await new DouyinDataReader(pub, guest, guest, account, account).ReadAsync(src, null, default);
        Assert(result.Followers == 28344 && guest.Hint == channelSec && account.ChannelReads == (guestMissing ? 1 : 0));
    }
});
await Test("CDN 429 dừng ngay, không lặp request tải và giữ partial", async () => {
    var requests = 0; var job = OwnedJob("cdn-rate-limit"); job.DownloadFormatId = "web-1";
    var dir = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "cdn-rate-state")); var path = Path.Combine(dir, "video.mp4");
    using var http = new HttpClient(new ScriptedHandler(req => { requests++; if (req.Method == HttpMethod.Head) return Task.FromResult(MediaReply(awaitFile(), 100)); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)); }));
    await Throws<NeedsActionException>(() => new MediaDownload(http, new MemorySecrets()).DownloadAsync(job, "https://v3.douyinvod.com/video.mp4", path, new DirectProgress<double>(_ => {}), default));
    Assert(requests == 2 && !File.Exists(path));
});
JsonNode CachedInfo(VideoJob job, string url = "https://v3.douyinvod.com/cached.mp4", string format = "web-1") => new JsonObject {
    ["id"] = job.Source!.Id, ["source_duration_ms"] = 240000L,
    ["formats"] = new JsonArray(new JsonObject { ["format_id"] = format, ["url"] = url, ["ext"] = "mp4", ["vcodec"] = "h264", ["acodec"] = "aac" }),
    ["thumbnails"] = new JsonArray(), ["http_headers"] = new JsonObject { ["Cookie"] = "must-not-persist" }, ["pageState"] = "must-not-persist"
};
await Test("Metadata tải được mã hóa, đọc lại sau restart và không lưu cookie/page state", async () => {
    var job = OwnedJob("cached-metadata"); var directory = Path.Combine(runRoot, "cached-vault");
    var info = CachedInfo(job); info["formats"]![0]!["http_headers"] = new JsonObject { ["Cookie"] = "must-not-persist" };
    await new DouyinDownloadMetadata(new DpapiSecretStore(directory)).WriteAsync(job, info, default);
    var saved = await new DouyinDownloadMetadata(new DpapiSecretStore(directory)).ReadAsync(job, default);
    Assert(saved is not null && saved["formats"]!.AsArray().Count == 1 && saved["http_headers"] is null && saved["pageState"] is null && saved["formats"]![0]!["http_headers"] is null);
    Assert(!Encoding.UTF8.GetString(await File.ReadAllBytesAsync(Path.Combine(directory, "source-" + job.Id + ".dpapi"))).Contains("cached.mp4"));
});
await Test("Tải lại sau xóa thủ công dùng metadata cũ, không dùng byte receipt hoặc browser", async () => {
    var job = OwnedJob("cached-redownload"); job.DownloadAttemptId = "previous"; job.DownloadFormatId = "web-1"; job.DownloadExtension = "mp4";
    var temp = Path.Combine(runRoot, "cached-redownload-state", "temp"); var state = Path.Combine(Path.GetDirectoryName(temp)!, "download-state");
    var folder = DownloadPolicy.ReserveDirectory(job, state); var path = Path.Combine(folder, Rules.FileStem(job.Name) + ".mp4");
    var vault = new MemorySecrets(); var cache = new DouyinDownloadMetadata(vault); await cache.WriteAsync(job, CachedInfo(job), default);
    await vault.WriteAsync("download-" + job.Id + "-previous", JsonSerializer.Serialize(new { SourceId = job.Source!.Id, FormatId = "web-1", Path = path, Validator = "source-v1", Length = 100 }));
    job.Receipts.Add(new(JobActions.Download, "previous", DateTimeOffset.UtcNow, folder, job.Media, null,null,null,null,null,null,0,false));
    job.DownloadAttemptId = "next"; job.Media = null; job.DownloadedBytes = 0;
    Assert(await cache.ReadAsync(job, default) is not null);
    var guest = new RoutingBrowser(); var account = new RoutingBrowser(); var queries = 0; var transfers = 0;
    using var publicHttp = new HttpClient(new ScriptedHandler(_ => { queries++; throw new Exception("Unexpected source query"); }));
    using var pub = new DouyinPublicReader(publicHttp, TimeSpan.Zero); var reader = new DouyinDataReader(pub, guest, guest, account, account);
    HttpClient Http() => new(new ScriptedHandler(req => {
        if (req.Method == HttpMethod.Head) return Task.FromResult(MediaReply(awaitFile(), 100));
        transfers++; Assert(req.Headers.Range!.Ranges.Single().From == 0);
        Assert(req.Headers.IfRange?.EntityTag?.Tag == "\"source-v1\"");
        return Task.FromResult(MediaReply(awaitFile(), 100, 0));
    }));
    var downloader = new DouyinSource("unused", temp, vault, reader, Http);
    // Transport is real; intentionally invalid MP4 is rejected only by the local probe.
    await Throws<NeedsActionException>(() => downloader.DownloadAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => {}), default));
    Assert(transfers == 1 && queries == 0 && guest.VideoReads == 0 && account.VideoReads == 0 && account.ChannelReads == 0 && guest.ChannelReads == 0);
    Assert(File.ReadAllBytes(path).SequenceEqual(awaitFile()) && await vault.ReadAsync("download-" + job.Id + "-next") is null);
    job.Receipts.Clear(); Assert(await cache.ReadAsync(job, default) is not null); // rebound to new attempt by transfer
    job.DownloadAttemptId = "unrelated"; Assert(await cache.ReadAsync(job, default) is null);
});
await Test("Bổ sung tải sau mất thư mục lưu receipt cũ và reset byte lượt mới, không Google", async () => {
    var job = OwnedJob("redownload-coordinator"); job.Actions = JobActions.Download; job.PendingActions = JobActions.None; job.State = JobState.Complete;
    job.DownloadAttemptId = "old"; job.DownloadDirectory = Path.Combine(job.Folder, "deleted"); job.Media = job.Media! with { Path = Path.Combine(job.DownloadDirectory, "missing.mp4") }; job.DownloadFormatId = "web-1"; job.DownloadExtension = "mp4";
    job.DownloadedBytes = job.DownloadTotalBytes = 100;
    var vault = new MemorySecrets(); var cache = new DouyinDownloadMetadata(vault); await cache.WriteAsync(job, CachedInfo(job), default);
    var db = new LocalStore(Path.Combine(runRoot, "redownload-coordinator-db")); await db.SaveAsync(job);
    var fake = new WorkflowFakes(Path.Combine(runRoot, "redownload-coordinator-files")); var coordinator = new JobCoordinator(db,fake,fake,fake);
    try {
        await coordinator.AddActionsAsync(job.Id, JobActions.Download, new AppSettings { VideoFolder = job.Folder }, ""); await FinishAutomatically(db,job.Id);
        var done = (await db.ListAsync()).Single();
        Assert(done.DownloadAttemptId != "old" && done.Receipts.Single().AttemptId == "old" && done.Receipts.Single().Media is not null);
        Assert(done.DownloadedBytes == 0 && done.DownloadTotalBytes == 0 && await cache.ReadAsync(done,default) is not null);
        Assert(fake.Downloads == 1 && fake.Writes == 0 && fake.Uploads == 0 && fake.MetadataReads == 0);
    } finally { await coordinator.StopAsync(); }
});
await Test("Metadata lượt cũ phải có receipt đúng lượt/thư mục/video và đúng nguồn", async () => {
    var job = OwnedJob("cached-receipt-binding"); job.DownloadAttemptId = "old"; job.DownloadDirectory = "original";
    var cache = new DouyinDownloadMetadata(new MemorySecrets()); await cache.WriteAsync(job, CachedInfo(job), default); job.DownloadAttemptId = "new";
    ActionReceipt Receipt(string attempt, string directory, MediaFile? file) => new(JobActions.Download,attempt,DateTimeOffset.UtcNow,directory,file,null,null,null,null,null,null,0,false);
    foreach (var receipt in new[] { Receipt("wrong","original",job.Media), Receipt("old","wrong",job.Media), Receipt("old","original",null) }) {
        job.Receipts.Clear(); job.Receipts.Add(receipt); Assert(await cache.ReadAsync(job, default) is null);
    }
    job.Receipts.Clear(); job.Receipts.Add(Receipt("old","original",job.Media)); Assert(await cache.ReadAsync(job, default) is not null);
    job.Source = new("different", "https://www.douyin.com/video/different"); Assert(await cache.ReadAsync(job, default) is null);
});
await Test("Metadata tải ràng buộc nguồn/lượt tải/thư mục và bỏ cache hỏng", async () => {
    var job = OwnedJob("cached-binding"); var vault = new MemorySecrets(); var cache = new DouyinDownloadMetadata(vault);
    await cache.WriteAsync(job, CachedInfo(job), default);
    job.DownloadAttemptId = "another"; Assert(await cache.ReadAsync(job, default) is null); job.DownloadAttemptId = null;
    job.DownloadDirectory = "another"; Assert(await cache.ReadAsync(job, default) is null); job.DownloadDirectory = null;
    var source = job.Source; job.Source = new("999", "https://www.douyin.com/video/999"); Assert(await cache.ReadAsync(job, default) is null); job.Source = source;
    await vault.WriteAsync("source-" + job.Id, "not JSON"); Assert(await cache.ReadAsync(job, default) is null);
    await cache.WriteAsync(job, CachedInfo(job), default); await cache.DeleteAsync(job, default); Assert(await cache.ReadAsync(job, default) is null);
});
foreach (var scenario in new[] { "valid", "expired", "rate-limited", "format-changed", "memory-cache" })
await Test("Tải tiếp từ metadata lưu, kịch bản " + scenario, async () => {
    var job = OwnedJob("cached-resume-" + scenario); job.Media = null; job.DownloadFormatId = "web-1"; job.DownloadExtension = "mp4";
    var privateTemp = Path.Combine(runRoot, "cached-state-" + scenario, "temp");
    var folder = DownloadPolicy.ReserveDirectory(job, Path.Combine(Path.GetDirectoryName(privateTemp)!, "download-state"));
    var path = Path.Combine(folder, Rules.FileStem(job.Name) + ".mp4"); await File.WriteAllBytesAsync(path + ".part", awaitFile()[..5]);
    var vault = new MemorySecrets(); if (scenario != "memory-cache") await new DouyinDownloadMetadata(vault).WriteAsync(job, CachedInfo(job), default);
    await vault.WriteAsync("download-" + job.Id, JsonSerializer.Serialize(new { SourceId = job.Source!.Id, FormatId = "web-1", Path = path, Validator = "\"source-v1\"", Length = 100 }));
    var reader = new RoutingBrowser { Info = CachedInfo(job, "https://v3.douyinvod.com/refreshed.mp4", scenario == "format-changed" ? "web-2" : "web-1") };
    var sourceRequests = 0; using var publicHttp = new HttpClient(new ScriptedHandler(_ => { sourceRequests++; throw new Exception("Must reuse in-memory source metadata"); }));
    using var publicReader = new DouyinPublicReader(publicHttp, TimeSpan.Zero); publicReader.Remember(job.Source!, CachedInfo(job));
    IDouyinVideoBrowserReader dataReader = scenario == "memory-cache" ? new DouyinDataReader(publicReader, reader, reader, reader, reader) : reader;
    var requests = new List<string>(); var notices = new List<string>();
    HttpClient Http() => new(new ScriptedHandler(req => {
        requests.Add(req.RequestUri!.AbsolutePath); Assert(req.Method == HttpMethod.Get && req.Headers.Range!.Ranges.Single().From == 5);
        if (req.RequestUri.AbsolutePath == "/cached.mp4" && scenario is not ("valid" or "memory-cache")) return Task.FromResult(new HttpResponseMessage(scenario == "rate-limited" ? HttpStatusCode.TooManyRequests : HttpStatusCode.Forbidden));
        Assert(req.Headers.GetValues("If-Range").Single() == "\"source-v1\""); return Task.FromResult(MediaReply(awaitFile()[5..], 100, 5));
    }));
    var downloader = new DouyinSource(Path.Combine(Directory.GetCurrentDirectory(), "tools"), privateTemp, vault, dataReader, Http, notices.Add);
    // Deliberately invalid media: transport must finish correctly, then ffprobe must reject it.
    await Throws<NeedsActionException>(() => downloader.DownloadAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => {}), default));
    Assert(reader.VideoReads == (scenario is "expired" or "format-changed" ? 1 : 0));
    Assert(reader.Forgets == reader.VideoReads);
    Assert(sourceRequests == 0);
    Assert(requests.Count == (scenario == "expired" ? 2 : 1));
    if (scenario is "rate-limited" or "format-changed") Assert((await File.ReadAllBytesAsync(path + ".part")).SequenceEqual(awaitFile()[..5]) && !File.Exists(path));
    else Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(awaitFile()) && !File.Exists(path + ".part"));
    Assert(notices.Count == (scenario is "expired" or "format-changed" ? 1 : 0));
});
await Test("File đã hoàn tất không mở browser lại chỉ vì thiếu cache metadata", async () => {
    var job = OwnedJob("cached-complete"); var temp = Path.Combine(runRoot, "cached-complete-state", "temp");
    var folder = DownloadPolicy.ReserveDirectory(job, Path.Combine(Path.GetDirectoryName(temp)!, "download-state"));
    var path = Path.Combine(folder, Rules.FileStem(job.Name) + ".mp4"); await File.WriteAllBytesAsync(path, awaitFile()); job.Media = job.Media! with { Path = path };
    var reader = new RoutingBrowser();
    var result = await new DouyinSource("unused", temp, new MemorySecrets(), reader).DownloadAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => {}), default);
    Assert(reader.VideoReads == 0 && result.Path == path && result.CoverStatus is not null);
});
await Test("Tải lại ảnh bìa đọc metadata mới dù cache rỗng, không yêu cầu video", async () => {
    var job = OwnedJob("covers-refresh-fresh"); var temp = Path.Combine(runRoot, "covers-refresh-state", "temp");
    var folder = DownloadPolicy.ReserveDirectory(job, Path.Combine(Path.GetDirectoryName(temp)!, "download-state"));
    var path = Path.Combine(folder, Rules.FileStem(job.Name) + ".mp4"); await File.WriteAllBytesAsync(path, awaitFile()); job.Media = job.Media! with { Path = path };
    var vault = new MemorySecrets(); var plans = new DouyinDownloadMetadata(vault); await plans.WriteAsync(job, CachedInfo(job), default);
    var fresh = CachedInfo(job); fresh["thumbnails"] = new JsonArray(new JsonObject { ["id"] = "source_cover", ["source_field"] = "cover", ["url"] = "https://p3.douyinpic.com/fresh-poster" });
    var reader = new RoutingBrowser { Info = fresh }; var requests = 0;
    HttpClient Http() => new(new ScriptedHandler(req => {
        requests++; Assert(req.Method == HttpMethod.Get && req.RequestUri!.AbsolutePath == "/fresh-poster");
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = req, Content = new ByteArrayContent(verticalPng) });
    }));
    var source = new DouyinSource(Path.GetFullPath("tools"), temp, vault, reader, Http);
    job.Media = await source.RefreshCoversAsync(job, () => Task.CompletedTask, default);
    Assert(reader.VideoReads == 1 && reader.Forgets == 1 && requests == 1 && job.Media.Covers!.Count == 1 && File.Exists(job.Media.CoverPath));
    Assert((await File.ReadAllBytesAsync(path)).SequenceEqual(awaitFile()) && (await plans.ReadAsync(job, default))!["thumbnails"]!.AsArray().Count == 1);
    // A canceled explicit retry must propagate and leave the video intact.
    using var canceled = new CancellationTokenSource(); canceled.Cancel();
    await Throws<OperationCanceledException>(() => source.RefreshCoversAsync(job, () => Task.CompletedTask, canceled.Token));
    Assert(reader.VideoReads == 1);
});
await Test("Xóa thư mục kết quả cũng xóa metadata tải riêng", async () => {
    var job = OwnedJob("cached-delete"); var temp = Path.Combine(runRoot, "cached-delete-state", "temp");
    DownloadPolicy.ReserveDirectory(job, Path.Combine(Path.GetDirectoryName(temp)!, "download-state"));
    var vault = new MemorySecrets(); await new DouyinDownloadMetadata(vault).WriteAsync(job, CachedInfo(job), default);
    await new DouyinSource("unused", temp, vault, new RoutingBrowser()).DeleteFolderAsync(job, () => Task.CompletedTask, default);
    Assert(await vault.ReadAsync("source-" + job.Id) is null);
});
await Test("Dòng Sheets kéo sang vị trí mới: tìm theo source ID, chỉ xóa dòng hiện tại", async () => {
    var job = Job(); job.SheetVerified = true; job.SheetRow = 1; job.Sheet = job.Sheet! with { Columns = new(0, [0,1,2,3]) };
    var removed = false; var posts = 0;
    var other = Row(Value("https://www.douyin.com/video/7659702794928147700"), Value("Video khác"), Value("Khác"), Value("Short"));
    var own = Row(Value(job.Source!.CanonicalUrl), Value(job.Name), Value(job.Niche), Value(job.Type));
    var svc = await Services(new ScriptedHandler(async req => {
        if (req.Method == HttpMethod.Post) {
            posts++; var body = JsonNode.Parse(await req.Content!.ReadAsStringAsync())!;
            var range = body["requests"]![0]!["deleteDimension"]!["range"]!;
            Assert(range["startIndex"]!.GetValue<int>() == 2 && range["endIndex"]!.GetValue<int>() == 3); removed = true;
            return JsonReply(new JsonObject());
        }
        if (req.RequestUri!.Query.Contains("includeGridData")) return JsonReply(new JsonObject { ["sheets"] = new JsonArray(removed ? Grid(other.DeepClone()) : Grid(other.DeepClone(), own.DeepClone())) });
        return JsonReply(JsonNode.Parse("""{"sheets":[{"properties":{"sheetId":7,"title":"Tab","sheetType":"GRID","gridProperties":{"rowCount":100,"columnCount":5}}}]}""")!);
    }));
    Assert(await svc.Sheets.FindExistingRowAsync(job, default) == 2);
    await svc.Sheets.DeleteSheetAsync(job, () => Task.CompletedTask, default); Assert(posts == 1 && job.SheetDeleted);
});
await Test("Cấu hình cột so sánh theo giá trị qua JSON, kể cả hash code", () => Sync(() => {
    var target = Job().Sheet! with { Columns = new(0, [0,1,2,3]), SourceChannelColumns = new(2, [4,3,2,1,0]) };
    var clone = JsonSerializer.Deserialize<SheetTarget>(JsonSerializer.Serialize(target))!;
    Assert(target == clone && target.GetHashCode() == clone.GetHashCode() && !ReferenceEquals(target.Columns!.Columns, clone.Columns!.Columns));
    Assert(target != clone with { Columns = new(0, [1,0,2,3]) });
}));
await Test("Đã xóa Sheets thủ công: cập nhật trạng thái, không gửi lệnh xóa và cho bổ sung", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "outside-sheet")); var job = Job(); job.State = JobState.Complete; job.SheetVerified = true; job.SheetRow = 2; job.Actions = JobActions.WriteSheet; job.PendingActions = JobActions.None; await db.SaveAsync(job);
    var fake = new Fakes { MissingSheet = true }; var coordinator = new JobCoordinator(db, fake, fake, fake);
    var result = await coordinator.RecheckResultsAsync(job.Id); Assert(result.SheetDeleted && result.Addable.HasFlag(JobActions.WriteSheet) && fake.SheetDeletes == 0 && fake.VideoDeletes == 0);
    Assert((await db.ListAsync()).Single().SheetDeleted);
});
await Test("Mất quyền đọc không coi Sheets/YouTube đã bị xóa", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "outside-auth")); var job = Job(); job.State = JobState.Complete; job.SheetVerified = true; job.YouTubeId = "existing"; await db.SaveAsync(job);
    var fake = new Fakes { InspectionAuthFailure = true }; var result = await new JobCoordinator(db, fake, fake, fake).RecheckResultsAsync(job.Id);
    Assert(!result.SheetDeleted && !result.YouTubeDeleted && result.Message.Contains("Chưa kiểm tra"));
});
await Test("YouTube tự cập nhật đã xóa thủ công: không xác nhận/DELETE, cho bổ sung", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "outside-youtube")); var job = Job(); job.State = JobState.Complete; job.YouTubeId = "existing"; job.YouTubeVerified = true; job.Actions = JobActions.All; job.PendingActions = JobActions.None; await db.SaveAsync(job);
    var fake = new Fakes { MissingYouTube = true }; var result = await new JobCoordinator(db, fake, fake, fake).RecheckResultsAsync(job.Id);
    Assert(result.YouTubeDeleted && result.Addable.HasFlag(JobActions.UploadYouTube) && !result.Results.HasFlag(JobActions.UploadYouTube) && fake.VideoDeletes == 0);
    Assert((await db.ListAsync()).Single().YouTubeDeleted);
});
await Test("YouTube tự cập nhật giữ kết quả khi video vẫn tồn tại", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "outside-youtube-exists")); var job = Job(); job.State = JobState.Complete; job.YouTubeId = "existing"; await db.SaveAsync(job);
    var fake = new Fakes(); var result = await new JobCoordinator(db, fake, fake, fake).RecheckResultsAsync(job.Id);
    Assert(!result.YouTubeDeleted && fake.VideoDeletes == 0);
});
await Test("Đọc trạng thái YouTube chỉ GET, xác minh đúng kênh và không đoán ID", async () => {
    var job = Job(); job.YouTubeId = "video-id"; var empty = false; var wrong = false;
    var svc = await Services(new ScriptedHandler(req => {
        Assert(req.Method == HttpMethod.Get);
        return Task.FromResult(JsonReply(req.RequestUri!.AbsolutePath.EndsWith("/channels")
            ? JsonNode.Parse("""{"items":[{"id":"channel-id","snippet":{"title":"Kênh"}}]}""")!
            : empty ? new JsonObject { ["items"] = new JsonArray() } : JsonNode.Parse(wrong ? """{"items":[{"id":"video-id","snippet":{"channelId":"wrong"}}]}""" : """{"items":[{"id":"video-id","snippet":{"channelId":"channel-id"}}]}""")!));
    }));
    Assert(await svc.YouTube.ExistsAsync(job, default)); empty = true; Assert(!await svc.YouTube.ExistsAsync(job, default));
    empty = false; wrong = true; await Throws<NeedsActionException>(() => svc.YouTube.ExistsAsync(job, default));
});
foreach (var change in new[] { "moved", "changed", "extra", "missing-video", "changed-cover" })
await Test("Thư mục chuyển đích: " + change, async () => {
    var job = OwnedJob("move-old-" + change); var state = Path.Combine(runRoot, "move-state-" + change); var old = DownloadPolicy.ReserveDirectory(job, state);
    var oldFile = Path.Combine(old, Rules.FileStem(job.Name) + ".mp4"); await File.WriteAllBytesAsync(oldFile, awaitFile()); job.Media = job.Media! with { Path = oldFile };
    var poster = Path.Combine(old, "poster.webp"); await File.WriteAllBytesAsync(poster, [1,2,3]);
    job.Media = job.Media with { CoverPath = poster, Covers = [new(poster, 100, 200, "fixture", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 1,2,3 })))] };
    var parent = Path.Combine(runRoot, "move-new-" + change); Directory.CreateDirectory(parent); var moved = Path.Combine(parent, Path.GetFileName(old));
    // Both paths are explicit test-owned children of runRoot.
    Assert(Path.GetFullPath(old).StartsWith(runRoot + Path.DirectorySeparatorChar) && Path.GetFullPath(moved).StartsWith(runRoot + Path.DirectorySeparatorChar)); Directory.Move(old, moved);
    var movedFile = Path.Combine(moved, Path.GetFileName(oldFile));
    if (change == "changed") await File.WriteAllBytesAsync(movedFile, new byte[100]);
    if (change == "extra") await File.WriteAllTextAsync(Path.Combine(moved, "unrelated.txt"), "keep");
    if (change == "missing-video") File.Delete(movedFile);
    if (change == "changed-cover") await File.WriteAllBytesAsync(Path.Combine(moved, "poster.webp"), [3,2,1]);
    var vault = new MemorySecrets(); await new DouyinDownloadMetadata(vault).WriteAsync(job, CachedInfo(job), default);
    await Throws<NeedsActionException>(() => OwnedDownloadDirectory.DeleteAsync(job, state, () => Task.CompletedTask, default)); Assert(Directory.Exists(moved) && !job.FolderDeleted);
    var checkpoints = 0;
    if (change == "moved") {
        await MovedDownloadDirectory.RelinkAsync(job, moved, state, vault, () => { checkpoints++; return Task.CompletedTask; }, default);
        Assert(checkpoints == 1 && job.Media!.Path == movedFile && job.Folder == parent && OwnedDownloadDirectory.Validate(job, state) == moved);
        Assert(job.Media!.Covers![0].Path == Path.Combine(moved, "poster.webp") && job.Media.CoverPath == job.Media.Covers[0].Path);
        Assert(await new DouyinDownloadMetadata(vault).ReadAsync(job, default) is not null);
        await OwnedDownloadDirectory.DeleteAsync(job, state, () => Task.CompletedTask, default); Assert(!Directory.Exists(moved) && job.FolderDeleted);
    } else {
        await Throws<NeedsActionException>(() => MovedDownloadDirectory.RelinkAsync(job, moved, state, vault, () => { checkpoints++; return Task.CompletedTask; }, default));
        Assert(checkpoints == 0 && Directory.Exists(moved) && job.DownloadDirectory == old && job.Media!.Path == oldFile);
    }
});
await Test("Thư mục tải dở bị chuyển: tiếp tục không tạo thư mục cũ hoặc tải lại từ đầu", async () => {
    var job = OwnedJob("moved-partial"); job.Media = null;
    var temp = Path.Combine(runRoot, "moved-partial-state", "temp"); var old = DownloadPolicy.ReserveDirectory(job, Path.Combine(Path.GetDirectoryName(temp)!, "download-state"));
    var partial = Path.Combine(old, "video.mp4.part"); await File.WriteAllBytesAsync(partial, [1,2,3,4]);
    var parent = Path.Combine(runRoot, "moved-partial-destination"); Directory.CreateDirectory(parent); var next = Path.Combine(parent, Path.GetFileName(old));
    Assert(Path.GetFullPath(old).StartsWith(runRoot + Path.DirectorySeparatorChar) && Path.GetFullPath(next).StartsWith(runRoot + Path.DirectorySeparatorChar)); Directory.Move(old, next);
    var reader = new RoutingBrowser(); var source = new DouyinSource("unused", temp, new MemorySecrets(), reader);
    await Throws<NeedsActionException>(() => source.DownloadAsync(job, () => Task.CompletedTask, new DirectProgress<double>(_ => {}), default));
    Assert(!Directory.Exists(old) && (await File.ReadAllBytesAsync(Path.Combine(next, "video.mp4.part"))).SequenceEqual(new byte[] {1,2,3,4}) && reader.VideoReads == 0);
});
await Test("Xóa file video thủ công: thư mục/bìa vẫn là kết quả, có thể tải bổ sung", async () => {
    var job = OwnedJob("manual-file"); var dir = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "manual-file-state"));
    job.Media = job.Media! with { Path = Path.Combine(dir, "removed.mp4") }; job.State = JobState.Complete; job.Actions = JobActions.Download; job.PendingActions = JobActions.None;
    Assert(job.MissingDownload && job.Results.HasFlag(JobActions.Download) && job.Addable.HasFlag(JobActions.Download));
});
await Test("Xác nhận folder đã xóa thủ công cần thao tác rõ ràng; không tạo folder thay thế", async () => {
    var job = OwnedJob("manual-folder"); var state = Path.Combine(runRoot, "manual-folder-state"); var dir = DownloadPolicy.ReserveDirectory(job, state);
    Assert(Path.GetFullPath(dir).StartsWith(runRoot + Path.DirectorySeparatorChar)); Directory.Delete(dir);
    await Throws<NeedsActionException>(() => OwnedDownloadDirectory.DeleteAsync(job, state, () => Task.CompletedTask, default));
    job.FolderAbsenceConfirmed = true; await OwnedDownloadDirectory.DeleteAsync(job, state, () => Task.CompletedTask, default);
    Assert(job.FolderDeleted && !Directory.Exists(dir));
});
await Test("Xóa ảnh bìa thủ công: báo thiếu bìa, giữ video và cho tải bìa bổ sung", async () => {
    var job = OwnedJob("manual-cover"); var dir = DownloadPolicy.ReserveDirectory(job, Path.Combine(runRoot, "manual-cover-state"));
    var video = Path.Combine(dir, "video.mp4"); await File.WriteAllBytesAsync(video, new byte[] {1,2,3});
    job.Media = job.Media! with { Path = video, Covers = [new(Path.Combine(dir, "missing.jpg"), 640, 480, "fixture", "horizontal")] };
    job.State = JobState.Complete; job.Actions = JobActions.Download; job.PendingActions = JobActions.None;
    var db = new LocalStore(Path.Combine(runRoot, "outside-cover")); await db.SaveAsync(job); var fake = new Fakes();
    var result = await new JobCoordinator(db, fake, fake, fake).RecheckResultsAsync(job.Id);
    Assert(result.Message.Contains("Ảnh bìa đã mất") && !result.MissingDownload && result.CanRefreshCovers && result.Results.HasFlag(JobActions.Download));
});

(VideoJob Job, string State, string Old, string File) AutoFixture(string label)
{
    var job = OwnedJob("auto-old-" + label); var state = Path.Combine(runRoot, "auto-state-" + label);
    var old = DownloadPolicy.ReserveDirectory(job, state); var file = Path.Combine(old, Rules.FileStem(job.Name) + ".mp4");
    File.WriteAllBytes(file, awaitFile()); job.Media = job.Media! with { Path = file }; job.VideoIdentity = WindowsFileIdentity.Capture(file);
    return (job, state, old, file);
}
string MoveFixture(string old, string label)
{
    var parent = Path.Combine(runRoot, "auto-new-" + label); Directory.CreateDirectory(parent); var next = Path.Combine(parent, Path.GetFileName(old));
    Assert(Path.GetFullPath(old).StartsWith(runRoot + Path.DirectorySeparatorChar) && Path.GetFullPath(next).StartsWith(runRoot + Path.DirectorySeparatorChar));
    Directory.Move(old, next); return next;
}
await Test("Windows File ID tìm lại folder/file sau di chuyển thật, không hash/quét", () => Sync(() => {
    var f = AutoFixture("identity"); Assert(f.Job.DirectoryIdentity is not null && f.Job.VideoIdentity is not null, "Cannot capture native file identity");
    var next = MoveFixture(f.Old, "identity");
    Assert(WindowsFileIdentity.Locate(f.Job.DirectoryIdentity) == next, "Cannot locate native moved directory");
    Assert(WindowsFileIdentity.Locate(f.Job.VideoIdentity) == Path.Combine(next, Path.GetFileName(f.File)), "Cannot locate native moved file");
    Assert(WindowsFileIdentity.Locate(f.Job.DirectoryIdentity! with { Created = 1 }) is null);
}));
await Test("Tự tìm bằng ID: chuyển ngoài phạm vi vẫn liên kết; không gọi index", async () => {
    var f = AutoFixture("native"); var next = MoveFixture(f.Old, "native"); var saves = 0;
    var found = await new MovedFolderFinder(f.State, new MemorySecrets(), (_, _) => throw new Exception("Unneeded index query")).FindAsync(f.Job, [], () => { saves++; return Task.CompletedTask; }, default);
    Assert(f.Job.DownloadDirectory == next && saves == 1 && found!.Contains("mã định danh") && !f.Job.MissingDownload);
});
await Test("Tự tìm công việc cũ chưa có ID: đối chiếu một bản khớp trong đích đã dùng", async () => {
    var f = AutoFixture("legacy"); var next = MoveFixture(f.Old, "legacy"); f.Job.DirectoryIdentity = null; f.Job.VideoIdentity = null;
    var reads = 0; var found = await new MovedFolderFinder(f.State, new MemorySecrets(), (_, _) => { reads++; return []; }).FindAsync(f.Job, [Path.GetDirectoryName(next)!], () => Task.CompletedTask, default);
    Assert(f.Job.DownloadDirectory == next && !f.Job.MissingDownload && reads == 1 && found!.Contains("SHA-256"));
});
await Test("Tự tìm đích gốc ổ khác chưa dùng và chưa index: chỉ kiểm tra đúng tên", async () => {
    var f = AutoFixture("drive-root"); var next = MoveFixture(f.Old, "drive-root"); f.Job.DirectoryIdentity = null; f.Job.VideoIdentity = null;
    var drive = Path.GetDirectoryName(next)!;
    // This same-name nested copy must not be visited by the drive-root probe.
    var nested = Path.Combine(drive, "unrelated", Path.GetFileName(next)); Directory.CreateDirectory(nested);
    await File.WriteAllBytesAsync(Path.Combine(nested, Path.GetFileName(f.File)), awaitFile());
    var found = await new MovedFolderFinder(f.State, new MemorySecrets(), (_, _) => [], () => [drive]).FindAsync(f.Job, [], () => Task.CompletedTask, default);
    Assert(f.Job.DownloadDirectory == next && !f.Job.MissingDownload && found!.Contains("SHA-256"));
});
await Test("Tự tìm đích gốc ổ khác có nhiều bản khớp: yêu cầu chọn, không liên kết nhầm", async () => {
    var f = AutoFixture("drive-copies"); var next = MoveFixture(f.Old, "drive-copies"); f.Job.DirectoryIdentity = null; f.Job.VideoIdentity = null;
    var secondRoot = Path.Combine(runRoot, "second-drive"); var copy = Path.Combine(secondRoot, Path.GetFileName(next)); Directory.CreateDirectory(copy);
    await File.WriteAllBytesAsync(Path.Combine(copy, Path.GetFileName(f.File)), awaitFile());
    var found = await new MovedFolderFinder(f.State, new MemorySecrets(), (_, _) => [], () => [Path.GetDirectoryName(next)!, secondRoot]).FindAsync(f.Job, [], () => throw new Exception("Ambiguous"), default);
    Assert(f.Job.DownloadDirectory == f.Old && found!.Contains("nhiều bản") && !File.Exists(DownloadPolicy.OwnershipMarker(next, f.State)));
});
await Test("Tự tìm đích gốc ổ khác từ chối bản cùng tên sai hash", async () => {
    var f = AutoFixture("drive-wrong"); var next = MoveFixture(f.Old, "drive-wrong"); f.Job.DirectoryIdentity = null; f.Job.VideoIdentity = null;
    await File.WriteAllBytesAsync(Path.Combine(next, Path.GetFileName(f.File)), new byte[100]);
    var found = await new MovedFolderFinder(f.State, new MemorySecrets(), (_, _) => [], () => [Path.GetDirectoryName(next)!]).FindAsync(f.Job, [], () => throw new Exception("Wrong contents"), default);
    Assert(f.Job.DownloadDirectory == f.Old && found!.Contains("Chưa tìm"));
});
await Test("Tự tìm copy-delete khác volume thật: mã cũ mất, liên kết bằng nội dung", async () => {
    var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TimVideoHay-cross-volume-" + Guid.NewGuid().ToString("N")));
    Assert(tempRoot.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    var job = OwnedJob("cross-volume"); job.Folder = tempRoot;
    var state = Path.Combine(runRoot, "cross-volume-state"); var old = DownloadPolicy.ReserveDirectory(job, state);
    var file = Path.Combine(old, Rules.FileStem(job.Name) + ".mp4"); await File.WriteAllBytesAsync(file, awaitFile());
    job.Media = job.Media! with { Path = file }; job.VideoIdentity = WindowsFileIdentity.Capture(file);
    try
    {
        var drive = Path.Combine(runRoot, "cross-volume-target"); var next = Path.Combine(drive, Path.GetFileName(old)); Directory.CreateDirectory(next);
        File.Copy(file, Path.Combine(next, Path.GetFileName(file))); File.Delete(file); Directory.Delete(old);
        Assert(WindowsFileIdentity.Locate(job.DirectoryIdentity) is null && WindowsFileIdentity.Locate(job.VideoIdentity) is null);
        var found = await new MovedFolderFinder(state, new MemorySecrets(), (_, _) => [], () => [drive]).FindAsync(job, [], () => Task.CompletedTask, default);
        Assert(job.DownloadDirectory == next && !job.MissingDownload && found!.Contains("SHA-256"));
        await File.WriteAllTextAsync(Path.Combine(root, "cross-volume.json"), JsonSerializer.Serialize(new { oldVolume = Path.GetPathRoot(tempRoot), newVolume = Path.GetPathRoot(next), differentVolumes = Path.GetPathRoot(tempRoot) != Path.GetPathRoot(next), contentsMatched = true, actualUserDataChanged = false }));
    }
    finally { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true); }
});
await Test("Tự tìm index hỗ trợ đích khác, hash sai không nhận nhầm", async () => {
    var f = AutoFixture("indexed"); var next = MoveFixture(f.Old, "indexed"); f.Job.DirectoryIdentity = null; f.Job.VideoIdentity = null;
    var wrong = Path.Combine(runRoot, "auto-index-wrong", Path.GetFileName(next)); Directory.CreateDirectory(wrong);
    await File.WriteAllBytesAsync(Path.Combine(wrong, Path.GetFileName(f.File)), new byte[100]);
    var found = await new MovedFolderFinder(f.State, new MemorySecrets(), (_, _) => [wrong, next]).FindAsync(f.Job, [], () => Task.CompletedTask, default);
    Assert(f.Job.DownloadDirectory == next && found!.Contains("SHA-256"));
});
await Test("Tự tìm nhiều bản giống nhau: không nhận quyền quản lý/xóa nhầm", async () => {
    var f = AutoFixture("ambiguous"); var next = MoveFixture(f.Old, "ambiguous"); f.Job.DirectoryIdentity = null; f.Job.VideoIdentity = null;
    var copy = Path.Combine(runRoot, "auto-copy", Path.GetFileName(next)); Directory.CreateDirectory(copy); await File.WriteAllBytesAsync(Path.Combine(copy, Path.GetFileName(f.File)), awaitFile());
    var saves = 0; var found = await new MovedFolderFinder(f.State, new MemorySecrets(), (_, _) => [next, copy]).FindAsync(f.Job, [], () => { saves++; return Task.CompletedTask; }, default);
    Assert(f.Job.DownloadDirectory == f.Old && saves == 0 && found!.Contains("nhiều bản") && !File.Exists(DownloadPolicy.OwnershipMarker(next, f.State)));
});
await Test("Tự tìm có thể hủy và không tìm khi thư mục cũ vẫn tồn tại", async () => {
    var f = AutoFixture("cancel"); var calls = 0; var finder = new MovedFolderFinder(f.State, new MemorySecrets(), (_, _) => { calls++; return []; });
    Assert(await finder.FindAsync(f.Job, [], () => Task.CompletedTask, default) is null && calls == 0);
    using var cancel = new CancellationTokenSource(); cancel.Cancel();
    await Throws<OperationCanceledException>(() => finder.FindAsync(f.Job, [], () => Task.CompletedTask, cancel.Token));
});
await Test("Tự tìm từ chối folder trong thùng rác và folder có file lạ", async () => {
    var f = AutoFixture("trash"); var next = MoveFixture(f.Old, "trash"); f.Job.DirectoryIdentity = null; f.Job.VideoIdentity = null;
    await File.WriteAllTextAsync(Path.Combine(next, "unrelated.txt"), "keep");
    var trash = Path.Combine(runRoot, "$Recycle.Bin", Path.GetFileName(next)); Directory.CreateDirectory(trash); await File.WriteAllBytesAsync(Path.Combine(trash, Path.GetFileName(f.File)), awaitFile());
    var found = await new MovedFolderFinder(f.State, new MemorySecrets(), (_, _) => [next, trash]).FindAsync(f.Job, [], () => throw new Exception("Must not adopt"), default);
    Assert(f.Job.DownloadDirectory == f.Old && found!.Contains("Chưa tìm"));
});
await Test("Tự tìm điều phối: cập nhật đường dẫn/ID bền vững trong SQLite, không Google writes", async () => {
    var f = AutoFixture("coordinator"); var next = MoveFixture(f.Old, "coordinator");
    f.Job.State = JobState.Complete; f.Job.Actions = JobActions.Download; f.Job.PendingActions = JobActions.None;
    var db = new LocalStore(Path.Combine(runRoot, "auto-coordinator-db")); await db.SaveAsync(f.Job);
    var fake = new Fakes { FindFolder = (job, roots, save, ct) => new MovedFolderFinder(f.State, new MemorySecrets(), (_, _) => throw new Exception("Not needed")).FindAsync(job, roots, save, ct) };
    var result = await new JobCoordinator(db, fake, fake, fake).RecheckResultsAsync(f.Job.Id);
    var durable = (await db.ListAsync()).Single();
    Assert(result.DownloadDirectory == next && durable.DownloadDirectory == next && durable.DirectoryIdentity is not null && !durable.MissingDownload && fake.Writes == 0 && fake.Uploads == 0 && fake.VideoDeletes == 0);
});
await Test("Tự tìm phục hồi từ receipt khi đã chọn tải lại vì mất file", async () => {
    var f = AutoFixture("receipt"); var next = MoveFixture(f.Old, "receipt");
    f.Job.Receipts.Add(new(JobActions.Download, "previous", DateTimeOffset.UtcNow, f.Old, f.Job.Media, null, null, null, null, null, null, 0, false));
    f.Job.Media = null; f.Job.VideoIdentity = null;
    var found = await new MovedFolderFinder(f.State, new MemorySecrets(), (_, _) => throw new Exception("Not needed")).FindAsync(f.Job, [], () => Task.CompletedTask, default);
    Assert(f.Job.DownloadDirectory == next && f.Job.Media is not null && !f.Job.MissingDownload && found!.Contains("mã định danh"));
});
await Test("Lịch sử cập nhật công việc sau bổ sung/xóa, không sửa lựa chọn gốc", () => Sync(() => {
    var job = new VideoJob { Actions = JobActions.Download, PendingActions = JobActions.None, State = JobState.Complete };
    Assert(job.ModeLabel == "Tải"); job.Actions = JobActions.All;
    Assert(job.ModeLabel == "Tải + Bảng + YouTube"); job.SheetDeleted = true;
    Assert(job.ModeLabel == "Tải + YouTube" && job.SelectedActions == JobActions.All);
    job.YouTubeDeleted = true; Assert(job.ModeLabel == "Tải"); job.FolderDeleted = true;
    Assert(job.CurrentActions == JobActions.None && job.ModeLabel == "");
    job.SheetDeleted = false; job.PendingActions = JobActions.WriteSheet; job.State = JobState.Queued;
    Assert(job.ModeLabel == "Bảng");
    var roundtrip = JsonSerializer.Deserialize<VideoJob>(JsonSerializer.Serialize(job), LocalStore.Json)!;
    Assert(roundtrip.ModeLabel == "Bảng");
}));
await Test("Lịch sử lọc phần đã xóa: chỉ tải đã xóa, xóa cả ba, kết hợp phần còn lại", () => Sync(() => {
    var onlyDownload = new VideoJob { Actions = JobActions.Download, FolderDeleted = true, State = JobState.Complete };
    var allDeleted = new VideoJob { Actions = JobActions.All, FolderDeleted = true, SheetDeleted = true, YouTubeDeleted = true, State = JobState.Complete };
    var remaining = new VideoJob { Actions = JobActions.All, FolderDeleted = true, State = JobState.Complete };
    var pending = new VideoJob { Actions = JobActions.Download, PendingActions = JobActions.Download, State = JobState.Queued };
    var hidden = new VideoJob { Actions = JobActions.Download, FolderDeleted = true, RemovedFromHistory = true };
    var jobs = new[] { onlyDownload, allDeleted, remaining, pending, hidden };
    Assert(onlyDownload.ModeLabel == "" && allDeleted.ModeLabel == "" && remaining.ModeLabel == "Bảng + YouTube");
    Assert(JobHistory.ProjectSelected(jobs, JobActions.None, [], includeEmptyActions: true).Select(j => j.Id).ToHashSet().SetEquals([onlyDownload.Id, allDeleted.Id]));
    Assert(JobHistory.ProjectSelected(jobs, JobActions.WriteSheet | JobActions.UploadYouTube, [], requireAllActions: true, includeEmptyActions: true).Length == 3);
    Assert(JobHistory.ProjectSelected(jobs, JobActions.None, [HistoryState.Queued], includeEmptyActions: true).Length == 0);
    onlyDownload.FolderDeleted = false; onlyDownload.PendingActions = JobActions.Download; onlyDownload.State = JobState.Queued;
    Assert(JobHistory.ProjectSelected(jobs, JobActions.None, [], includeEmptyActions: true).Single().Id == allDeleted.Id);
}));
await Test("Lịch sử lọc theo phần hiện còn, trạng thái và tên/link/ngách", () => Sync(() => {
    var a = new VideoJob { Name = "Hành trình vũ trụ", Niche = "Thiên văn", Input = "https://www.douyin.com/video/7659702794928147739", Actions = JobActions.All, SheetDeleted = true, State = JobState.NeedsAuth };
    var b = new VideoJob { Name = "Video hoàn tất", Actions = JobActions.WriteSheet, State = JobState.Complete };
    var c = new VideoJob { Actions = JobActions.WriteSheet, State = JobState.Failed };
    var hidden = new VideoJob { Actions = JobActions.All, RemovedFromHistory = true, State = JobState.Queued };
    Assert(JobHistory.Project([a,b,c,hidden], JobActions.WriteSheet).Select(j => j.Id).ToHashSet().SetEquals([b.Id,c.Id]));
    Assert(JobHistory.Project([a,b,c,hidden], state: HistoryState.Attention).Length == 2);
    Assert(JobHistory.Project([a,b,c], search: "HÀNH thiên").Single().Id == a.Id);
    Assert(JobHistory.Project([a,b,c], search: "7659702794928147739").Single().Id == a.Id);
    Assert(JobHistory.Project([a,b,c], state: HistoryState.Unfinished).Length == 2);
}));
await Test("Lịch sử hàng chờ 300 mục: thứ tự worker, bộ lọc không đánh lại số", () => Sync(() => {
    var time = DateTimeOffset.UtcNow.AddDays(-1);
    var queued = Enumerable.Range(1, 300).Select(i => new VideoJob { Id = i.ToString("D4"), Name = "Video " + i, Actions = i % 2 == 0 ? JobActions.All : JobActions.WriteSheet,
        State = JobState.Queued, Created = time.AddMinutes(301 - i), EnqueuedAt = time.AddSeconds(i) }).ToArray();
    var running = new VideoJob { State = JobState.Running }; var waiting = new VideoJob { State = JobState.WaitingForYouTube };
    var complete = new VideoJob { State = JobState.Complete }; var jobs = queued.Concat([complete, waiting, running]).Reverse().ToArray();
    var all = JobHistory.Project(jobs); Assert(all[0].Id == running.Id && all[301].Id == waiting.Id && all[302].Id == complete.Id);
    Assert(all.Where(j => j.State == JobState.Queued).Select(j => j.Id).SequenceEqual(JobQueue.Order(jobs).Select(j => j.Id)));
    var subset = JobHistory.Project(jobs, JobActions.Download, HistoryState.Queued, HistoryOrder.Newest);
    Assert(subset.Length == 150 && subset[0].QueuePosition == 2 && subset[^1].QueuePosition == 300);
    Assert(JobHistory.Project(jobs, state: HistoryState.Queued, search: "Video 200").Single().QueuePosition == 200);
    queued[0].State = JobState.Paused;
    Assert(JobHistory.Project(jobs, state: HistoryState.Queued, search: "Video 200").Single().QueuePosition == 199);
    Assert(!JsonSerializer.Serialize(queued[199]).Contains("QueuePosition") && !JsonSerializer.Serialize(queued[199]).Contains("QueueLabel"));
}));
await Test("Lịch sử tích nhiều bộ lọc: OR trạng thái, ANY/ALL công việc và kết hợp tìm kiếm", () => Sync(() => {
    var jobs = new[] {
        new VideoJob { Name = "Vũ trụ A", State = JobState.Queued, Actions = JobActions.Download },
        new VideoJob { Name = "Vũ trụ B", State = JobState.Paused, Actions = JobActions.WriteSheet },
        new VideoJob { Name = "Vũ trụ C", State = JobState.Queued, Actions = JobActions.All },
        new VideoJob { Name = "Động vật", State = JobState.Complete, Actions = JobActions.All },
        new VideoJob { Name = "Đã xóa", State = JobState.Paused, Actions = JobActions.All, FolderDeleted = true, SheetDeleted = true, YouTubeDeleted = true }
    };
    var mask = JobActions.Download | JobActions.WriteSheet;
    Assert(JobHistory.ProjectSelected(jobs, JobActions.None, []).Length == 5);
    Assert(JobHistory.ProjectSelected(jobs, mask, [HistoryState.Queued, HistoryState.Paused]).Length == 3);
    Assert(JobHistory.ProjectSelected(jobs, mask, [HistoryState.Queued, HistoryState.Paused], requireAllActions: true).Single().Name == "Vũ trụ C");
    Assert(JobHistory.ProjectSelected(jobs, mask, [HistoryState.Queued, HistoryState.Paused], search: "Vũ trụ B").Single().State == JobState.Paused);
    Assert(JobHistory.ProjectSelected(jobs, mask, [HistoryState.Complete], search: "Vũ trụ").Length == 0);
    Assert(JobHistory.ProjectSelected(jobs, JobActions.Download, [HistoryState.Queued], search: "C").Single().QueuePosition == 2);
    Assert(JobQueue.Order(jobs).Count() == 2);
}));
await Test("Hàng chờ ưu tiên xóa, fallback dữ liệu cũ và tiếp tục vào cuối", () => Sync(() => {
    var time = DateTimeOffset.UtcNow;
    var legacy = new VideoJob { Id = "legacy", State = JobState.Queued, Created = time.AddMinutes(-10) };
    var resumed = new VideoJob { Id = "resumed", State = JobState.Queued, Created = time.AddDays(-10), EnqueuedAt = time.AddMinutes(1) };
    var queued = new VideoJob { Id = "queued", State = JobState.Queued, Created = time, EnqueuedAt = time };
    var deleting = new VideoJob { Id = "delete", State = JobState.Queued, DeletionRequested = true, Created = time, EnqueuedAt = time.AddMinutes(2) };
    Assert(JobQueue.Order([resumed,queued,legacy,deleting]).Select(j => j.Id).SequenceEqual(["delete","legacy","queued","resumed"]));
}));
await Test("Hàng chờ điều phối 40 mục: tạm dừng/tiếp tục không chen trước các mục đã chờ", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "queue-40")); var blocker = Job(); blocker.DownloadDirectory = runRoot;
    var fake = new Fakes { BlockUploadJobId = blocker.Id }; var coordinator = new JobCoordinator(db, fake, fake, fake);
    var executed = new System.Collections.Concurrent.ConcurrentQueue<string>(); var seen = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
    coordinator.Changed += job => { if (job.State == JobState.Running && seen.TryAdd(job.Id, 0)) executed.Enqueue(job.Id); };
    try
    {
        await coordinator.SubmitAsync(blocker); await fake.UploadEntered.Task.WaitAsync(TimeSpan.FromSeconds(4)); executed.Clear();
        var jobs = new List<VideoJob>();
        for (var i = 0; i < 40; i++)
        {
            var job = Job(JobMode.DownloadOnly); job.Input = "https://www.douyin.com/video/" + (7700000000000000000L + i);
            job.Created = DateTimeOffset.UtcNow.AddDays(-i - 1); job.DownloadDirectory = runRoot;
            await coordinator.SubmitAsync(job); jobs.Add(job);
        }
        await coordinator.PauseJobAsync(jobs[5].Id); await coordinator.ResumeAsync(jobs[5].Id);
        var expected = jobs.Where((_, i) => i != 5).Append(jobs[5]).Select(j => j.Id).ToArray();
        Assert(JobQueue.Order(await db.ListAsync()).Select(j => j.Id).SequenceEqual(expected));
        Assert(JobHistory.Project(await db.ListAsync(), state: HistoryState.Queued).Select(j => j.Id).SequenceEqual(expected));
        await coordinator.PauseJobAsync(blocker.Id); await FinishAutomatically(db, jobs[5].Id);
        Assert(executed.ToArray().SequenceEqual(expected), "Worker differs from displayed queue order");
        Assert((await db.ListAsync()).Where(j => j.Id != blocker.Id).All(j => j.State == JobState.Complete) && fake.Writes == 1 && fake.Uploads == 1);
    }
    finally { await coordinator.StopAsync(); }
});
await Test("Thông báo chờ chỉ tạo một lần, không phụ thuộc lọc lịch sử", () => Sync(() => {
    var inbox = new JobAttentionInbox(); var a = Job(); a.State = JobState.NeedsAction; a.Message = "Kiểm tra dữ liệu";
    Assert(inbox.Update([a]) && inbox.Items.Count == 1); Assert(!inbox.Update([a]));
    a.Message = "Yêu cầu mới"; Assert(inbox.Update([a]));
    var b = Job(); b.State = JobState.NeedsAuth; Assert(inbox.Update([a,b]) && inbox.Items.Count == 2);
    a.State = JobState.Queued; Assert(!inbox.Update([a,b]) && inbox.Items.Count == 1);
    a.State = JobState.NeedsAction; Assert(inbox.Update([a,b]));
    a.RemovedFromHistory = true; b.State = JobState.Paused; Assert(!inbox.Update([a,b]) && inbox.Items.Count == 0);
}));
await Test("Upload chưa rõ kết quả chỉ cho đối soát, không nút retry mù", () => Sync(() => {
    var job = Job(); job.State = JobState.NeedsAction; job.MediaSendStarted = true; job.YouTubeId = null;
    var item = JobAttention.From(job)!; Assert(item.Choices.Any(x => x.Action == AttentionAction.Reconcile)); Assert(item.Choices.All(x => x.Action != AttentionAction.Retry));
    job.MediaSendStarted = false; job.SheetWriteStarted = true; item = JobAttention.From(job)!;
    Assert(item.Choices.Any(x => x.Action == AttentionAction.Reconcile) && item.Choices.All(x => x.Action != AttentionAction.Retry));
}));
await Test("Thiếu quyền Google cho đăng nhập lại; thư mục mất cho liên kết", () => Sync(() => {
    var job = Job(); job.State = JobState.NeedsAuth; job.DownloadDirectory = Path.Combine(runRoot,"not-there-attention");
    var item = JobAttention.From(job)!; Assert(item.Choices.Any(x => x.Action == AttentionAction.ReconnectGoogle));
    Assert(item.Choices.Any(x => x.Action == AttentionAction.LocateFolder));
    job.FolderDeleted = true; Assert(JobAttention.From(job)!.Choices.All(x => x.Action != AttentionAction.LocateFolder));
}));
await Test("Công việc cần lựa chọn không chặn hàng chờ; duyệt tiếp từ checkpoint", async () => {
    var db = new LocalStore(Path.Combine(runRoot,"attention-queue")); var f = new WorkflowFakes(Path.Combine(runRoot,"attention-files")) { FailMetadata = true };
    var coordinator = new JobCoordinator(db,f,f,f);
    var first = Job(); first.Actions = JobActions.WriteSheet; first.Media = null; first.Metadata = null;
    var second = Job(JobMode.DownloadOnly); second.Input = "https://www.douyin.com/video/7673782911702748458"; second.Name = "Công việc tiếp tục"; second.Media = null;
    await coordinator.SubmitAsync(first); await coordinator.SubmitAsync(second); await Finish(db,second.Id);
    var saved = (await db.ListAsync()).Single(x => x.Id == first.Id); Assert(saved.State == JobState.NeedsAction && JobAttention.From(saved) is not null);
    Assert((await db.ListAsync()).Single(x => x.Id == second.Id).State == JobState.Complete);
    f.FailMetadata = false; await coordinator.ResumeAsync(first.Id); await Finish(db,first.Id); await coordinator.StopAsync();
    saved = (await db.ListAsync()).Single(x => x.Id == first.Id); Assert(saved.State == JobState.Complete && f.Downloads == 1 && f.Writes == 1);
});
await Test("Tạm dừng do người chọn xóa yêu cầu chờ nhưng giữ phần trăm", async () => {
    var db = new LocalStore(Path.Combine(runRoot,"attention-pause")); var f = new WorkflowFakes(Path.Combine(runRoot,"attention-pause-files"));
    var job = Job(); job.State = JobState.NeedsAction; job.Progress = 37.5; await db.SaveAsync(job);
    await new JobCoordinator(db,f,f,f).PauseJobAsync(job.Id); var saved = (await db.ListAsync()).Single();
    Assert(saved.State == JobState.Paused && saved.Progress == 37.5 && JobAttention.From(saved) is null);
});

await Test("Cùng credential chỉ hỏi một lần và không gọi lại API đang thiếu quyền", async () => {
    var db = new LocalStore(Path.Combine(runRoot,"attention-auth-group")); var f = new WorkflowFakes(Path.Combine(runRoot,"attention-auth-group-files")) { ChannelFailure = new NeedsAuthException("Expired") };
    var c = new JobCoordinator(db,f,f,f); var first = Job(); first.Actions = JobActions.Download | JobActions.UploadYouTube;
    var second = Job(); second.Input = "https://www.douyin.com/video/7673782911702748458"; second.Name = "Cùng kết nối"; second.Actions = first.Actions;
    await c.SubmitAsync(first); await Finish(db,first.Id); await c.SubmitAsync(second); await Finish(db,second.Id);
    var waiting = await db.ListAsync(); Assert(waiting.All(x => x.State == JobState.NeedsAuth) && f.ChannelChecks == 1);
    var inbox = new JobAttentionInbox(); Assert(inbox.Update(waiting) && inbox.Items.Count == 1 && inbox.Items[0].RelatedJobIds.Count == 2);
    var original = waiting.Single(x => x.Id == first.Id); var connected = JsonSerializer.Deserialize<VideoJob>(JsonSerializer.Serialize(original))!;
    connected.Channel = connected.Channel! with { CredentialId = "new-credential" }; await db.SaveAsync(connected); f.ChannelFailure = null;
    await c.ReconnectDependentJobsAsync(original,connected); await FinishAutomatically(db,first.Id); await FinishAutomatically(db,second.Id); await c.StopAsync();
    Assert((await db.ListAsync()).All(x => x.Channel!.CredentialId == "new-credential" && x.State == JobState.Complete) && f.Uploads == 2);
});
await Test("Khôi phục quyền ngay khóa cũ tiếp tục cả nhóm và giữ công việc tạm dừng", async () => {
    var db = new LocalStore(Path.Combine(runRoot,"attention-auth-stable")); var f = new WorkflowFakes(Path.Combine(runRoot,"attention-auth-stable-files")) { ChannelFailure = new NeedsAuthException("Expired") };
    var c = new JobCoordinator(db,f,f,f); var first = Job(); first.Actions = JobActions.Download | JobActions.UploadYouTube;
    var second = Job(); second.Input = "https://www.douyin.com/video/7673782911702748458"; second.Name = "Cùng kết nối"; second.Actions = first.Actions;
    await c.SubmitAsync(first); await Finish(db,first.Id); await c.SubmitAsync(second); await Finish(db,second.Id);
    var paused = Job(); paused.State = JobState.Paused; paused.Actions = first.Actions; await db.SaveAsync(paused);
    var original = (await db.ListAsync()).Single(x => x.Id == first.Id); f.ChannelFailure = null;
    await c.ReconnectDependentJobsAsync(original, original); await FinishAutomatically(db,first.Id); await FinishAutomatically(db,second.Id); await c.StopAsync();
    var jobs = await db.ListAsync();
    Assert(jobs.Where(x => x.Id != paused.Id).All(x => x.State == JobState.Complete && x.Channel!.CredentialId == first.Channel!.CredentialId));
    Assert(jobs.Single(x => x.Id == paused.Id).State == JobState.Paused && f.Uploads == 2);
});
await Test("Chờ 429 không thử sớm; công việc độc lập vẫn chạy và tự tiếp tục", async () => {
    var db = new LocalStore(Path.Combine(runRoot,"attention-rate")); var date = DateTimeOffset.UtcNow.AddSeconds(10);
    var f = new WorkflowFakes(Path.Combine(runRoot,"attention-rate-files")) { ChannelFailure = new RetryLaterException("429",date,"google-api") };
    var c = new JobCoordinator(db,f,f,f); var job = Job(); job.Actions = JobActions.Download | JobActions.UploadYouTube;
    try {
        await c.SubmitAsync(job); await Finish(db,job.Id); var saved = (await db.ListAsync()).Single();
        Assert(saved.State == JobState.WaitingToRetry && saved.NextRetryAt >= date && f.ChannelChecks == 1,
            $"state={saved.State}, calls={f.ChannelChecks}, retry={saved.NextRetryAt:O}, deadline={date:O}, now={DateTimeOffset.UtcNow:O}");
        await c.ResumeAsync(job.Id); Assert(f.ChannelChecks == 1);
        var independent = Job(JobMode.DownloadOnly); independent.Input = "https://www.douyin.com/video/7673782911702748458"; independent.Name = "Tải độc lập"; independent.Media = null;
        await c.SubmitAsync(independent); await FinishAutomatically(db,independent.Id); Assert(f.Downloads == 1 && f.ChannelChecks == 1);
        f.ChannelFailure = null;
        // Give fixture I/O room before the deadline, then test the real automatic wake-up.
        var remaining = date - DateTimeOffset.UtcNow;
        if (remaining > TimeSpan.Zero) await Task.Delay(remaining + TimeSpan.FromMilliseconds(100));
        await FinishAutomatically(db,job.Id); Assert(f.ChannelChecks == 2);
    } finally { await c.StopAsync(); }
});
await Test("Yêu cầu và lịch thử lại được lưu khi mở lại, thoát không mất quyết định", async () => {
    var db = new LocalStore(Path.Combine(runRoot,"attention-restart")); var job = Job(); job.State = JobState.WaitingToRetry; job.StageAction = JobActions.Download;
    job.Issue = JobIssue.Create(job,new RetryLaterException("429",DateTimeOffset.UtcNow.AddMinutes(2),"douyin-cdn"),"429"); job.NextRetryAt = job.Issue.RetryAt;
    job.Decisions.Add(new(job.Issue.Id,AttentionAction.Retry,DateTimeOffset.UtcNow)); await db.SaveAsync(job);
    var f = new WorkflowFakes(Path.Combine(runRoot,"attention-restart-files")); var c = new JobCoordinator(db,f,f,f); await c.InitializeAsync(); await c.StopAsync(preservePendingYouTube:true);
    var saved = (await new LocalStore(Path.Combine(runRoot,"attention-restart")).ListAsync()).Single();
    Assert(saved.State == JobState.WaitingToRetry && saved.NextRetryAt == job.NextRetryAt && saved.Decisions.Count == 1 && f.Downloads == 0);
});
await Test("Quyết định lỗi thời không được thực hiện, quyết định hợp lệ lưu một receipt", async () => {
    var db = new LocalStore(Path.Combine(runRoot,"attention-stale")); var job = Job(); job.State = JobState.NeedsAction; job.StageAction = JobActions.UploadYouTube;
    job.Issue = JobIssue.Create(job,new NeedsActionException("old"),"old"); await db.SaveAsync(job); var request = JobAttention.From(job)!;
    var f = new WorkflowFakes(Path.Combine(runRoot,"attention-stale-files")); var c = new JobCoordinator(db,f,f,f);
    job.Issue = JobIssue.Create(job,new NeedsActionException("new"),"new"); await db.SaveAsync(job);
    await Throws<NeedsActionException>(() => c.AcknowledgeAsync(request,AttentionAction.Retry));
    await c.AcknowledgeAsync(JobAttention.From(job)!,AttentionAction.Pause);
    Assert((await db.ListAsync()).Single().Decisions.Count == 1 && f.Uploads == 0);
});
await Test("Bỏ phần chưa xong giữ kết quả đã có và cho Bổ sung thực hiện lại", async () => {
    var db = new LocalStore(Path.Combine(runRoot,"attention-omit")); var job = Job(); job.State = JobState.NeedsAction; job.Actions = JobActions.All; job.PendingActions = JobActions.UploadYouTube; job.SheetVerified = true;
    job.StageAction = JobActions.UploadYouTube; job.Issue = JobIssue.Create(job,new NeedsActionException("cannot upload"),"cannot upload"); await db.SaveAsync(job);
    var f = new WorkflowFakes(Path.Combine(runRoot,"attention-omit-files")); var c = new JobCoordinator(db,f,f,f); await c.OmitAffectedAsync(job.Id);
    var saved = (await db.ListAsync()).Single(); Assert(saved.State == JobState.Complete && saved.OmittedActions == JobActions.UploadYouTube && saved.SheetVerified && saved.Media is not null);
    Assert(saved.CurrentActions == (JobActions.Download | JobActions.WriteSheet) && saved.Addable.HasFlag(JobActions.UploadYouTube)); await c.StopAsync();
    saved.State = JobState.NeedsAction; saved.PendingActions = JobActions.UploadYouTube; saved.MediaSendStarted = true; saved.Issue = JobIssue.Create(saved,new NeedsActionException("unknown"),"unknown"); await db.SaveAsync(saved);
    await Throws<NeedsActionException>(() => c.OmitAffectedAsync(saved.Id));
});
await Test("Kiểm tra phiên upload chỉ gửi query rỗng, không gửi lại video", async () => {
    var requests = 0; var checkpoints = 0;
    var svc = await Services(new ScriptedHandler(async req => { requests++; Assert(req.Method == HttpMethod.Put && (await req.Content!.ReadAsByteArrayAsync()).Length == 0); var reply=new HttpResponseMessage((HttpStatusCode)308); reply.Headers.TryAddWithoutValidation("Range","bytes=0-49"); return reply; }));
    var job = Job(); job.MediaSendStarted = true; job.UploadSessionKey = "inspect-session"; await svc.Secrets.WriteAsync(job.UploadSessionKey,"https://www.googleapis.com/upload/session");
    Assert(await svc.YouTube.InspectPendingUploadAsync(job,()=>{checkpoints++;return Task.CompletedTask;},default));
    Assert(requests == 1 && checkpoints == 1 && job.UploadedBytes == 50 && job.YouTubeId is null);
});
await Test("Phiên upload hết hạn giữ trạng thái chưa rõ để người dùng chọn", async () => {
    var requests = 0; var svc = await Services(new ScriptedHandler(req => { requests++; Assert(req.Method == HttpMethod.Put); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone)); }));
    var job=Job(); job.MediaSendStarted=true; job.UploadSessionKey="expired-inspect"; await svc.Secrets.WriteAsync(job.UploadSessionKey,"https://www.googleapis.com/upload/session");
    Assert(!await svc.YouTube.InspectPendingUploadAsync(job,()=>Task.CompletedTask,default) && job.UnknownUpload && requests==1);
});
await Test("Liên kết video có sẵn xác minh đúng kênh, không tạo upload", async () => {
    var db=new LocalStore(Path.Combine(runRoot,"attention-attach")); var job=Job(); job.State=JobState.NeedsAction; job.Actions=JobActions.All; job.PendingActions=JobActions.UploadYouTube; job.SheetVerified=true; job.MediaSendStarted=true;
    job.Issue=JobIssue.Create(job,new NeedsActionException("unknown"),"unknown"); await db.SaveAsync(job);
    var f=new Fakes { MissingYouTube=true }; var c=new JobCoordinator(db,f,f,f);
    await Throws<NeedsActionException>(()=>c.AttachUploadedVideoAsync(job.Id,"abcDEF_1234",default)); Assert((await db.ListAsync()).Single().YouTubeId is null);
    f.MissingYouTube=false; await c.AttachUploadedVideoAsync(job.Id,"abcDEF_1234",default); await c.ResumeAsync(job.Id); await FinishAutomatically(db,job.Id); await c.StopAsync();
    Assert(f.Uploads==0 && (await db.ListAsync()).Single().YouTubeId=="abcDEF_1234");
});

await Test("Đã nhận đủ bytes nhưng thiếu ID vẫn yêu cầu kiểm tra, không lặp gửi", async () => {
    foreach(var replyKind in new[]{"308","success-no-id"}) {
        var requests=0; var svc=await Services(new ScriptedHandler(req=>{requests++;Assert(req.Method==HttpMethod.Put); if(replyKind=="success-no-id")return Task.FromResult(JsonReply(new JsonObject())); var response=new HttpResponseMessage((HttpStatusCode)308);response.Headers.TryAddWithoutValidation("Range","bytes=0-99");return Task.FromResult(response);}));
        var job=Job();job.MediaSendStarted=true;job.UploadSessionKey="full-"+replyKind;await svc.Secrets.WriteAsync(job.UploadSessionKey,"https://www.googleapis.com/upload/session");
        Assert(!await svc.YouTube.InspectPendingUploadAsync(job,()=>Task.CompletedTask,default) && requests==1 && job.UnknownUpload);
    }
});
await Test("Xác nhận chưa tạo video cần đúng issue hiện tại và lưu quyết định", async () => {
    var db=new LocalStore(Path.Combine(runRoot,"attention-confirm-absence"));var job=Job();job.State=JobState.NeedsAction;job.MediaSendStarted=true;job.UploadedBytes=50;job.UploadSessionKey="old-session";job.Issue=JobIssue.Create(job,new NeedsActionException("unknown"),"unknown");await db.SaveAsync(job);
    var f=new Fakes();var c=new JobCoordinator(db,f,f,f);await Throws<NeedsActionException>(()=>c.ConfirmUploadAbsentAsync(job.Id,"stale"));
    Assert((await db.ListAsync()).Single().UnknownUpload);await c.ConfirmUploadAbsentAsync(job.Id,job.Issue.Id);var saved=(await db.ListAsync()).Single();
    Assert(!saved.MediaSendStarted && saved.UploadSessionKey is null && saved.UploadedBytes==0 && saved.Decisions.Last().Action==AttentionAction.ConfirmUploadAbsent && f.Uploads==0);
});
await Test("Thông báo thao tác giữ nhiều lỗi và gộp đúng lỗi lặp", () => Sync(() => {
    var entries = new List<OperationNotice>();
    var first = OperationNotice.Report(entries, "Mạng Sheets", OperationArea.Settings);
    OperationNotice.Report(entries, "Mạng Sheets", OperationArea.Settings);
    OperationNotice.Report(entries, "Không mở được file", OperationArea.History);
    Assert(entries.Count == 2 && entries[0].Id == first.Id && entries[0].Occurrences == 2);
    Assert(entries[1].ToAttention().Choices.Any(x => x.Action == AttentionAction.Inspect));
}));
await Test("Thông báo thao tác tồn tại sau restart và xóa đúng một yêu cầu", async () => {
    var path = Path.Combine(runRoot,"operation-notices"); var db = new LocalStore(path); var settings = new AppSettings();
    OperationNotice.Report(settings.PendingOperations, "Lỗi kết nối", OperationArea.Settings);
    OperationNotice.Report(settings.PendingOperations, "Lỗi file", OperationArea.History); await db.SaveSettingsAsync(settings);
    var restored = await new LocalStore(path).SettingsAsync(); Assert(restored.PendingOperations.Count == 2);
    restored.PendingOperations.RemoveAt(0); await db.SaveSettingsAsync(restored);
    Assert((await db.SettingsAsync()).PendingOperations.Single().Area == OperationArea.History);
});
await Test("Dữ liệu sai không có nút thử lại mù; lỗi trùng yêu cầu xử lý trước", () => Sync(() => {
    var job = Job(); job.State = JobState.NeedsAction;
    job.Issue = JobIssue.Create(job,new ArgumentException("Tên sai"),"Tên sai");
    Assert(job.Issue.Kind == JobIssueKind.InvalidData && JobAttention.From(job)!.Choices.All(x => x.Action != AttentionAction.Retry));
    job.Issue = JobIssue.Create(job,new NeedsActionException("Trùng", JobIssueKind.Duplicate),"Trùng");
    var request = JobAttention.From(job)!; Assert(request.Guidance.Contains("trùng") && request.Choices.Single(x => x.Action == AttentionAction.Retry).Label.Contains("Đã xử lý"));
}));
await Test("Cùng kết nối không nhắc lại cho thành viên mới nhưng cập nhật sự cố mới", () => Sync(() => {
    var a = Job(); a.State = JobState.NeedsAuth; a.StageAction = JobActions.WriteSheet;
    a.Issue = JobIssue.Create(a,new NeedsAuthException("Expired"),"Expired");
    var b = Job(); b.State = a.State; b.StageAction = a.StageAction; b.Sheet = a.Sheet;
    b.Issue = JobIssue.Create(b,new NeedsAuthException("Expired"),"Expired");
    var inbox = new JobAttentionInbox(); Assert(inbox.Update([a])); Assert(!inbox.Update([a,b]) && inbox.Items.Count == 1);
    a.Message = "Khác đích gốc"; Assert(inbox.Update([a,b])); Assert(!inbox.Update([a,b]));
}));
await Test("Ngách thiếu quyền mở kết nối; lỗi mạng có thể tải lại", () => Sync(() => {
    var entries = new List<OperationNotice>();
    var authError = OperationNotice.Report(entries,"Cần quyền",OperationArea.Niches,true).ToAttention();
    Assert(authError.Choices.All(x => x.Action != AttentionAction.ReloadNiches) && authError.Choices.Any(x => x.Action == AttentionAction.OpenSettings));
    var network = OperationNotice.Report(entries,"Lỗi mạng",OperationArea.Niches).ToAttention();
    Assert(network.Choices.Any(x => x.Action == AttentionAction.ReloadNiches));
}));

await Test("Lưu thông báo nền không ghi đè kết nối Google vừa đổi", async () => {
    var db = new LocalStore(Path.Combine(runRoot,"notice-settings-merge"));
    var current = new AppSettings { Sheet = new("new-sheet",4,"Bảng mới","Video","new-grant"), Channel = new("new-channel","Kênh mới","tube-grant"), ShortMaxMilliseconds = 600000 };
    await db.SaveSettingsAsync(current);
    var oldNotices = new List<OperationNotice>(); OperationNotice.Report(oldNotices,"Lỗi nền của bảng cũ",OperationArea.Sheets);
    await db.SavePendingOperationsAsync(oldNotices); var restored = await db.SettingsAsync();
    Assert(restored.Sheet == current.Sheet && restored.Channel == current.Channel && restored.ShortMaxMilliseconds == current.ShortMaxMilliseconds && restored.PendingOperations.Count == 1);
    await db.SavePendingOperationsAsync([]); restored = await db.SettingsAsync();
    Assert(restored.PendingOperations.Count == 0 && restored.Sheet == current.Sheet);
    var dataError = OperationNotice.Report(oldNotices,"Không ghi được ổ đĩa",OperationArea.General).ToAttention();
    Assert(dataError.Choices.Any(x => x.Action == AttentionAction.OpenDataDirectory));
});

await Test("Thao tác độc lập: xóa thư mục và lịch sử trống ngay khi video khác đang upload", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "independent-local")); var blocker = Job();
    var f = new Fakes { BlockUploadJobId = blocker.Id }; var c = new JobCoordinator(db, f, f, f);
    try {
        await c.SubmitAsync(blocker); await f.UploadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var local = OwnedJob("independent-local-result"); local.Actions = JobActions.Download; local.PendingActions = JobActions.None; local.State = JobState.Complete;
        f.FolderState = Path.Combine(runRoot, "independent-local-state"); var directory = DownloadPolicy.ReserveDirectory(local, f.FolderState); await db.SaveAsync(local);
        await c.RemoveJobAsync(local.Id, deleteFolder: true, removeHistory: true).WaitAsync(TimeSpan.FromSeconds(5));
        Assert(!Directory.Exists(directory) && (await db.ListAsync()).Single(x => x.Id == local.Id).RemovedFromHistory);
        var empty = new VideoJob { Actions = JobActions.Download, Source = blocker.Source, Input = blocker.Input }; await db.SaveAsync(empty);
        await c.RemoveJobAsync(empty.Id, removeHistory: true).WaitAsync(TimeSpan.FromSeconds(5));
        Assert((await db.ListAsync()).Single(x => x.Id == empty.Id).RemovedFromHistory && c.IsJobBusy(blocker.Id));
    } finally { await c.StopAsync(); }
});
await Test("Thao tác độc lập: lỗi Google vẫn xóa file và giữ lịch sử cùng phần còn thiếu", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "independent-errors")); var job = OwnedJob("independent-errors-result");
    job.Actions = JobActions.All; job.State = JobState.Complete; job.SheetVerified = true; job.YouTubeId = "old-video";
    var f = new Fakes { FolderState = Path.Combine(runRoot, "independent-errors-state"), FailDeleteVideo = true };
    var directory = DownloadPolicy.ReserveDirectory(job, f.FolderState); await db.SaveAsync(job); var c = new JobCoordinator(db,f,f,f);
    try {
        await c.RemoveJobAsync(job.Id, true, true, true, removeHistory: true); await Finish(db,job.Id);
        var failed = (await db.ListAsync()).Single(); Assert(failed.FolderDeleted && failed.SheetDeleted && !failed.YouTubeDeleted && !failed.RemovedFromHistory && !Directory.Exists(directory));
        f.FailDeleteVideo = false; await c.ResumeAsync(job.Id); await FinishAutomatically(db,job.Id);
        Assert((await db.ListAsync()).Single().RemovedFromHistory && f.FolderDeletes == 1 && f.SheetDeletes == 1);
    } finally { await c.StopAsync(); }
});
await Test("Thao tác độc lập: xóa Google chạy riêng, tạm dừng đúng mục và giữ ý định", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "independent-remote")); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var f = new Fakes { BeforeVideoDelete = async (_,ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite,ct); } }; var c = new JobCoordinator(db,f,f,f);
    var deleting = Job(); deleting.State = JobState.Complete; deleting.SheetVerified = true; deleting.YouTubeId = "delete-id"; await db.SaveAsync(deleting);
    try {
        await c.RemoveJobAsync(deleting.Id, deleteYouTube: true); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var other = Job(JobMode.DownloadOnly); other.State = JobState.Paused; other.Input = "https://www.douyin.com/video/7673782911702748458"; await db.SaveAsync(other);
        await c.ResumeAsync(other.Id); await FinishAutomatically(db,other.Id);
        Assert(c.IsJobBusy(deleting.Id)); await c.PauseJobAsync(deleting.Id).WaitAsync(TimeSpan.FromSeconds(5));
        var paused = (await db.ListAsync()).Single(x=>x.Id==deleting.Id); Assert(paused.State==JobState.Paused && paused.DeletionRequested && !paused.YouTubeDeleted);
    } finally { await c.StopAsync(); }
});
await Test("Thao tác độc lập: kiểm tra mạng không giữ khóa chung, dừng hủy và không ghi đè mục", async () => {
    var db = new LocalStore(Path.Combine(runRoot, "independent-inspection")); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var f = new Fakes { BeforeSheetRead = async (_,ct) => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite,ct); } }; var c = new JobCoordinator(db,f,f,f);
    var inspecting = Job(); inspecting.State=JobState.Complete; inspecting.SheetVerified=true; await db.SaveAsync(inspecting);
    var inspection = c.RecheckResultsAsync(inspecting.Id); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try {
        var empty = new VideoJob { Actions=JobActions.Download }; await db.SaveAsync(empty);
        await c.RemoveJobAsync(empty.Id,removeHistory:true).WaitAsync(TimeSpan.FromSeconds(5));
        await Throws<NeedsActionException>(() => c.AddActionsAsync(inspecting.Id,JobActions.WriteSheet,new AppSettings(),""));
        await c.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)); await Throws<OperationCanceledException>(async()=>await inspection);
        Assert((await db.ListAsync()).Single(x=>x.Id==inspecting.Id).SheetVerified);
    } finally { await c.StopAsync(); }
});
await Test("Thao tác độc lập: sửa bìa khi video khác đang upload", async () => {
    var db=new LocalStore(Path.Combine(runRoot,"independent-covers")); var blocker=Job(); var f=new Fakes{BlockUploadJobId=blocker.Id}; var c=new JobCoordinator(db,f,f,f);
    try {
        await c.SubmitAsync(blocker); await f.UploadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var covers=Job(); covers.State=JobState.Complete; covers.Actions=JobActions.Download; covers.DownloadDirectory=runRoot; await db.SaveAsync(covers);
        await c.RefreshCoversAsync(covers.Id,default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert((await db.ListAsync()).Single(x=>x.Id==covers.Id).Media!.CoverStatus=="Đã tải ảnh bìa" && c.IsJobBusy(blocker.Id));
    } finally { await c.StopAsync(); }
});
await Test("Thao tác độc lập: cùng bảng tuần tự, bảng khác được xóa khi bảng đầu đang ghi", async () => {
    var db=new LocalStore(Path.Combine(runRoot,"independent-sheet-lock")); var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var f=new Fakes{BeforeSheetWrite=async(_,ct)=>{entered.TrySetResult();await release.Task.WaitAsync(ct);}}; var c=new JobCoordinator(db,f,f,f);
    var writing=Job(); writing.State=JobState.Paused; writing.Actions=JobActions.WriteSheet; writing.PendingActions=JobActions.WriteSheet; await db.SaveAsync(writing);
    var same=Job(); same.State=JobState.Complete; same.SheetVerified=true; await db.SaveAsync(same);
    var other=Job(); other.State=JobState.Complete; other.SheetVerified=true; other.Sheet=other.Sheet! with {SpreadsheetId="other-sheet"}; await db.SaveAsync(other);
    try {
        await c.ResumeAsync(writing.Id); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await c.RemoveJobAsync(same.Id,deleteSheet:true); await c.RemoveJobAsync(other.Id,deleteSheet:true); await FinishAutomatically(db,other.Id);
        Assert(!(await db.ListAsync()).Single(x=>x.Id==same.Id).SheetDeleted && f.SheetDeletes==1);
        release.TrySetResult(); await FinishAutomatically(db,writing.Id); await FinishAutomatically(db,same.Id); Assert(f.SheetDeletes==2);
    } finally { release.TrySetResult(); await c.StopAsync(); }
});
await Test("Thao tác độc lập: chờ YouTube không tính đang thực hiện và vẫn sửa được bìa", async () => {
    var db=new LocalStore(Path.Combine(runRoot,"independent-wait")); var f=new Fakes{PendingChecks=1}; var c=new JobCoordinator(db,f,f,f);
    var pending=Job(); pending.State=JobState.WaitingForYouTube; pending.YouTubeId="pending"; pending.PendingActions=JobActions.UploadYouTube; pending.NextYouTubeCheckAt=DateTimeOffset.UtcNow.AddMinutes(5); await db.SaveAsync(pending);
    try {
        await c.InitializeAsync();
        for(var i=0;i<100;i++){var saved=(await db.ListAsync()).Single();if(saved.YouTubeCheckAttempts>0 && !c.IsExecuting)break;await Task.Delay(20);}
        Assert(c.IsRunning && !c.IsExecuting && !c.IsUsingSource && !c.UsesCredential("youtube-test"));
        var covers=Job(); covers.State=JobState.Complete; covers.Actions=JobActions.Download; covers.DownloadDirectory=runRoot; await db.SaveAsync(covers);
        await c.RefreshCoversAsync(covers.Id,default); Assert((await db.ListAsync()).Single(x=>x.Id==covers.Id).Media!.CoverStatus=="Đã tải ảnh bìa");
    } finally { await c.StopAsync(); }
});
await Test("Thao tác độc lập: xác nhận YouTube đến giờ vẫn chạy khi mục khác đang upload", async () => {
    var db=new LocalStore(Path.Combine(runRoot,"independent-verification")); var blocker=Job(); blocker.Input="https://www.douyin.com/video/7673782911702748458"; blocker.Metadata=null;
    var f=new Fakes{BlockUploadJobId=blocker.Id}; var c=new JobCoordinator(db,f,f,f);
    var pending=Job(); pending.State=JobState.WaitingForYouTube; pending.YouTubeId="pending"; pending.PendingActions=JobActions.UploadYouTube; pending.NextYouTubeCheckAt=DateTimeOffset.UtcNow.AddMilliseconds(150); await db.SaveAsync(pending);
    try {
        await c.SubmitAsync(blocker); await f.UploadEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await FinishAutomatically(db,pending.Id); Assert(c.IsJobBusy(blocker.Id) && !c.IsUsingSource);
        var sessionEntered=false;
        await c.OpenSourceSessionAsync(ct=>{sessionEntered=true; return Task.FromResult(true);},default);
        Assert(sessionEntered);
    } finally { await c.StopAsync(); }
});
await Test("Thao tác độc lập: thiếu quyền xóa YouTube báo đăng nhập trước DELETE", async () => {
    var deletes=0; var job=Job();job.YouTubeId="video-id";
    var svc=await Services(new ScriptedHandler(req=>{
        if(req.Method==HttpMethod.Delete)deletes++;
        return Task.FromResult(JsonReply(req.RequestUri!.AbsolutePath.EndsWith("/channels")
            ? JsonNode.Parse("""{"items":[{"id":"channel-id","snippet":{"title":"Kênh"}}]}""")!
            : JsonNode.Parse("""{"items":[{"id":"video-id","snippet":{"channelId":"channel-id"}}]}""")!));
    }));
    await Throws<NeedsAuthException>(()=>svc.YouTube.DeleteVideoAsync(job,()=>Task.CompletedTask,default));
    Assert(deletes==0 && !job.YouTubeDeleteStarted && !job.YouTubeDeleted);
});
await Test("Thao tác độc lập: giới hạn bốn lượt Google vẫn cho xóa file của yêu cầu tiếp theo", async () => {
    var db=new LocalStore(Path.Combine(runRoot,"independent-limit")); var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var localDeleted=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;
    var f=new Fakes{FolderState=Path.Combine(runRoot,"independent-limit-state"),BeforeVideoDelete=async(_,ct)=>{if(Interlocked.Increment(ref calls)==4)entered.TrySetResult();await Task.Delay(Timeout.Infinite,ct);}};
    var c=new JobCoordinator(db,f,f,f); string? fifthId=null;
    c.Changed+=job=>{if(job.Id==fifthId && job.FolderDeleted)localDeleted.TrySetResult();};
    try {
        for(var i=0;i<5;i++){
            if(i==4)await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var job=OwnedJob("independent-limit-"+i);job.Actions=JobActions.Download|JobActions.UploadYouTube;job.State=JobState.Complete;job.YouTubeId="video-"+i;
            DownloadPolicy.ReserveDirectory(job,f.FolderState);await db.SaveAsync(job);if(i==4)fifthId=job.Id;
            await c.RemoveJobAsync(job.Id,deleteFolder:true,deleteYouTube:true,removeHistory:true);
        }
        await localDeleted.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert(calls==4);
        await c.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));Assert((await db.ListAsync()).All(j=>j.State==JobState.Paused && j.DeletionRequested && j.FolderDeleted && !j.RemovedFromHistory));
    } finally {await c.StopAsync();}
});
await Test("Thao tác độc lập: đối soát upload chưa có ID vẫn giữ đúng credential đang dùng", async () => {
    var db=new LocalStore(Path.Combine(runRoot,"independent-inspect-credential")); var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var f=new Fakes{BeforePendingInspection=async(_,ct)=>{entered.TrySetResult();await Task.Delay(Timeout.Infinite,ct);}};var c=new JobCoordinator(db,f,f,f);
    var job=Job();job.State=JobState.NeedsAction;job.MediaSendStarted=true;job.PendingActions=JobActions.UploadYouTube;await db.SaveAsync(job);
    var inspection=c.InspectRemoteAsync(job.Id,default);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    try{
        Assert(c.UsesCredential(job.Channel!.CredentialId) && !c.UsesCredential("unrelated-credential"));
        await c.PauseJobAsync(job.Id);await Throws<OperationCanceledException>(async()=>await inspection);
        var saved=(await db.ListAsync()).Single();Assert(saved.State==JobState.Paused && saved.UnknownUpload && !c.UsesCredential(job.Channel.CredentialId));
    }finally{await c.StopAsync();}
});
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static async Task Finish(IJobStore store, string id)
{
    for (var i = 0; i < 200; i++) { var job = (await store.ListAsync()).Single(x => x.Id == id); if (job.State is not (JobState.Queued or JobState.Running)) return; await Task.Delay(10); }
    throw new Exception("Worker did not finish");
}
static async Task CreateJunctionFixtureAsync(string link, string destination)
{
    // PowerShell handles Unicode/spaces in fixture paths without cmd.exe's quoting rules.
    var command = "$ErrorActionPreference='Stop'; New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + destination.Replace("'", "''") + "' | Out-Null";
    var start = new System.Diagnostics.ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(command)) }) start.ArgumentList.Add(argument);
    using var process = System.Diagnostics.Process.Start(start)!;
    var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15)); await output;
    Assert(process.ExitCode == 0, "Cannot create junction fixture: " + await error);
}
static async Task FinishAutomatically(IJobStore store, string id)
{
    for (var i = 0; i < 400; i++) {
        var job = (await store.ListAsync()).Single(x => x.Id == id);
        if (job.State == JobState.Complete) return;
        if (job.State is JobState.NeedsAction or JobState.NeedsAuth or JobState.Failed) throw new Exception(job.Message);
        await Task.Delay(10);
    }
    throw new Exception("Automatic verification did not finish");
}
sealed class Fakes : IVideoSource, ISheetDestination, IYouTubeDestination
{
    public Func<VideoJob,CancellationToken,Task>? BeforeSheetRead, BeforeSheetWrite, BeforeVideoDelete, BeforePendingInspection;
    public async Task<bool> InspectPendingUploadAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) { if(BeforePendingInspection is not null)await BeforePendingInspection(job,ct);return false; }
    public bool FailDeleteVideo;
    public Func<VideoJob, IReadOnlyList<string>, Func<Task>, CancellationToken, Task<string?>>? FindFolder;
    public Task<string?> FindMovedFolderAsync(VideoJob job, IReadOnlyList<string> roots, Func<Task> checkpoint, CancellationToken ct) => FindFolder?.Invoke(job, roots, checkpoint, ct) ?? Task.FromResult<string?>(null);
    public bool MissingSheet, MissingYouTube, InspectionAuthFailure, FailUploadPreflight;
    public async Task<int?> FindExistingRowAsync(VideoJob job, CancellationToken ct) { if(BeforeSheetRead is not null) await BeforeSheetRead(job,ct); if(InspectionAuthFailure) throw new NeedsAuthException("Expired grant"); return MissingSheet ? null : job.SheetRow ?? 1; }
    public Task<bool> ExistsAsync(VideoJob job, CancellationToken ct) => InspectionAuthFailure ? throw new NeedsAuthException("Expired grant") : Task.FromResult(!MissingYouTube);
    public bool FailFullDownload, FailSheet, ChangedFile, FailCoverOnce, FailCheckNetworkOnce; public int Writes, Uploads, Checks, Validations, PendingChecks;
    public string? BlockUploadJobId; public bool BlockUploadOnce = true, FailDeleteSheetOnce;
    public int SheetDeletes, VideoDeletes;
    public TaskCompletionSource UploadEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task<SourceVideo> ResolveAsync(string input, CancellationToken ct) => Task.FromResult(new SourceVideo(Rules.SourceId(Rules.DouyinUri(input)) ?? "7659702794928147739", input));
    public int MetadataReads;
    public Task<SourceMetadata> ReadMetadataAsync(SourceVideo source, CancellationToken ct) { MetadataReads++; return Task.FromResult(new SourceMetadata(source.Id, 300001, "fixture", DateTimeOffset.UtcNow)); }
    public Task<MediaFile> DownloadAsync(VideoJob job, Func<Task> checkpoint, IProgress<double> progress, CancellationToken ct)
    {
        if (FailFullDownload && job.Mode == JobMode.Full) throw new NeedsActionException("Douyin yêu cầu xác minh");
        if (FailCoverOnce) { FailCoverOnce = false; return Task.FromResult(job.Media! with { CoverStatus = "Thiếu ảnh bìa; tiếp tục video" }); }
        if (job.DownloadDirectory is not null && job.Media is not null) return Task.FromResult(job.Media with { CoverStatus = "Đã tải ảnh bìa" });
        throw new Exception("Unexpected download");
    }
    public Task ValidateFileAsync(MediaFile file, CancellationToken ct) { Validations++; return ChangedFile ? throw new NeedsActionException("Changed file") : Task.CompletedTask; }
    public string? FolderState;
    public int FolderDeletes; public bool FailDeleteFolderOnce;
    public Task ValidateFolderDeletionAsync(VideoJob job, CancellationToken ct) { ct.ThrowIfCancellationRequested(); _ = OwnedDownloadDirectory.Validate(job, FolderState!); return Task.CompletedTask; }
    public Task DeleteFolderAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) { FolderDeletes++; if (FailDeleteFolderOnce) { FailDeleteFolderOnce = false; throw new IOException("Locked folder"); } return OwnedDownloadDirectory.DeleteAsync(job, FolderState!, checkpoint, ct); }
    public Task PreflightAsync(SheetTarget target, CancellationToken ct) => Task.CompletedTask;
    public Task PreflightAsync(ChannelTarget target, CancellationToken ct) => FailUploadPreflight ? throw new NeedsAuthException("Expired fixture grant") : Task.CompletedTask;
    public async Task WriteAndVerifyAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) { if(BeforeSheetWrite is not null) await BeforeSheetWrite(job,ct); Writes++; if (FailSheet) throw new NeedsActionException("Sheet unavailable"); job.SheetVerified = true; await checkpoint(); }
    public async Task DeleteSheetAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
    {
        SheetDeletes++; if (FailDeleteSheetOnce) { FailDeleteSheetOnce = false; throw new NeedsActionException("Sheet delete failed"); }
        job.SheetDeleted = true; await checkpoint();
    }
    public async Task DeleteVideoAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct)
    {
        if(BeforeVideoDelete is not null) await BeforeVideoDelete(job,ct);
        if(FailDeleteVideo) throw new NeedsAuthException("Missing delete grant");
        if (job.YouTubeId is null) throw new NeedsActionException("Unknown upload ID");
        VideoDeletes++; job.YouTubeDeleted = true; await checkpoint();
    }
    public async Task UploadAndVerifyAsync(VideoJob job, Func<Task> checkpoint, IProgress<double> progress, CancellationToken ct) {
        if (job.YouTubeId is null) { Uploads++; job.YouTubeId = "id"; await checkpoint(); }
        if (job.Id == BlockUploadJobId && BlockUploadOnce) { BlockUploadOnce = false; UploadEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); }
        Checks++;
        if (FailCheckNetworkOnce) { FailCheckNetworkOnce = false; throw new HttpRequestException("Offline"); }
        if (PendingChecks-- > 0) throw new YouTubePendingException("Processing");
        await checkpoint();
    }
}
sealed class MemorySecrets : ISecretStore
{
    private readonly Dictionary<string, string> values = new();
    public List<string> Reads { get; } = new();
    public Task<string?> ReadAsync(string key, CancellationToken ct = default) { Reads.Add(key); return Task.FromResult(values.GetValueOrDefault(key)); }
    public Task WriteAsync(string key, string value, CancellationToken ct = default) { values[key] = value; return Task.CompletedTask; }
    public Task DeleteAsync(string key, CancellationToken ct = default) { values.Remove(key); return Task.CompletedTask; }
}
sealed class ScriptedHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handler(request);
}
sealed class CancellableHandler(Func<CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handler(ct);
}
sealed class TokenScriptedHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handler(request, ct);
}
sealed class SetupService : IGoogleConnectionService
{
    public SheetSelection Selection = new("sheet-account-A", "Bảng của Google A", [new(3, "Tab khác"), new(7, "Video")], "sheets-account-A");
    public ChannelTarget Channel = new("channel-account-B", "Kênh của Google B", "youtube-account-B");
    public bool FailCheck, FailChannel; public int ChannelCalls;
    public List<string> Forgotten { get; } = new(); public List<SheetTarget> Checked { get; } = new();
    public Task<SheetSelection> SelectSheetAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(Selection); }
    public Task<ChannelTarget> SelectChannelAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); ChannelCalls++; if (FailChannel) throw new NeedsActionException("No channel permission"); return Task.FromResult(Channel); }
    public Task CheckSheetAsync(SheetTarget target, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Checked.Add(target); if (FailCheck) throw new NeedsActionException("Wrong headers"); return Task.CompletedTask; }
    public Task ForgetAsync(string credential, CancellationToken ct) { Forgotten.Add(credential); return Task.CompletedTask; }
}
static class NodeExtensions { public static JsonNode Also(this JsonNode value, Action<JsonNode> change) { change(value); return value; } }
sealed class WaitingReadStream : Stream
{
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => 100;
    public override long Position { get => 0; set => throw new NotSupportedException(); }
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await Task.Delay(Timeout.Infinite, ct); return 0; }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
sealed class PacedReadStream(byte[] data, TimeSpan delay) : MemoryStream(data)
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (Position == Length) return 0;
        await Task.Delay(delay, ct);
        return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 10)], ct);
    }
}
sealed class ThrottledReadStream(byte[] data, TimeSpan delay) : MemoryStream(data)
{
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (Position == Length) return 0;
        await Task.Delay(delay, ct);
        return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 131072)], ct);
    }
}
sealed class ResetReadStream(byte[] data) : MemoryStream(data)
{
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (Position >= 131072) throw new IOException("Simulated connection reset");
        return base.ReadAsync(buffer[..Math.Min(buffer.Length, 131072)], ct);
    }
}

sealed class FixedChannelReader(SourceChannelMetadata metadata) : ISourceChannelReader
{
    public int Reads;
    public Task<SourceChannelMetadata> ReadAsync(VideoJob job, CancellationToken ct) { Reads++; ct.ThrowIfCancellationRequested(); return Task.FromResult(metadata); }
}
sealed class FakeDouyinBrowser(DouyinChannelSnapshot snapshot) : IDouyinBrowserReader
{
    public int Reads; public string? Hint;
    public Task<DouyinChannelSnapshot> ReadAsync(SourceVideo source, string? secUid, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); Reads++; Hint = secUid; return Task.FromResult(snapshot); }
}
sealed class WorkflowFakes(string root) : IVideoSource, ISheetDestination, IYouTubeDestination
{
    public string Root => root;
    public int Downloads, Writes, Uploads, MetadataReads, Validations, SheetChecks, ChannelChecks;
    public bool FailMetadata;
    public Exception? ChannelFailure;
    public JobActions DuplicateAt;
    Task IVideoSource.CheckDuplicateAsync(VideoJob job, CancellationToken ct) => Check(JobActions.Download);
    Task ISheetDestination.CheckDuplicateAsync(VideoJob job, CancellationToken ct) => Check(JobActions.WriteSheet);
    Task IYouTubeDestination.CheckDuplicateAsync(VideoJob job, CancellationToken ct) => Check(JobActions.UploadYouTube);
    private Task Check(JobActions action) { if (DuplicateAt == action) throw new NeedsActionException("Đã dừng: trùng " + action); return Task.CompletedTask; }
    public Task<SourceVideo> ResolveAsync(string input, CancellationToken ct) => Task.FromResult(new SourceVideo(Rules.SourceId(Rules.DouyinUri(input)) ?? "7659702794928147739", input));
    public Task<SourceMetadata> ReadMetadataAsync(SourceVideo source, CancellationToken ct) { MetadataReads++; if (FailMetadata) throw new NeedsActionException("No duration"); return Task.FromResult(new SourceMetadata(source.Id, 300001, "mock", DateTimeOffset.UtcNow)); }
    public async Task<MediaFile> DownloadAsync(VideoJob job, Func<Task> checkpoint, IProgress<double> progress, CancellationToken ct)
    {
        Downloads++; Directory.CreateDirectory(root); job.DownloadDirectory = Path.Combine(root, job.Id); Directory.CreateDirectory(job.DownloadDirectory);
        var path = Path.Combine(job.DownloadDirectory, job.Name.Replace(':','_').Replace('?','_') + ".mp4"); await File.WriteAllBytesAsync(path, [1,2,3], ct);
        var result = new MediaFile(path, 239, 3, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] {1,2,3})), "video/mp4", CoverStatus: "mock cover");
        job.Media = result; await checkpoint(); return result;
    }
    public Task ValidateFileAsync(MediaFile file, CancellationToken ct) { Validations++; if (!File.Exists(file.Path)) throw new NeedsActionException("Missing file"); return Task.CompletedTask; }
    public Task ValidateFolderDeletionAsync(VideoJob job, CancellationToken ct) => Task.CompletedTask;
    public async Task DeleteFolderAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) { job.FolderDeleted = true; await checkpoint(); }
    public Task PreflightAsync(SheetTarget target, CancellationToken ct) { SheetChecks++; return Task.CompletedTask; }
    public Task PreflightAsync(ChannelTarget target, CancellationToken ct) { ChannelChecks++; if (ChannelFailure is not null) throw ChannelFailure; return Task.CompletedTask; }
    public async Task WriteAndVerifyAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) { Writes++; if (job.Type != "Long") throw new Exception("Must use source duration, not file duration"); job.SheetWriteStarted = true; job.SheetVerified = true; job.SheetRow = Writes + 1; await checkpoint(); }
    public async Task UploadAndVerifyAsync(VideoJob job, Func<Task> checkpoint, IProgress<double> progress, CancellationToken ct) { if (job.YouTubeId is null) { Uploads++; job.YouTubeId = "mock-" + Uploads; job.MediaSendStarted = true; } await checkpoint(); }
    public async Task DeleteSheetAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) { job.SheetDeleted = true; await checkpoint(); }
    public async Task DeleteVideoAsync(VideoJob job, Func<Task> checkpoint, CancellationToken ct) { job.YouTubeDeleted = true; await checkpoint(); }
}

sealed class FakeVideoBrowser(JsonNode info) : IDouyinVideoBrowserReader
{
    public int Reads; public bool ReadCovers;
    public Task<JsonNode> ReadVideoMetadataAsync(SourceVideo source, CancellationToken ct, bool readCovers = true)
    { ct.ThrowIfCancellationRequested(); Reads++; ReadCovers = readCovers; return Task.FromResult(info.DeepClone()); }
}

sealed class RoutingBrowser : IDouyinVideoBrowserReader, IDouyinBrowserReader
{
    public int Forgets;
    public void Forget(SourceVideo source) { Forgets++; }
    public JsonNode? Info; public DouyinChannelSnapshot? Channel; public Exception? Failure; public int VideoReads, ChannelReads; public string? Hint;
    public Task<JsonNode> ReadVideoMetadataAsync(SourceVideo source, CancellationToken ct, bool readCovers = true)
    { ct.ThrowIfCancellationRequested(); VideoReads++; if (Failure is not null) throw Failure; return Info is not null ? Task.FromResult(Info.DeepClone()) : throw new NeedsActionException("Missing guest data"); }
    public Task<DouyinChannelSnapshot> ReadAsync(SourceVideo source, string? secUid, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); ChannelReads++; Hint = secUid; if (Failure is not null) throw Failure; return Channel is not null ? Task.FromResult(Channel) : throw new NeedsActionException("Missing guest data"); }
}

sealed class PublicProbeTraceHandler : DelegatingHandler
{
    public List<object> Responses { get; } = [];
    public PublicProbeTraceHandler() : base(DouyinHttp.Handler()) { }
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        // A bounded diagnostic summary only: never persist body, cookies, signed URLs or identifiers.
        await response.Content.LoadIntoBufferAsync(4*1024*1024,ct);
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        var text = body.Length <= 4*1024*1024 ? Encoding.UTF8.GetString(body) : "";
        string kind = body.Length == 0 ? "empty_body" : "non_json"; string? status = null; int? videos = null;
        if (text.Contains("Uifid Not Found", StringComparison.OrdinalIgnoreCase)) kind = "browser_identity_missing";
        else
        {
            try { var json=JsonNode.Parse(text); kind="json"; status=json?["status_code"]?.ToString(); videos=json?["aweme_list"] is JsonArray list ? list.Count : json?["aweme_detail"] is JsonObject ? 1 : null;
                if(json?["verify_url"] is not null || json?["captcha"] is not null)kind="json_verification"; }
            catch(JsonException) { if(text.Contains("请完成验证")||text.Contains("验证中心")||text.Contains("captcha",StringComparison.OrdinalIgnoreCase))kind="verification_page"; }
        }
        Responses.Add(new { endpoint=request.RequestUri!.GetLeftPart(UriPartial.Path), httpStatus=(int)response.StatusCode, bytes=body.Length, kind, apiStatus=status, videoCount=videos });
        var replacement=new ByteArrayContent(body); foreach(var h in response.Content.Headers)replacement.Headers.TryAddWithoutValidation(h.Key,h.Value);
        response.Content.Dispose();response.Content=replacement;return response;
    }
}
