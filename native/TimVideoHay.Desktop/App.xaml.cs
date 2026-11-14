using System;

using System.Diagnostics;

using System.IO;

using System.Net.Http;

using System.Runtime.InteropServices;

using System.Threading;

using System.Windows;

using TimVideoHay.Core;

using TimVideoHay.Infrastructure;

namespace TimVideoHay.Desktop;

public partial class App : Application

{

    private Mutex? mutex;

    private HttpClient? http;

    private DouyinPublicReader? publicDouyin;

    private MainViewModel? model;

    public static string DataRoot { get; private set; } = "";

    public static string? SmokeOutput { get; private set; }

    public static bool SmokeWizard { get; private set; }
    public static bool SmokeInspect { get; private set; }

    public static string? SessionDiagnosticOutput { get; private set; }

    public static uint ActivationMessage { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)

    {

        base.OnStartup(e);

        try

        {

            var smoke = Array.IndexOf(e.Args, "--smoke-test");

            if (smoke >= 0 && e.Args.Length > smoke + 1) SmokeOutput = Path.GetFullPath(e.Args[smoke + 1]);

            if (SmokeOutput is not null)

                DispatcherUnhandledException += (_, failure) =>

                {

                    // Only isolated fixture runs opt into stack traces; normal app logs exclude exception payloads.

                    Directory.CreateDirectory(Path.GetDirectoryName(SmokeOutput)!);

                    File.WriteAllText(SmokeOutput + ".error.txt", failure.Exception.ToString());

                    failure.Handled = true; Shutdown(1);

                };

            SmokeWizard = Array.IndexOf(e.Args, "--smoke-wizard") >= 0;
            SmokeInspect = SmokeOutput is not null && e.Args.Contains("--smoke-inspect");

            var sessionDiagnostic = Array.IndexOf(e.Args, "--check-douyin-session");

            if (sessionDiagnostic >= 0 && e.Args.Length > sessionDiagnostic + 1) SessionDiagnosticOutput = Path.GetFullPath(e.Args[sessionDiagnostic + 1]);

            var data = Array.IndexOf(e.Args, "--data-dir");

            DataRoot = data >= 0 && e.Args.Length > data + 1 ? Path.GetFullPath(e.Args[data + 1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimVideoHay");

            var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(DataRoot.ToUpperInvariant())))[..20];

            ActivationMessage = RegisterWindowMessage("TimVideoHay.Show." + identity);

            mutex = new Mutex(true, "Local\\TimVideoHay-" + identity, out var owner);

            if (!owner)

            {

                PostMessage(new IntPtr(0xffff), ActivationMessage, IntPtr.Zero, IntPtr.Zero);

                Shutdown(); return;

            }

            PrivateFiles.EnsureDirectory(DataRoot);

            // Explicit diagnostic only. Never reached by a normal job or guest probe.
            var probeAccount = Array.IndexOf(e.Args, "--probe-douyin-account");
            if (probeAccount >= 0 && e.Args.Length > probeAccount + 1)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var profileArg = Array.IndexOf(e.Args, "--probe-account-profile");
                if (profileArg < 0 || e.Args.Length <= profileArg + 1) throw new ArgumentException("Cần chỉ rõ hồ sơ tài khoản được phép kiểm tra.");
                var accountProfile = Path.GetFullPath(e.Args[profileArg + 1]);
                if (!Directory.Exists(accountProfile)) throw new DirectoryNotFoundException("Không có hồ sơ Douyin đã đăng nhập.");
                PrivateFiles.RejectLinks(accountProfile);
                var uri = Rules.DouyinUri(e.Args[probeAccount + 1]);
                var id = Rules.SourceId(uri) ?? throw new ArgumentException("Cần link video Douyin trực tiếp.");
                var output = Path.Combine(DataRoot, "account-probe-report"); Directory.CreateDirectory(output);
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(100));
                try
                {
                    using var publicReader = new DouyinPublicReader();
                    var accountReader = new DouyinBrowserReader(accountProfile);
                    var guestReader = new DouyinBrowserReader(Path.Combine(DataRoot, "douyin-public-browser"), anonymous: true);
                    var reader = new DouyinDataReader(publicReader, guestReader, guestReader, accountReader, accountReader);
                    var probeAccountSource = new SourceVideo(id, "https://www.douyin.com/video/" + id);
                    var info = await reader.ReadVideoMetadataAsync(probeAccountSource, budget.Token);
                    await File.WriteAllTextAsync(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new {
                        succeeded = info["id"]?.ToString() == id, sourceId = id, sourceDuration = info["source_duration_ms"]?.ToString(),
                        formatCount = info["formats"]?.AsArray().Count ?? 0, bothCovers = DouyinBrowserCapture.HasBothCovers(info),
                        accountUsed = true, sourceRoutingChecked = true, googleCalled = false, mediaDownloaded = false }));
                    Shutdown(0);
                }
                catch (Exception ex) when (ex is NeedsActionException or OperationCanceledException)
                {
                    await File.WriteAllTextAsync(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new {
                        succeeded = false, sourceId = id, error = ex is NeedsActionException ? ex.Message : "Hết thời gian kiểm tra phiên tài khoản",
                        accountUsed = true, googleCalled = false, mediaDownloaded = false }));
                    Shutdown(1);
                }
                return;
            }

            var probeGuest = Array.IndexOf(e.Args, "--probe-douyin-guest");
            if (probeGuest >= 0 && e.Args.Length > probeGuest + 1)
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                var uri = Rules.DouyinUri(e.Args[probeGuest + 1]);
                var id = Rules.SourceId(uri) ?? throw new ArgumentException("Cần link video Douyin trực tiếp.");
                var output = Path.Combine(DataRoot, "probe-report"); Directory.CreateDirectory(output);
                using var budget = new CancellationTokenSource(e.Args.Contains("--probe-download") ? TimeSpan.FromMinutes(20) : TimeSpan.FromSeconds(100));
                try
                {
                    var probeSecrets = new DpapiSecretStore(Path.Combine(DataRoot,"secrets"));
                    using var probePublic = new DouyinPublicReader(guestStore: probeSecrets);
                    var probeSource = new SourceVideo(id,"https://www.douyin.com/video/"+id);
                    System.Text.Json.Nodes.JsonNode? info;
                    try { info=e.Args.Contains("--probe-skip-public") ? null : await probePublic.ReadAsync(probeSource,true,budget.Token); }
                    catch(DouyinGuestVerificationException) { info=null; }
                    var guest = new DouyinBrowserReader(Path.Combine(DataRoot, "douyin-public-browser"), output, anonymous: true, guestToken:probePublic.CachedGuestTokenAsync);
                    info ??= await guest.ReadVideoMetadataAsync(probeSource, budget.Token);
                    MediaFile? probeMedia = null;
                    if(e.Args.Contains("--probe-download"))
                    {
                        var toolsArg=Array.IndexOf(e.Args,"--probe-tools");
                        var toolRoot=toolsArg>=0 ? Path.GetFullPath(e.Args[toolsArg+1]) : Path.Combine(AppContext.BaseDirectory,"tools");
                        var probeJob=new VideoJob { Source=probeSource, Input=probeSource.CanonicalUrl, Name="Hành trình trên một hòn đảo", Folder=Path.Combine(DataRoot,"downloads"), Mode=JobMode.DownloadOnly, Actions=JobActions.Download };
                        var downloader=new DouyinSource(toolRoot,Path.Combine(DataRoot,"temp"),probeSecrets,new ProbeVideoMetadata(info));
                        probeMedia=await downloader.DownloadAsync(probeJob,()=>System.Threading.Tasks.Task.CompletedTask,new Progress<double>(),budget.Token);
                    }
                    await File.WriteAllTextAsync(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new {
                        succeeded = true, sourceId = id, sourceDuration = info["source_duration_ms"]?.ToString(),
                        formatCount = info["formats"]?.AsArray().Count ?? 0, bothCovers = DouyinBrowserCapture.HasBothCovers(info),
                        accountCreated = false, accountUsed = false, googleCalled = false, mediaDownloaded = probeMedia is not null, media = probeMedia is null ? null : new { probeMedia.Path, probeMedia.Width, probeMedia.Height, probeMedia.Length, probeMedia.Duration, probeMedia.FormatId, probeMedia.CoverStatus } }));
                    Shutdown(0);
                }
                catch (Exception ex) when (ex is NeedsActionException or OperationCanceledException)
                {
                    await File.WriteAllTextAsync(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new {
                        succeeded = false, sourceId = id, error = ex is NeedsActionException ? ex.Message : "Hết thời gian thử khách",
                        accountCreated = false, accountUsed = false, googleCalled = false, mediaDownloaded = false }));
                    Shutdown(1);
                }
                return;
            }

            var readChannel = Array.IndexOf(e.Args, "--read-douyin-channel");

            if (readChannel >= 0 && e.Args.Length > readChannel + 1)

            {

                ShutdownMode = ShutdownMode.OnExplicitShutdown;

                var uri = Rules.DouyinUri(e.Args[readChannel + 1]);

                var id = Rules.SourceId(uri) ?? throw new ArgumentException("Cần link video Douyin trực tiếp.");

                var output = Path.Combine(DataRoot, "reader-report"); Directory.CreateDirectory(output);

                                try

                {

                    var job = new VideoJob { Source = new(id, "https://www.douyin.com/video/" + id) };

                    publicDouyin = new DouyinPublicReader(guestStore: new DpapiSecretStore(Path.Combine(DataRoot, "secrets")));

                    var guest = new DouyinBrowserReader(Path.Combine(DataRoot, "douyin-public-browser"), output, anonymous: true, guestToken: publicDouyin.CachedGuestTokenAsync);

                    var account = new DouyinBrowserReader(Path.Combine(DataRoot, "douyin-browser"), output);

                    var reader = new DouyinDataReader(publicDouyin, guest, guest, account, account);

                    var read = await new DouyinChannelReader(new ChannelNameTranslator(), reader).ReadAsync(job, default);

                    await File.WriteAllTextAsync(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { succeeded = true, read, type = read.ContentType, googleCalled = false, downloadedMedia = false }));

                }

                catch (NeedsActionException ex) { await File.WriteAllTextAsync(Path.Combine(output, "result.json"), System.Text.Json.JsonSerializer.Serialize(new { succeeded = false, error = ex.Message, googleCalled = false, downloadedMedia = false })); }

                Shutdown(); return;

            }

            var temp = Path.Combine(DataRoot, "temp");

            PrivateFiles.CleanTemporaryDirectory(temp);

            http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(2) };

            http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);

            var store = new LocalStore(DataRoot);

            var secrets = new DpapiSecretStore(Path.Combine(DataRoot, "secrets"));

            var auth = new GoogleAuth(secrets, http); var transport = new GoogleTransport(auth, http);

            Action<string> sourceNotice = message => Dispatcher.BeginInvoke(() => model?.ReportSourceActivity(message));

            publicDouyin = new DouyinPublicReader(guestStore: secrets);

            var accountBrowser = new DouyinBrowserReader(Path.Combine(DataRoot, "douyin-browser"), notify: sourceNotice);

            var guestBrowser = new DouyinBrowserReader(Path.Combine(DataRoot, "douyin-public-browser"), anonymous: true, guestToken: publicDouyin.CachedGuestTokenAsync, notify: sourceNotice);

            var douyinReader = new DouyinDataReader(publicDouyin, guestBrowser, guestBrowser, accountBrowser, accountBrowser, sourceNotice);

            var sheets = new GoogleSheets(transport, new DouyinChannelReader(new ChannelNameTranslator(), douyinReader)); var youtube = new YouTubeDestination(transport, secrets);

            var source = new DouyinSource(Path.Combine(AppContext.BaseDirectory, "tools"), temp, secrets, douyinReader, notify: sourceNotice);

            var coordinator = new JobCoordinator(store, source, sheets, youtube);

            var log = new JobLog(Path.Combine(DataRoot, "logs")); coordinator.Changed += log.Record;

            model = new MainViewModel(store, secrets, auth, sheets, youtube, coordinator, DataRoot);

            await model.InitializeAsync();

            MainWindow = new MainWindow(model); MainWindow.Show();

        }

        catch (Exception ex)

        {

            MessageBox.Show("Không thể khởi động app. Kiểm tra quyền thư mục dữ liệu và bản cài đầy đủ.\nLoại lỗi: " + ex.GetType().Name, "Tìm video hay", MessageBoxButton.OK, MessageBoxImage.Error);

            Shutdown(1);

        }

    }

    public async System.Threading.Tasks.Task ExitAsync()

    {

        if (model is not null) await model.ShutdownAsync(); Shutdown();

    }

    protected override void OnExit(ExitEventArgs e) { http?.Dispose(); publicDouyin?.Dispose(); mutex?.Dispose(); base.OnExit(e); }

    private sealed class ProbeVideoMetadata(System.Text.Json.Nodes.JsonNode info) : IDouyinVideoBrowserReader
    {
        public System.Threading.Tasks.Task<System.Text.Json.Nodes.JsonNode> ReadVideoMetadataAsync(SourceVideo source, CancellationToken ct, bool readCovers = true)
        {
            ct.ThrowIfCancellationRequested();
            if(info["id"]?.ToString()!=source.Id)throw new InvalidOperationException("Probe source mismatch");
            return System.Threading.Tasks.Task.FromResult(info.DeepClone());
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

}

