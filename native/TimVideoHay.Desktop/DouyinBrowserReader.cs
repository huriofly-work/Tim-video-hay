using System;
using System.Collections.Generic;

using System.IO;

using System.Linq;

using System.Text;

using System.Text.Json;

using System.Text.Json.Nodes;

using System.Threading;

using System.Threading.Tasks;

using System.Windows;

using System.Windows.Controls;

using System.Windows.Threading;

using Microsoft.Web.WebView2.Core;

using Microsoft.Web.WebView2.Wpf;

using TimVideoHay.Core;

using TimVideoHay.Infrastructure;



namespace TimVideoHay.Desktop;



public sealed class DouyinBrowserReader(string profileDirectory, string? diagnosticDirectory = null, bool anonymous = false, Func<CancellationToken, Task<string?>>? guestToken = null, Action<string>? notify = null) : IDouyinBrowserReader, IDouyinVideoBrowserReader

{

    // Real WebView UI against local fixture HTML; no Douyin/network/account access.

    public static async Task<object> CheckVerificationSmokeAsync(string fixtureDirectory)

    {

        var fixtures = new[] { "<h2>Log in to Douyin</h2>", "<p>请完成下列验证后继续</p>", "<iframe name='verification' srcdoc='<p>请完成验证后继续</p>' style='width:500px;height:300px'></iframe>", "<p>请完成下列验证后继续</p>", "<iframe name='verification' srcdoc='<p>请完成验证后继续</p>' style='width:500px;height:300px'></iframe>", "<h2>Log in to Douyin</h2>" };

        for (var i = 0; i < fixtures.Length; i++)

        {

            using var cancel = new CancellationTokenSource();

            var window = new ReaderWindow(fixtureDirectory, new("123", "https://www.douyin.com/video/123"), null, cancel.Token, null, videoOnly: true, anonymous: i >= 3, smokeHtml: "<meta charset='utf-8'>" + fixtures[i], guestToken: _ => Task.FromResult<string?>("anonymous-fixture-ticket"));

            window.Show();

            try { await window.CheckSmokeInteractionAsync(i == 0 || i == 5); }

            catch (Exception ex) { throw new InvalidOperationException("Verification fixture " + i + " failed.", ex); }

            finally { cancel.Cancel(); }

            try { await window.Completion; }

            catch (OperationCanceledException) { }

            catch (NeedsActionException) when (i >= 3) { }

        }

        foreach (var guest in new[] { false, true })

        {

            using var challengeCancel = new CancellationTokenSource();

            var challengeHtml = "<meta charset='utf-8'><div role='dialog' style='position:relative;width:640px;height:350px'><h2>Log in to Douyin</h2><button aria-label='Close' style='position:absolute;top:10px;right:10px' onclick='this.parentElement.remove()'>×</button><iframe name='verification' srcdoc='<p>请完成验证后继续</p>' style='width:500px;height:200px'></iframe></div>";

            var challengeWindow = new ReaderWindow(fixtureDirectory, new("123", "https://www.douyin.com/video/123"), null, challengeCancel.Token, null, videoOnly: true, anonymous: guest, smokeHtml: challengeHtml, guestToken: _ => Task.FromResult<string?>("anonymous-fixture-ticket"));

            challengeWindow.Show();

            try { await challengeWindow.CheckSmokeInteractionAsync(false); }

            finally { challengeCancel.Cancel(); }

            try { await challengeWindow.Completion; } catch (OperationCanceledException) { } catch (NeedsActionException) when (guest) { }

            using var cancel = new CancellationTokenSource();

            var html = "<meta charset='utf-8'><button onclick='document.body.dataset.wrong=1'>Other</button><div role='dialog' style='position:relative;width:640px;height:350px'><h2>Log in to Douyin</h2><button aria-label='Close' style='position:absolute;top:10px;right:10px' onclick='this.parentElement.remove()'>×</button></div>";

            var window = new ReaderWindow(fixtureDirectory, new("123", "https://www.douyin.com/video/123"), null, cancel.Token, null, videoOnly: true, anonymous: guest, smokeHtml: html, guestToken: _ => Task.FromResult<string?>("anonymous-fixture-ticket"));

            window.Show();

            try { await window.CheckSmokeDismissAsync(); }

            finally { cancel.Cancel(); }

            try { await window.Completion; } catch (OperationCanceledException) { }

        }

        using (var cancel = new CancellationTokenSource())
        {
            var html = "<meta charset='utf-8'><div role='dialog' style='position:relative;width:640px;height:350px'><h2>Log in to Douyin</h2><svg width='32' height='32' style='position:absolute;top:10px;right:10px' onclick='document.body.dataset.wrong=1'><circle cx='16' cy='16' r='8'/></svg><span style='position:absolute;top:10px;right:50px' onclick='this.parentElement.remove()'><svg width='37' height='36' viewBox='0 0 37 36'><path d='M12.7929 22.2426C12.4024 22.6331 12.4024 23.2663 12.7929 23.6568C13.1834 24.0474 13.8166 24.0474 14.2071 23.6568L18.5 19.3639L22.7929 23.6568C23.1834 24.0474 23.8166 24.0474 24.2071 23.6568C24.5976 23.2663 24.5976 22.6331 24.2071 22.2426L19.9142 17.9497L24.1066 13.7573C24.4971 13.3668 24.4971 12.7336 24.1066 12.3431C23.7161 11.9526 23.0829 11.9526 22.6924 12.3431L18.5 16.5355L14.3076 12.3431C13.9171 11.9526 13.2839 11.9526 12.8934 12.3431C12.5029 12.7336 12.5029 13.3668 12.8934 13.7573L17.0858 17.9497L12.7929 22.2426Z'/></svg></span></div>";
            var window = new ReaderWindow(fixtureDirectory, new("123", "https://www.douyin.com/video/123"), null, cancel.Token, null, videoOnly: true, anonymous: true, smokeHtml: html, guestToken: _ => Task.FromResult<string?>("anonymous-fixture-ticket"));
            window.Show();
            try
            {
                for(var invitation=0;invitation<3;invitation++)
                {
                    if(invitation>0)await window.ResetInvitationFixtureAsync(html);
                    await window.CheckSmokeDismissAsync(invitation+1);
                }
            }
            finally { cancel.Cancel(); }
            try { await window.Completion; } catch(OperationCanceledException) { }
        }

        return new { optionalLoginAutomaticallyDismissed = true, unlabelledSvgCrossDismissed = true, repeatedInvitationsDismissed = true, backgroundBrowserRemainsActive = true, nonCrossControlNotClicked = true, unclosableGuestInvitationWaits = true, normalReadsHidden = true, challengeShownThenHidden = true, loginWaits = true, captchaWaits = true, iframeCaptchaWaits = true, guestCaptchaWaits = true, guestIframeCaptchaWaits = true, resumesAfterChallengeClears = true, guestTimeoutPreservesJob = true, guestCookieIsolatedFromAccountProfile = true, expiredMetadataDeadlineDoesNotCloseAccountWindow = true, accountSessionUsed = false, douyinPageOpened = false, diagnostic = "Actual WebView2 with local fixture HTML; no real signed-in challenge solved" };

    }

    public async Task<bool?> OpenSessionAsync(CancellationToken ct)

    {

        ct.ThrowIfCancellationRequested();

        return await await Application.Current.Dispatcher.InvokeAsync(async () =>

        {

            PrivateFiles.EnsureDirectory(profileDirectory);

            var window = new ReaderWindow(profileDirectory, new("", "https://www.douyin.com/"), null, ct, diagnosticDirectory, sessionOnly: true, anonymous: anonymous);

            if (Application.Current.MainWindow is { IsVisible: true } owner) window.Owner = owner;

            window.Show(); await window.Completion; return window.Authenticated;

        });

    }

    public async Task<DouyinChannelSnapshot> ReadAsync(SourceVideo source, string? secUid, CancellationToken ct)

    {

        ct.ThrowIfCancellationRequested();

        return await await Application.Current.Dispatcher.InvokeAsync(() => ReadOnUiAsync(source, secUid, ct));

    }

    public async Task<JsonNode> ReadVideoMetadataAsync(SourceVideo source, CancellationToken ct, bool readCovers = true)

    {

        ct.ThrowIfCancellationRequested();

        return await await Application.Current.Dispatcher.InvokeAsync(async () =>

        {

            PrivateFiles.EnsureDirectory(profileDirectory);

            var window = new ReaderWindow(profileDirectory, source, null, ct, diagnosticDirectory, videoOnly: true, readCovers: readCovers, anonymous: anonymous, guestToken: guestToken, notify: notify);

            if (Application.Current.MainWindow is { IsVisible: true } owner) window.Owner = owner;

            window.Show(); return (JsonNode)await window.Completion;

        });

    }

    private async Task<DouyinChannelSnapshot> ReadOnUiAsync(SourceVideo source, string? secUid, CancellationToken ct)

    {

        ct.ThrowIfCancellationRequested(); PrivateFiles.EnsureDirectory(profileDirectory);

        var window = new ReaderWindow(profileDirectory, source, secUid, ct, diagnosticDirectory, anonymous: anonymous, guestToken: guestToken, notify: notify);

        if (Application.Current.MainWindow is { IsVisible: true } owner) window.Owner = owner;

        window.Show();

        return (DouyinChannelSnapshot)await window.Completion;

    }



    internal static bool HasVisibleInteraction => Application.Current.Windows.OfType<ReaderWindow>().Any(x => x.IsVisible && x.ShowInTaskbar);

    private sealed class ReaderWindow : Window

    {

        private readonly WebView2 browser = new();

        private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12), Text = "Đang mở Douyin để lấy 粉丝 và 10 video mới nhất… Nếu được yêu cầu, hãy đăng nhập/xác minh ngay trong cửa sổ này." };

        private readonly TaskCompletionSource<object> result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(3) };

        private readonly SourceVideo source;

        private DouyinBrowserCapture capture;

        private CancellationTokenRegistration cancellation;

        private DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(3);

        private bool finished, scrolling, navigatedChannel;

        private bool previewSaved;

        private readonly string? diagnosticDirectory;

        private readonly bool sessionOnly;

        private readonly bool videoOnly;

        private readonly bool readCovers;

        private readonly bool anonymous;

        private bool selectRequested;

        private int scrollAttempts;

        private DateTimeOffset lastScroll;

        private bool waitingForUser;

        public bool? Authenticated { get; private set; }

        private JsonNode? videoMetadata;

        private DateTimeOffset? coversStartedAt;

        private bool coverRouteOpened;

        private object? pendingResult;

        private Exception? pendingError;

        private readonly DateTimeOffset openedAt = DateTimeOffset.UtcNow;

        private DateTimeOffset lastVideoNavigation;

        private int videoRoute;

        private int detailRequestedRoute = -1;

        private int postResponses, acceptedPostResponses;
        private readonly List<object> diagnosticResponses = [];

        private readonly System.Collections.Generic.HashSet<string> sessionRoutes = new();

        private bool checkingLogin;

        private bool? loginPromptVisible;

        private bool? verificationVisible;

        private int loginDismissAttempts;

        private readonly Action<string>? notify;

        private readonly System.Collections.Generic.List<(CoreWebView2Frame Frame, CoreWebView2Frame Root)> frames = new();

        private string? actualProfileDirectory;

        private string? actualProfileName;

        private DateTimeOffset? navigationFinishedAt;

        private void RecordSessionState()

        {

            if (!sessionOnly) return;

            try

            {

                var logs = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(profileDirectoryForLog))!, "logs");

                PrivateFiles.EnsureDirectory(logs);

                var report = Path.Combine(logs, "douyin-session-last.json");

                File.WriteAllText(report, JsonSerializer.Serialize(new { version = typeof(DouyinBrowserReader).Assembly.GetName().Version?.ToString(3), timeUtc = DateTimeOffset.UtcNow,

                    actualProfileDirectory, actualProfileName, authenticated = Authenticated, loginPromptVisible, verificationVisible, credentialValuesLogged = false }));

                PrivateFiles.ProtectFile(report);

            }

            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }

        }

        private readonly string profileDirectoryForLog;

        public Task<object> Completion => result.Task;

        public ReaderWindow(string directory, SourceVideo source, string? secUid, CancellationToken ct, string? diagnosticDirectory, bool sessionOnly = false, bool videoOnly = false, bool readCovers = true, bool anonymous = false, string? smokeHtml = null, Func<CancellationToken, Task<string?>>? guestToken = null, Action<string>? notify = null)

        {

            this.notify = notify; profileDirectoryForLog = directory; this.videoOnly = videoOnly; this.readCovers = readCovers; this.anonymous = anonymous;

            if (anonymous) deadline = DateTimeOffset.UtcNow.AddSeconds(35);

            this.sessionOnly = sessionOnly;

            this.diagnosticDirectory = diagnosticDirectory;

            this.source = source; capture = new(source, secUid);

            if (!sessionOnly && !videoOnly && capture.SecUid is null)

                status.Text = "Đang mở đúng video nguồn " + source.Id + " để xác định chủ kênh, sau đó mới đọc 粉丝 và 10 video mới nhất. Hoàn tất đăng nhập/xác minh nếu trang yêu cầu.";

            Title = "Đọc kênh nguồn Douyin — Tìm video hay"; Width = 1040; Height = 780; MinWidth = 700; MinHeight = 550; WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SetResourceReference(FrameworkElement.StyleProperty, typeof(Window));

            if (sessionOnly) { Title = "Phiên Douyin của app — Tìm video hay"; status.Text = "Bạn có thể đăng nhập Douyin tại đây rồi đóng cửa sổ. App giữ phiên cho các lần đọc kênh sau, không cần xuất cookie. Đây là phiên riêng của app."; }

            if (videoOnly) { Title = "Đọc video Douyin — Tìm video hay"; status.Text = "Đang lấy dữ liệu video từ phiên Douyin của app. Hoàn tất đăng nhập/xác minh nếu trang yêu cầu; app sẽ tự tiếp tục khi đọc được đúng video."; }

            if (anonymous) { Title = "Đọc Douyin công khai — không dùng tài khoản"; status.Text = "Đang đọc nguồn công khai trong phiên ẩn danh riêng. Không đăng nhập tại đây. Chỉ dùng phiên tài khoản nếu thiếu dữ liệu bắt buộc và không gặp chặn truy cập."; }

            if (!sessionOnly)

            {

                ShowActivated = false; ShowInTaskbar = false; Opacity = 1;

                WindowStartupLocation = WindowStartupLocation.Manual;

                Left = SystemParameters.VirtualScreenLeft - Width - 100; Top = SystemParameters.VirtualScreenTop;

            }

            var panel = new DockPanel(); DockPanel.SetDock(status, Dock.Top); panel.Children.Add(status);

            var profileLabel = new TextBlock { Text = "Hồ sơ Douyin: " + Path.GetFullPath(directory), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = Path.GetFullPath(directory), FontSize = 12, Margin = new Thickness(12, 0, 12, 8) };

            DockPanel.SetDock(profileLabel, Dock.Top); panel.Children.Add(profileLabel);

            var actions = new WrapPanel { Margin = new Thickness(12) };

            var reload = new Button { Content = "Tải lại trang", Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(0, 0, 12, 0) };

            reload.Click += (_, _) => { capture = new(source, capture.SecUid); detailRequestedRoute = -1; selectRequested = false; scrollAttempts = 0; deadline = DateTimeOffset.UtcNow.AddSeconds(anonymous ? 35 : 180); lastVideoNavigation = DateTimeOffset.UtcNow; browser.CoreWebView2?.Reload(); };

            var cancel = new Button { Content = sessionOnly ? "Đóng cửa sổ" : "Đóng và dừng công việc", Padding = new Thickness(14, 7, 14, 7) }; cancel.Click += (_, _) => Close();

            actions.Children.Add(reload); actions.Children.Add(cancel); DockPanel.SetDock(actions, Dock.Bottom); panel.Children.Add(actions); panel.Children.Add(browser); Content = panel;

            Closing += (_, _) => { finished = true; timer.Stop(); };

            Closed += (_, _) =>

            {

                timer.Stop(); cancellation.Dispose(); finished = true; RecordSessionState();

                if (diagnosticDirectory is not null)

                    File.WriteAllText(Path.Combine(diagnosticDirectory, "capture-state.json"), JsonSerializer.Serialize(new { authorIdentified = capture.SecUid is not null, capture.ProfileReceived, capture.PagesReceived, postResponses, acceptedPostResponses, googleCalled = false }));

                if (ct.IsCancellationRequested) result.TrySetCanceled(ct);

                else if (pendingError is not null) result.TrySetException(pendingError);

                else if (pendingResult is not null) result.TrySetResult(pendingResult);

                else if (sessionOnly) result.TrySetResult(true);

                else result.TrySetException(new DouyinTerminalException("Đã đóng bộ đọc Douyin. Công việc được giữ lại; bấm Tiếp tục để đọc lại dữ liệu nguồn.", blocked: verificationVisible == true));

                browser.Dispose();

            };

            cancellation = ct.Register(() => Dispatcher.BeginInvoke(() => { if (!finished) Close(); }));

            Loaded += async (_, _) =>

            {

                try

                {

                    var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: directory);

                    if (finished) return;

                    if (anonymous)

                    {

                        var options = environment.CreateCoreWebView2ControllerOptions();

                        options.ProfileName = "PublicReader"; options.IsInPrivateModeEnabled = true;

                        await browser.EnsureCoreWebView2Async(environment, options);

                    }

                    else await browser.EnsureCoreWebView2Async(environment);

                    if (finished) return;

                    var core = browser.CoreWebView2;

                    if (anonymous && guestToken is not null && await guestToken(ct) is { } token)

                    {

                        // Seed only the dedicated anonymous ttwid. Never export/import an account session.

                        var cookie = core.CookieManager.CreateCookie("ttwid", token, ".douyin.com", "/");

                        cookie.IsSecure = true; cookie.IsHttpOnly = true; core.CookieManager.AddOrUpdateCookie(cookie);

                    }

                    actualProfileDirectory = core.Environment.UserDataFolder; actualProfileName = core.Profile.ProfileName;

                    profileLabel.Text = "Hồ sơ Douyin đang dùng: " + actualProfileDirectory;

                    RecordSessionState();

                    if (diagnosticDirectory is not null) File.WriteAllText(Path.Combine(diagnosticDirectory, "session-profile.json"), JsonSerializer.Serialize(new { requested = Path.GetFullPath(directory), actual = core.Environment.UserDataFolder, inPrivate = core.Profile.IsInPrivateModeEnabled, profileName = core.Profile.ProfileName, browserProcessId = core.BrowserProcessId, browserVersion = core.Environment.BrowserVersionString }));

                    core.Settings.AreDevToolsEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;

                    core.Settings.IsWebMessageEnabled = false; core.Settings.AreHostObjectsAllowed = false; core.IsMuted = true;

                    core.NewWindowRequested += (_, e) => e.Handled = true;

                    core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;

                    core.DownloadStarting += (_, e) => e.Cancel = true;

                    core.NavigationStarting += (_, e) =>

                    {

                        if (smokeHtml is not null && (e.Uri == "about:blank" || e.Uri == "data:text/html;charset=utf-8;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(smokeHtml)))) return;

                        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "www.douyin.com" || !uri.IsDefaultPort || uri.UserInfo.Length != 0)

                        { e.Cancel = true; status.Text = "Chỉ mở trang Douyin trong bộ đọc này. Hãy đăng nhập/xác minh trên trang hiện tại."; }

                    };

                    core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);

                    core.WebResourceRequested += (_, e) =>

                    {

                        var mediaHost = Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) && (uri.Host == "douyinvod.com" || uri.Host.EndsWith(".douyinvod.com", StringComparison.OrdinalIgnoreCase));

                        if (e.ResourceContext == CoreWebView2WebResourceContext.Media || mediaHost || e.Request.Headers.Contains("Range"))

                            e.Response = environment.CreateWebResourceResponse(new MemoryStream(), 204, "No Content", "");

                    };

                    core.WebResourceResponseReceived += ResponseReceived;

                    core.FrameCreated += (_, e) => TrackFrame(e.Frame, e.Frame);

                    core.NavigationCompleted += async (_, _) =>

                    {

                        navigationFinishedAt = DateTimeOffset.UtcNow;

                        if (diagnosticDirectory is null || !Uri.TryCreate(core.Source, UriKind.Absolute, out var page)) return;

                        File.WriteAllText(Path.Combine(diagnosticDirectory, "navigation.json"), JsonSerializer.Serialize(new { path = page.AbsolutePath, modalRequested = page.Query.Contains("modal_id=" + source.Id, StringComparison.Ordinal), sourceId = source.Id }));

                        if (sessionOnly)

                        {

                            await Task.Delay(6000); if (finished) return;

                            try { using var preview = File.Create(Path.Combine(diagnosticDirectory, "session-preview.png")); await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview); }

                            catch (Exception) { /* Diagnostic capture must not interrupt the user session. */ }

                        }

                    };

                    await core.AddScriptToExecuteOnDocumentCreatedAsync("document.addEventListener('play',e=>{if(e.target instanceof HTMLMediaElement)e.target.pause()},true);");

                    await core.AddScriptToExecuteOnDocumentCreatedAsync("(()=>{window.__timVideoHaySourceNotice=null;const notices=" + JsonSerializer.Serialize(DouyinBrowserCapture.UnavailableNotices) + ";const check=()=>{for(const e of document.querySelectorAll('body div,body span,body p,[role=alert]')){const s=e.textContent.trim().replace(/[.。]$/,'');if(notices.includes(s)&&!e.closest('[data-e2e*=comment],[contenteditable]')&&e.getClientRects().length&&getComputedStyle(e).visibility!=='hidden'){window.__timVideoHaySourceNotice=s;break}}};new MutationObserver(check).observe(document,{childList:true,subtree:true,characterData:true});document.addEventListener('DOMContentLoaded',check)})()");

                    if (finished) return;

                    if (!sessionOnly) PresentInteraction(false);

                    if (smokeHtml is not null) core.NavigateToString(smokeHtml);

                    else { NavigateChannelOrVideo(); timer.Tick += sessionOnly ? SessionTick : Tick; timer.Start(); }

                }

                catch (WebView2RuntimeNotFoundException)

                { Fail("Máy chưa có Microsoft Edge WebView2 Runtime. Xem INSTALLATION.md để cài Evergreen Runtime miễn phí, rồi Tiếp tục.", terminal: true); }

                catch (Exception ex) when (ex is not OperationCanceledException)

                { if (!finished) Fail("Không mở được bộ đọc Douyin (" + ex.GetType().Name + "). Đóng cửa sổ và thử lại sau khi kiểm tra WebView2 Runtime.", terminal: true); }

            };

        }

        public Task ResetInvitationFixtureAsync(string html) => browser.CoreWebView2.ExecuteScriptAsync("document.body.innerHTML="+JsonSerializer.Serialize(html));
        public async Task CheckSmokeDismissAsync(int expectedDismissAttempts = 1)

        {

            for (var i = 0; navigationFinishedAt is null && !finished && i < 200; i++) await Task.Delay(100);

            if (finished || navigationFinishedAt is null || ShowInTaskbar || Left + Width >= SystemParameters.VirtualScreenLeft) throw new InvalidOperationException("Normal browser read was not hidden.");

            navigationFinishedAt = DateTimeOffset.UtcNow.AddSeconds(-10);

            await CheckInteractionAsync();
            if (await browser.CoreWebView2.ExecuteScriptAsync("document.visibilityState") != "\"visible\"")
                throw new InvalidOperationException("Background WebView SDK was suspended by visibility.");

            var state = await browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('[role=dialog]')===null && !document.body.dataset.wrong");

            if (state != "true" || loginDismissAttempts != expectedDismissAttempts || ShowInTaskbar || Left + Width >= SystemParameters.VirtualScreenLeft || Completion.IsCompleted) throw new InvalidOperationException("Login invitation was not automatically dismissed while hidden.");

            await CheckInteractionAsync();

            if (loginPromptVisible == true || verificationVisible == true || ShowInTaskbar || Left + Width >= SystemParameters.VirtualScreenLeft) throw new InvalidOperationException("Dismissed invitation still blocks the reader.");

        }

        public async Task CheckSmokeInteractionAsync(bool login)

        {

            for (var i = 0; navigationFinishedAt is null && !finished && i < 200; i++) await Task.Delay(100);

            if (finished || navigationFinishedAt is null) throw new InvalidOperationException("Fixture WebView failed to load.");

            await Task.Delay(700); navigationFinishedAt = DateTimeOffset.UtcNow.AddSeconds(-10);

            deadline = DateTimeOffset.UtcNow.AddMinutes(-1);

            await CheckInteractionAsync();

            if (login ? loginPromptVisible != true : verificationVisible != true)

            {

                var fixture = await browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({text:document.body?.innerText,ready:document.readyState,width:innerWidth,height:innerHeight})");

                throw new InvalidOperationException("Fixture login/challenge not detected: " + fixture + "; login=" + loginPromptVisible + "; verification=" + verificationVisible);

            }

            Tick(null, EventArgs.Empty); await Task.Delay(300);

            if (!IsVisible || Completion.IsCompleted || finished) throw new InvalidOperationException("Account challenge closed on timeout.");

            await browser.CoreWebView2.ExecuteScriptAsync("document.body.innerHTML='<p>Verification completed</p>'");

            navigationFinishedAt = DateTimeOffset.UtcNow.AddSeconds(-10);

            await CheckInteractionAsync(); Tick(null, EventArgs.Empty); await Task.Delay(200);

            if (loginPromptVisible == true || verificationVisible == true || waitingForUser || finished || ShowInTaskbar || Left + Width >= SystemParameters.VirtualScreenLeft) throw new InvalidOperationException("Reader did not resume after verification cleared.");

            var cookies = await browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.douyin.com/");

            if (anonymous)

            {

                if (!browser.CoreWebView2.Profile.IsInPrivateModeEnabled || cookies.Count != 1 || cookies[0].Name != "ttwid" || cookies[0].Value != "anonymous-fixture-ticket") throw new InvalidOperationException("Anonymous browser ticket isolation failed.");

                deadline = DateTimeOffset.UtcNow.AddMinutes(-1); Tick(null, EventArgs.Empty); await Task.Delay(100);

                if (!finished || !Completion.IsCompleted) throw new InvalidOperationException("Guest timeout did not automatically fall back.");

            }

            else if (cookies.Count != 0) throw new InvalidOperationException("Account fixture received a guest cookie.");

        }

        private void NavigateChannelOrVideo()

        {

            navigatedChannel = !videoOnly && capture.SecUid is not null;

            lastVideoNavigation = DateTimeOffset.UtcNow;

            browser.CoreWebView2.Navigate(navigatedChannel ? "https://www.douyin.com/user/" + capture.SecUid

                : sessionOnly ? source.CanonicalUrl : videoRoute switch {

                    0 => source.CanonicalUrl,

                    1 => "https://www.douyin.com/jingxuan?modal_id=" + Uri.EscapeDataString(source.Id),

                    _ => "https://www.douyin.com/?modal_id=" + Uri.EscapeDataString(source.Id)

                });

        }

        private async void ResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)

        {

            if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var action) && (action.Host == "douyin.com" || action.Host.EndsWith(".douyin.com", StringComparison.Ordinal))

                && (action.AbsolutePath.EndsWith("/logout/", StringComparison.Ordinal) || action.AbsolutePath.EndsWith("/logout", StringComparison.Ordinal)) && e.Response.StatusCode == 200)

            {

                Authenticated = false; RecordSessionState();

            }

            if (sessionOnly && diagnosticDirectory is not null && Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var route)

                && (route.Host == "douyin.com" || route.Host.EndsWith(".douyin.com", StringComparison.Ordinal))

                && (route.AbsolutePath.Contains("passport", StringComparison.Ordinal) || route.AbsolutePath.Contains("user/", StringComparison.Ordinal) || route.AbsolutePath.Contains("login", StringComparison.Ordinal)))

            {

                sessionRoutes.Add(route.Host + route.AbsolutePath + " " + e.Response.StatusCode);

                File.WriteAllText(Path.Combine(diagnosticDirectory, "session-routes.json"), JsonSerializer.Serialize(sessionRoutes));

            }

            if (finished || !Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) || uri.Host != "www.douyin.com") return;

            if (!sessionOnly && e.Response.StatusCode == 429 && (uri.AbsolutePath.StartsWith("/aweme/v1/web/", StringComparison.Ordinal)

                || uri.AbsolutePath.StartsWith("/video/", StringComparison.Ordinal) || uri.AbsolutePath.StartsWith("/user/", StringComparison.Ordinal) || uri.AbsolutePath == "/jingxuan"))

            {

                DateTimeOffset? retryAt = null;

                if (e.Response.Headers.Contains("Retry-After"))

                {

                    var value = e.Response.Headers.GetHeader("Retry-After");

                    if (long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds <= 31536000)

                        retryAt = DateTimeOffset.UtcNow.AddSeconds(seconds);

                    else if (DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var date)) retryAt = date;

                }

                Fail("Douyin báo giới hạn truy cập (HTTP 429). Công việc được lưu để tiếp tục sau; không đổi phiên để thử tiếp ngay.", terminal: true, blocked: true, retryAt: retryAt); return;

            }

            var loginResponse = uri.AbsolutePath == "/aweme/v1/web/user/profile/self/";

            if (!loginResponse && (sessionOnly || uri.AbsolutePath is not ("/aweme/v1/web/aweme/detail/" or "/aweme/v1/web/user/profile/other/" or "/aweme/v1/web/aweme/post/"))) return;

            if (uri.AbsolutePath.EndsWith("/post/", StringComparison.Ordinal)) postResponses++;

            if (diagnosticDirectory is not null && !sessionOnly && diagnosticResponses.Count < 32)
            {
                diagnosticResponses.Add(new { endpoint=uri.AbsolutePath, httpStatus=e.Response.StatusCode, browserIdentityHeader=e.Request.Headers.Contains("uifid"), queryKeys=uri.Query.TrimStart('?').Split('&').Select(p=>p.Split('=',2)[0]).ToArray() });
                File.WriteAllText(Path.Combine(diagnosticDirectory,"browser-responses.json"),JsonSerializer.Serialize(diagnosticResponses));
            }
            if (e.Response.StatusCode != 200)
            {
                if (diagnosticDirectory is not null && !sessionOnly)
                {
                    using var rejected=await e.Response.GetContentAsync();
                    if(rejected is not null)
                    {
                        var prefix=new byte[512];var n=await rejected.ReadAsync(prefix);
                        var text=Encoding.UTF8.GetString(prefix,0,n);
                        var kind=text.Contains("Uifid Not Found",StringComparison.OrdinalIgnoreCase) ? "browser_identity_missing"
                            : text.Contains("Uifid",StringComparison.OrdinalIgnoreCase) ? "browser_identity_rejected" : "http_rejected";
                        diagnosticResponses.Add(new { endpoint=uri.AbsolutePath, httpStatus=e.Response.StatusCode, kind });
                        File.WriteAllText(Path.Combine(diagnosticDirectory,"browser-responses.json"),JsonSerializer.Serialize(diagnosticResponses));
                    }
                }
                return;
            }

            try

            {

                using var stream = await e.Response.GetContentAsync(); if (finished || stream is null) return;

                using var bytes = new MemoryStream(); var buffer = new byte[16384]; int count;

                while ((count = await stream.ReadAsync(buffer)) > 0)

                {

                    if (finished || bytes.Length + count > 4 * 1024 * 1024) return;

                    bytes.Write(buffer, 0, count);

                }

                if (finished) return;

                var json = Encoding.UTF8.GetString(bytes.ToArray());

                if (loginResponse)

                {

                    var self = JsonNode.Parse(json); var uid = self?["user"]?["uid"]?.ToString();

                    Authenticated = self?["status_code"]?.ToString() == "0" && !string.IsNullOrWhiteSpace(uid) && uid != "0";

                    if (sessionOnly) { loginPromptVisible = null; RecordSessionState(); }

                    if (sessionOnly) status.Text = Authenticated == true ? "Douyin đã xác nhận đăng nhập trên hồ sơ của app. Bạn có thể đóng cửa sổ; phiên được lưu cho lần sau." : "Douyin chưa xác nhận đăng nhập. Hãy đăng nhập/xác minh ngay trên trang này rồi đóng cửa sổ.";

                    if (diagnosticDirectory is not null) File.WriteAllText(Path.Combine(diagnosticDirectory, "session-auth.json"), JsonSerializer.Serialize(new { serverConfirmedLogin = Authenticated, statusCode = self?["status_code"]?.ToString(), accountDataLogged = false }));

                    return;

                }

                if (diagnosticDirectory is not null && !sessionOnly)
                {
                    var summary=JsonNode.Parse(json); var detail=summary?["aweme_detail"];
                    diagnosticResponses.Add(new { endpoint=uri.AbsolutePath, bytes=bytes.Length, apiStatus=summary?["status_code"]?.ToString(), hasDetail=detail is not null, matchesSource=detail?["aweme_id"]?.ToString()==source.Id,
                        videoKeys=(detail?["video"] as JsonObject)?.Select(x=>x.Key).ToArray(), formatCount=detail is not null ? DouyinWebMetadata.Parse(detail,source)?["formats"]?.AsArray().Count : null });
                    File.WriteAllText(Path.Combine(diagnosticDirectory,"browser-responses.json"),JsonSerializer.Serialize(diagnosticResponses));
                }
                if (videoOnly)

                {

                    var info = DouyinBrowserCapture.VideoMetadata(uri, json, source);

                    if (info is not null && videoMetadata is null) AcceptVideoMetadata(info);

                    if (videoMetadata is not null && DouyinBrowserCapture.MergeVideoCovers(uri, json, source, videoMetadata) && DouyinBrowserCapture.HasBothCovers(videoMetadata)) FinishVideo();

                    return;

                }

                if (!capture.Accept(uri, json)) return;

                if (uri.AbsolutePath.EndsWith("/post/", StringComparison.Ordinal)) acceptedPostResponses++;

                if (!navigatedChannel && capture.SecUid is not null)

                { if (loginPromptVisible != true && verificationVisible != true) NavigateChannelOrVideo(); return; }

                var found = capture.Result();

                if (found is null) { status.Text = "Đang chờ dữ liệu kênh và 10 video mới nhất. Nếu trang yêu cầu đăng nhập/xác minh, hãy hoàn tất tại đây; app sẽ tự tiếp tục."; return; }

                pendingResult = found; finished = true; Close();

            }

            catch (JsonException) { /* Challenges/non-JSON are not accepted as channel data. */ }

            catch (NeedsActionException ex) { if (!finished) status.Text = ex.Message + " Bạn có thể Tải lại trang hoặc đóng để dừng."; }

            catch (Exception ex) when (ex is IOException or System.Runtime.InteropServices.COMException or ObjectDisposedException) { }

        }

        private async void Tick(object? sender, EventArgs e)

        {

            if (finished || scrolling) return;

            scrolling = true;

            try { await CheckInteractionAsync(); } finally { scrolling = false; }

            if (finished) return;

            if (loginPromptVisible == true || verificationVisible == true)

            { waitingForUser = true; deadline = DateTimeOffset.UtcNow.AddMinutes(3); lastVideoNavigation = DateTimeOffset.UtcNow; if (coversStartedAt is not null) coversStartedAt = DateTimeOffset.UtcNow; return; }

            if (waitingForUser) { waitingForUser = false; detailRequestedRoute = -1; navigationFinishedAt = DateTimeOffset.UtcNow; deadline = DateTimeOffset.UtcNow.AddSeconds(anonymous ? 35 : 180); status.Text = "Yêu cầu đăng nhập/xác minh đã đóng. Đang đọc tiếp dữ liệu của đúng video/kênh nguồn…"; }

            if (DateTimeOffset.UtcNow >= deadline)

            {

                Fail(anonymous ? (videoOnly ? "Phiên khách chưa trả đủ dữ liệu video sau lượt đọc giới hạn; app sẽ tự thử phiên đã đăng nhập." : "Phiên khách chưa trả đủ dữ liệu kênh; app sẽ tự thử phiên đăng nhập để đọc kênh nguồn.") : "Douyin chưa trả dữ liệu hợp lệ sau lượt đọc giới hạn. Công việc giữ nguyên để tiếp tục; kiểm tra nguồn hoặc mở Douyin trong cài đặt nếu cần."); return;

            }

            if (browser.CoreWebView2 is null) return;

            scrolling = true;

            try

            {

                if (!videoOnly && capture.SecUid is not null && !navigatedChannel) { NavigateChannelOrVideo(); return; }

                if (videoOnly && videoMetadata is not null)

                {

                    coversStartedAt ??= DateTimeOffset.UtcNow;

                    if (DateTimeOffset.UtcNow - coversStartedAt > TimeSpan.FromSeconds(20)) { FinishVideo(); return; }

                    if (!coverRouteOpened)

                    {

                        coverRouteOpened = true;

                        browser.CoreWebView2.Navigate("https://www.douyin.com/jingxuan?modal_id=" + Uri.EscapeDataString(source.Id));

                    }

                    if (!selectRequested && DateTimeOffset.UtcNow - lastVideoNavigation > TimeSpan.FromSeconds(5))

                    {

                        selectRequested = true; lastVideoNavigation = DateTimeOffset.UtcNow;

                        var path = JsonSerializer.Serialize("/aweme/v1/web/aweme/detail/?device_platform=webapp&aid=568863&aweme_id=" + Uri.EscapeDataString(source.Id) + "&channel=channel_pc_web&pc_client_type=1&codec_type=1&support_h265=1");

                        await browser.CoreWebView2.ExecuteScriptAsync("(()=>{const x=new XMLHttpRequest();x.open('GET'," + path + ");x.timeout=10000;x.send();return true})()");

                    }

                    return;

                }

                // A direct video page can include its detail in server-rendered state,

                // without making a detail API request. Accept only the exact source ID.

                if (capture.SecUid is null || videoOnly)

                {

                    var notice = JsonSerializer.Deserialize<string?>(await browser.CoreWebView2.ExecuteScriptAsync("window.__timVideoHaySourceNotice||null"));

                    if (notice is not null && DouyinBrowserCapture.IsUnavailableNotice(notice))

                    {

                        Fail("Douyin thông báo video nguồn không tồn tại (你要观看的视频不存在). Đã dừng, không dùng video đề xuất thay thế và không ghi bảng hoặc đăng YouTube.", terminal: true); return;

                    }

                    if (detailRequestedRoute != videoRoute && DateTimeOffset.UtcNow - lastVideoNavigation > TimeSpan.FromSeconds(5))

                    {

                        detailRequestedRoute = videoRoute;

                        // Request through the page's own browser context. Its session/SDK

                        // supplies browser identity; the app never creates a signature.

                        var detailPath = JsonSerializer.Serialize("/aweme/v1/web/aweme/detail/?device_platform=webapp&aid=6383&aweme_id=" + Uri.EscapeDataString(source.Id)

                            + "&channel=channel_pc_web&pc_client_type=1&version_code=190500&version_name=19.5.0&codec_type=1&support_h265=1&support_dash=1");

                        await browser.CoreWebView2.ExecuteScriptAsync("(()=>{const x=new XMLHttpRequest();x.open('GET'," + detailPath + ");x.timeout=10000;x.send();return true})()");

                    }

                    if (videoRoute < 1 && DateTimeOffset.UtcNow - lastVideoNavigation > TimeSpan.FromSeconds(20))

                    {

                        videoRoute++; status.Text = "Chưa nhận được dữ liệu đúng video nguồn. Đang thử mở video bằng đường dẫn khác của Douyin…";

                        NavigateChannelOrVideo(); return;

                    }

                    var id = JsonSerializer.Serialize(source.Id);

                    var script = "(()=>{const id=" + id + ";const roots=[window._ROUTER_DATA,window.__routerData];try{const s=document.getElementById('RENDER_DATA');if(s)roots.push(JSON.parse(decodeURIComponent(s.textContent)))}catch{}const seen=new Set();let n=0;function find(v){if(!v||typeof v!=='object'||seen.has(v)||++n>20000)return null;seen.add(v);if(String(v.aweme_id||'')===id&&v.video&&v.author)return v;for(const x of Object.values(v)){const r=find(x);if(r)return r}return null}for(const r of roots){const v=find(r);if(v)return JSON.stringify({status_code:0,aweme_detail:v})}return null})()";

                    if(anonymous && diagnosticDirectory is not null && DateTimeOffset.UtcNow-openedAt>TimeSpan.FromSeconds(20) && !File.Exists(Path.Combine(diagnosticDirectory,"server-shape.json")))
                    {
                        var shape=await browser.CoreWebView2.ExecuteScriptAsync("(()=>{const roots=[window._ROUTER_DATA,window.__routerData];const seen=new Set();const candidates=[];let n=0;function find(v){if(!v||typeof v!=='object'||seen.has(v)||++n>20000)return;seen.add(v);if(v.aweme_id||v.awemeId)candidates.push({matches:String(v.aweme_id||v.awemeId)==="+id+",keys:Object.keys(v),videoKeys:Object.keys(v.video||{})});for(const x of Object.values(v))find(x)}for(const r of roots)find(r);return JSON.stringify({rootKeys:roots.map(r=>r?Object.keys(r):null),candidates})})()");
                        await File.WriteAllTextAsync(Path.Combine(diagnosticDirectory,"server-shape.json"),JsonSerializer.Deserialize<string>(shape));
                    }
                    var encoded = await browser.CoreWebView2.ExecuteScriptAsync(script);

                    var detail = JsonSerializer.Deserialize<string?>(encoded);

                    if (detail is not null && detail.Length <= 4 * 1024 * 1024)

                    {

                        var uri = new Uri("https://www.douyin.com/aweme/v1/web/aweme/detail/?aweme_id=" + source.Id);

                        if (videoOnly)

                        {

                            var metadata = DouyinBrowserCapture.VideoMetadata(uri, detail, source);

                            if (metadata is not null) { AcceptVideoMetadata(metadata); return; }

                        }

                        else if (capture.Accept(uri, detail) && !navigatedChannel) { NavigateChannelOrVideo(); return; }

                    }

                }

                if (!previewSaved && ShowInTaskbar && diagnosticDirectory is not null && DateTimeOffset.UtcNow - openedAt > TimeSpan.FromSeconds(25))

                {

                    previewSaved = true;

                    using var preview = File.Create(Path.Combine(diagnosticDirectory, "reader.png"));

                    await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, preview);

                }

                if (!navigatedChannel || videoOnly) return;

                if (scrollAttempts >= 5 || DateTimeOffset.UtcNow - lastScroll < TimeSpan.FromSeconds(10)) return;

                scrollAttempts++; lastScroll = DateTimeOffset.UtcNow;

                // Scroll the actual site's lists so its own client requests further pages with its session signature.

                await browser.CoreWebView2.ExecuteScriptAsync("(()=>{for(const e of document.querySelectorAll('*')){if(e.clientHeight>200&&e.scrollHeight>e.clientHeight+300&&['auto','scroll'].includes(getComputedStyle(e).overflowY))e.scrollTop=e.scrollHeight}window.scrollTo(0,document.documentElement.scrollHeight)})()");

            }

            catch (NeedsActionException ex) { status.Text = ex.Message + " Bạn có thể tải lại trang để thử lại."; }

            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ObjectDisposedException or InvalidOperationException or JsonException) { }

            finally { scrolling = false; }

        }

        private void AcceptVideoMetadata(JsonNode info)

        {

            videoMetadata = info;

            if (!readCovers || DouyinBrowserCapture.HasBothCovers(info)) { FinishVideo(); return; }

            coversStartedAt = DateTimeOffset.UtcNow;

            status.Text = "Đã đọc đúng video. Đang tìm ảnh bìa ngang/dọc trên Douyin Jingxuan bằng cùng phiên đăng nhập…";

        }

        private void FinishVideo() { pendingResult = videoMetadata; finished = true; Close(); }

        private void Fail(string message, bool terminal = false, bool blocked = false, DateTimeOffset? retryAt = null) { if (finished) return; finished = true; pendingError = terminal ? new DouyinTerminalException(message, blocked, retryAt) : new NeedsActionException(message); Close(); }

        private void TrackFrame(CoreWebView2Frame frame, CoreWebView2Frame root)

        {

            if (frames.Count >= 32) return;

            frames.Add((frame, root));

            frame.FrameCreated += (_, e) => TrackFrame(e.Frame, root);

            frame.Destroyed += (_, _) => frames.RemoveAll(f => f.Frame == frame);

        }

        private void PresentInteraction(bool needed)

        {

            if (sessionOnly) return;

            if (!needed)
            {
                // Hide() makes the WebView controller invisible and can suspend the page SDK.
                // Keep it laid out, off-screen while waiting for source data.
                Opacity = 1; ShowInTaskbar = false; ShowActivated = false;
                Left = SystemParameters.VirtualScreenLeft - Width - 100; Top = SystemParameters.VirtualScreenTop;
                return;
            }

            if (IsVisible && ShowInTaskbar) return;

            Opacity = 1; ShowInTaskbar = true; ShowActivated = true;

            var area = SystemParameters.WorkArea;

            Left = area.Left + (area.Width - Width) / 2; Top = area.Top + (area.Height - Height) / 2;

            WindowAttention.Present(this);

            notify?.Invoke(anonymous ? "Douyin cần bạn xác minh trong phiên khách. Hoàn tất trong cửa sổ vừa hiện; app sẽ tự ẩn và chạy tiếp." : "Douyin cần bạn đăng nhập/xác minh. Hoàn tất trong cửa sổ vừa hiện; app sẽ tự ẩn và chạy tiếp.");

        }

        private async void SessionTick(object? sender, EventArgs e) => await CheckInteractionAsync();

        private async Task CheckInteractionAsync()

        {

            if (finished || checkingLogin || browser.CoreWebView2 is null || navigationFinishedAt is null

                || DateTimeOffset.UtcNow - navigationFinishedAt < TimeSpan.FromSeconds(5)) return;

            checkingLogin = true;

            try

            {

                // Inspect nested verification frames before closing an optional login invitation.

                var script = DouyinInteractionScripts.Inspect + "(false)";

                var state = JsonNode.Parse(await browser.CoreWebView2.ExecuteScriptAsync(script));

                loginPromptVisible = state?["login"]?.GetValue<bool>(); verificationVisible = state?["verification"]?.GetValue<bool>();

                if (finished) return;

                if (verificationVisible != true && state?["visibleFrames"] is JsonArray visible)

                {

                    var names = visible.Select(n => n?.ToString() ?? "").ToHashSet(StringComparer.Ordinal);

                    foreach (var frame in frames.ToArray())

                    {

                        if (frame.Frame.IsDestroyed() != 0 || frame.Root.IsDestroyed() != 0 || !names.Contains(frame.Root.Name)) continue;

                        try

                        {

                            // Read a boolean marker only. Do not interact with or solve the challenge.

                            var marker = await frame.Frame.ExecuteScriptAsync("(()=>{const t=document.body?.innerText||'';return t.includes('请完成下列验证后继续')||t.includes('请完成验证后继续')})()");

                            if (JsonSerializer.Deserialize<bool>(marker)) { verificationVisible = true; break; }

                        }

                        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ObjectDisposedException or InvalidOperationException or JsonException) { }

                    }

                }

                if (finished) return;

                if (verificationVisible != true && loginPromptVisible == true && !sessionOnly && loginDismissAttempts < 6)

                    state = JsonNode.Parse(await browser.CoreWebView2.ExecuteScriptAsync(DouyinInteractionScripts.Inspect + "(true)"));

                if (state?["closed"]?.GetValue<bool>() == true && verificationVisible != true)

                {

                    loginDismissAttempts++; loginPromptVisible = false; PresentInteraction(false);
                    if (diagnosticDirectory is not null)
                        await File.WriteAllTextAsync(Path.Combine(diagnosticDirectory, "interaction.json"), JsonSerializer.Serialize(new { optionalLoginDismissed = true, loginDismissAttempts, accountSessionUsed = !anonymous }));

                    status.Text = "Đã tự đóng lời mời đăng nhập; đang đọc tiếp dữ liệu nguồn."; return;

                }

                if (verificationVisible != true && loginPromptVisible != true) PresentInteraction(false);

                if (verificationVisible == true)

                {

                    PresentInteraction(true);

                    Authenticated = null;

                    status.Text = anonymous ? "Phiên khách yêu cầu xác minh (CAPTCHA). Hãy tự hoàn tất ngay tại đây, không đăng nhập tài khoản. App giữ cửa sổ và chờ; không tự tải lại hoặc chuyển sang tài khoản." : "Douyin đang yêu cầu xác minh chống bot (CAPTCHA). Hãy tự hoàn tất câu đố trên trang này; app giữ phiên và chờ xác nhận, không tự tải lại liên tục.";

                    RecordSessionState(); return;

                }

                if (loginPromptVisible == true)

                {

                    if (anonymous)
                    {
                        PresentInteraction(true);
                        status.Text = "Phiên khách đang bị lời mời đăng nhập che. Đóng lời mời bằng dấu × để app đọc tiếp; không đăng nhập tài khoản ở cửa sổ khách. App giữ cửa sổ và chờ.";
                        return;
                    }

                    PresentInteraction(true);

                    Authenticated = false;

                    status.Text = "Douyin yêu cầu đăng nhập. Hãy đăng nhập ngay trên trang này; app chờ bạn hoàn tất để tiếp tục.";

                }

                else if (sessionOnly && Authenticated is null)

                    status.Text = "Đã mở hồ sơ Douyin của app, đang chờ trang xác nhận đăng nhập. Nếu xuất hiện yêu cầu đăng nhập/xác minh, hãy hoàn tất tại đây.";

                RecordSessionState();

            }

            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or ObjectDisposedException or JsonException or InvalidOperationException) { }

            finally { checkingLogin = false; }

        }

    }

}

