using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using System.Windows.Input;
using System.Text.Json;
using System.Windows.Threading;
using System.Threading;
using TimVideoHay.Core;

namespace TimVideoHay.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel model;
    private readonly System.Windows.Forms.NotifyIcon tray;
    private bool exiting;
    private AttentionWindow? attentionWindow;
    private readonly DispatcherTimer notificationPump = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly System.Collections.Generic.Dictionary<string, string> completedNames = new();
    private DateTimeOffset completionDue;
    private bool attentionAnnouncementPending;
    private bool exerciseNotifications;
    private DateTimeOffset lastAttentionPresentation;
    public MainWindow(MainViewModel model)
    {
        InitializeComponent(); Title = AppInfo.WindowTitle; this.model = model; DataContext = model;
        MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height);
        Height = Math.Min(Height, MaxHeight);
        tray = new System.Windows.Forms.NotifyIcon { Icon = System.Drawing.SystemIcons.Application, Text = "Tìm video hay", Visible = false };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Mở Tìm video hay", null, (_, _) => Dispatcher.BeginInvoke(ShowApp));
        menu.Items.Add("Thoát và lưu checkpoint", null, (_, _) => Dispatcher.BeginInvoke(async () => await ExitAsync()));
        tray.ContextMenuStrip = menu; tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowApp);
        Closing += OnClosing;
        model.AttentionRequired += OnAttentionRequired;
        model.FocusRequested += FocusNotificationTarget;
        model.BackgroundNotice += OnBackgroundNotice;
        tray.BalloonTipClicked += (_, _) => Dispatcher.BeginInvoke(() => { ShowApp(); if (model.HasAttentions) PresentAttentions(); });
        notificationPump.Tick += (_, _) => PumpNotifications(); notificationPump.Start();
        model.AttentionJobOpened += RevealAttentionJob;
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MainViewModel.Busy) && !model.Busy && attentionAnnouncementPending) OnAttentionRequired(); };
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if ((uint)msg == App.ActivationMessage) { ShowApp(); handled = true; }
            return IntPtr.Zero;
        });
        Closed += (_, _) => { model.AttentionRequired -= OnAttentionRequired; model.FocusRequested -= FocusNotificationTarget; model.BackgroundNotice -= OnBackgroundNotice; notificationPump.Stop(); model.AttentionJobOpened -= RevealAttentionJob; tray.Dispose(); };
        Loaded += async (_, _) =>
        {
            if (App.SessionDiagnosticOutput is { } report)
            {
                Directory.CreateDirectory(report); model.TabIndex = 1; DouyinSettings.IsExpanded = true;
                await Task.Delay(300);
                var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(OpenDouyinButton);
                ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
                await Task.Delay(45000); model.CancelSessionDiagnostic();
                for (var i = 0; model.Busy && i < 100; i++) await Task.Delay(50);
                await File.WriteAllTextAsync(Path.Combine(report, "main-button.json"), JsonSerializer.Serialize(new { dataRoot = App.DataRoot, actualMainButtonInvoked = true, operationFinished = !model.Busy, googleCalled = false, jobsResumed = false }));
                Application.Current.Shutdown(model.Busy ? 1 : 0); return;
            }
            if (App.SmokeOutput is null) { if (model.HasAttentions) OnAttentionRequired(); return; }
            await Task.Delay(500);
            if (model.DetailsExpanded || HistoryFilters.IsExpanded) throw new InvalidOperationException("Details and filters must start collapsed.");
            Directory.CreateDirectory(Path.GetDirectoryName(App.SmokeOutput)!);
            for (var tab = 0; tab < 2; tab++)
            {
                model.TabIndex = tab;
                if (tab == 1) model.SetTabsForSmoke(true);
                model.SelectedJob = model.Jobs.FirstOrDefault();
                await Task.Delay(200); UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                var path = tab == 0 ? App.SmokeOutput : Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, Path.GetFileNameWithoutExtension(App.SmokeOutput) + "-" + tab + ".png");
                using var file = File.Create(path); encoder.Save(file);
                if (tab == 1) model.SetTabsForSmoke(false);
            }
            var inputChecks = await CheckInputControlsAsync();
            var repeatedNicheChecks = await CheckRepeatedNicheControlsAsync();
            var independentCommandChecks = await model.CheckIndependentCommandsSmokeAsync();
            var independenceChecks = await model.CheckUnifiedHistoryAsync();
            model.TabIndex = 0;
            var historyChecks = await RenderHistorySmokeAsync();
            var deletionChecks = await RenderDeletionSmokeAsync();
            var additionalChecks = await RenderAddSmokeAsync();
            var attentionChecks = await RenderAttentionSmokeAsync();
            var operationNoticeChecks = await RenderOperationNoticesAsync();
            var selectionChecks = await model.CheckSelectionPreferencesAsync();
            var guideChecks = await RenderGuidesSmokeAsync();
            var columnsChecks = await RenderColumnsSmokeAsync();
            var thresholdChecks = await CheckThresholdControlsAsync();
            await CheckSettingsLayoutAsync();
            var verificationChecks = await DouyinBrowserReader.CheckVerificationSmokeAsync(Path.Combine(App.DataRoot, "verification-fixture"));
            if (App.SmokeWizard) await RenderWizardAsync();
            model.FullLink = "https://www.douyin.com/video/7659702794928147739"; model.FullName = "Video thử";
            var allUnavailable = !model.SubmitFullCommand.CanExecute(null);
            model.UploadSelected = false; model.SheetSelected = false; model.DownloadSelected = true;
            var check = new { downloadAvailableWithoutGoogle = model.SubmitFullCommand.CanExecute(null), fullUnavailableWithoutGoogle = allUnavailable, googleConfigured = model.GoogleConfigured, inputChecks, repeatedNicheChecks, independentCommandChecks, deletionChecks, additionalChecks, attentionChecks, operationNoticeChecks, selectionChecks, independenceChecks, historyChecks, guideChecks, columnsChecks, thresholdChecks, verificationChecks,
                layoutChecks = new { detailsAndFiltersStartCollapsed = true, detailHeightFixed = true, detailScrollbarOnlyForOverflow = true, detailWheelAndThumbDragVerified = true, independentFormAndHistory = true, narrowExpandedHistoryReachable = true, detailScrollsWithPage = true, buttonsAboveDetail = true, wholeDetailHasOwnScroll = true, pausedProgressBindingVerified = true, compactDetailVerified = true, fullPathAvailableInTooltip = true, historyToolbarContainsRefreshAndPause = true, checklistFiltersCompact = true, perFilterClearVerified = true, detailCanCollapseWithoutDisappearing = true, stageMovedToDetail = true, allSectionsExpandAndCollapseIndependently = true, compactDialogActionsReachable = true, settingsControlsReachable = true, guideSearchVerified = true },
                diagnostic = "UI only; no Google login, permissions, sheet writes or uploads" };
            await File.WriteAllTextAsync(App.SmokeOutput + ".json", JsonSerializer.Serialize(check));
            await ExitAsync();
        };
    }
    private static System.Collections.Generic.IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var item in Descendants<T>(child)) yield return item;
        }
    }
    private void CheckDetailLayout(bool requireInViewport = false)
    {
        UpdateLayout();
        if (!JobDetailHost.IsVisible || JobDetailHost.ActualHeight < 50 || !Descendants<System.Windows.Controls.ContentControl>(WorkArea).Contains(JobDetailHost))
            throw new InvalidOperationException("Detail must belong to the shared scroll area.");
        if (requireInViewport)
        {
            var rect = JobDetailHost.TransformToAncestor(WorkArea).TransformBounds(new Rect(0,0,JobDetailHost.ActualWidth,JobDetailHost.ActualHeight));
            if (rect.Top < -1 || rect.Bottom > WorkArea.ActualHeight + 1) throw new InvalidOperationException("Detail cannot be reached using the common scrollbar.");
        }
        if (model.DetailsExpanded && model.SelectedJob is not null)
        {
            var scroll = Descendants<System.Windows.Controls.ScrollViewer>(JobDetailHost).Single();
            if (scroll.Height != 170 || Math.Abs(scroll.ActualHeight - 170) > 1 / VisualTreeHelper.GetDpi(scroll).DpiScaleY || scroll.ActualWidth < JobDetailHost.ActualWidth - 64 || scroll.VerticalScrollBarVisibility != System.Windows.Controls.ScrollBarVisibility.Auto)
                throw new InvalidOperationException($"Detail scroll area invalid: height={scroll.Height}, actual={scroll.ActualHeight}, width={scroll.ActualWidth}, host={JobDetailHost.ActualWidth}, visibility={scroll.VerticalScrollBarVisibility}.");
            if (Descendants<System.Windows.Controls.Primitives.ScrollBar>(scroll).Any(b => b.Orientation == System.Windows.Controls.Orientation.Vertical && b.IsVisible) != (scroll.ScrollableHeight > 0))
                throw new InvalidOperationException("Detail vertical scrollbar visibility must match overflowing content.");
        }
        var open = Descendants<System.Windows.Controls.Button>(JobDetailHost).Single(b => Equals(b.Content, "Mở bảng"));
        var detail = Descendants<System.Windows.Controls.Expander>(JobDetailHost).Single();
        if (!open.IsVisible || open.TranslatePoint(new Point(0, open.ActualHeight), JobDetailHost).Y > detail.TranslatePoint(new Point(), JobDetailHost).Y)
            throw new InvalidOperationException("Detail actions must remain above the detail expander.");
        if (model.DetailsExpanded && model.SelectedJob is not null)
        {
            var scroll = Descendants<System.Windows.Controls.ScrollViewer>(JobDetailHost).Single();
            if (!Descendants<System.Windows.Controls.ProgressBar>(scroll).Any()) throw new InvalidOperationException("Whole detail body must have its own scroll area.");
        }
    }
    private static void InvokeButton(System.Windows.Controls.Button button)
    {
        var peer = new System.Windows.Automation.Peers.ButtonAutomationPeer(button);
        ((System.Windows.Automation.Provider.IInvokeProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
    }
    private void DetailMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
    {
        if (sender is not System.Windows.Controls.ScrollViewer detail || e.Delta == 0) return;
        var target = detail.ScrollableHeight > 0 && (e.Delta < 0 ? detail.VerticalOffset < detail.ScrollableHeight : detail.VerticalOffset > 0) ? detail : WorkArea;
        var lines = SystemParameters.WheelScrollLines;
        if (lines == 0) return;
        var distance = lines < 0 ? target.ViewportHeight : 16d * lines;
        target.ScrollToVerticalOffset(Math.Clamp(target.VerticalOffset - e.Delta / 120d * distance, 0, target.ScrollableHeight));
        e.Handled = true;
    }
    private async Task WaitForCommandAsync()
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        for (var i = 0; model.Busy && i < 200; i++) await Task.Delay(25);
        if (model.Busy) throw new InvalidOperationException("UI command did not finish.");
    }
    private void CaptureMain(string name)
    {
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, name + ".png")); encoder.Save(file);
    }
    private async Task<object> RenderHistorySmokeAsync()
    {
        if (FullHistory.Columns.Any(c => Convert.ToString(c.Header) is "Loại" or "Bước xử lý")) throw new InvalidOperationException("Obsolete history column remains.");
        return await model.CheckHistorySmokeAsync(async () =>
        {
            UploadOptions.IsExpanded = false; HistoryFilters.IsExpanded = false; NewJobPanel.IsExpanded = false;
            model.DetailsExpanded = true; WorkArea.ScrollToEnd(); FullHistory.ScrollIntoView(model.Jobs[0]);
            await Task.Delay(200); UpdateLayout(); CheckDetailLayout();
            var realized = model.Jobs.Count(j => FullHistory.ItemContainerGenerator.ContainerFromItem(j) is not null);
            if (!FullHistory.EnableRowVirtualization || realized >= model.Jobs.Count) throw new InvalidOperationException("Many-job grid is not virtualized.");
            CaptureMain("history-120-queued");
            var width = Width; var height = Height; Width = MinWidth; Height = MinHeight;
            await Task.Delay(150); CheckDetailLayout(); CaptureMain("history-narrow");
            NewJobPanel.IsExpanded = true; WorkArea.ScrollToHome();
            await Task.Delay(150); CheckDetailLayout();
            if (!model.DetailsExpanded || !NewJobPanel.IsVisible) throw new InvalidOperationException("Opening new job changed detail visibility/expansion.");
            CaptureMain("history-narrow-expanded");
            var beforeScroll = JobDetailHost.TransformToAncestor(WorkArea).Transform(new Point()).Y;
            WorkArea.ScrollToEnd(); await Task.Delay(100); UpdateLayout(); CheckDetailLayout(requireInViewport: true);
            var afterScroll = JobDetailHost.TransformToAncestor(WorkArea).Transform(new Point()).Y;
            if (beforeScroll - afterScroll < 40) throw new InvalidOperationException("Detail is still pinned while scrolling the page.");
            var visibleGrid = FullHistory.TransformToAncestor(WorkArea).TransformBounds(new Rect(0,0,FullHistory.ActualWidth,FullHistory.ActualHeight));
            if (!visibleGrid.IntersectsWith(new Rect(0,0,WorkArea.ActualWidth,WorkArea.ActualHeight))) throw new InvalidOperationException("History cannot be reached in the common scroll area.");
            CaptureMain("history-narrow-details");
            model.DetailsExpanded = true; await Task.Delay(100); CheckDetailLayout();
            var selected = model.SelectedJob!; var message = selected.Message;
            selected.Message = string.Join(" ", Enumerable.Repeat("Thông tin tiến trình dài để kiểm tra vùng cuộn: giữ nguyên kết quả và xem từng bước thực hiện.", 28));
            model.SelectedJob = null; model.SelectedJob = selected;
            await Task.Delay(100); CheckDetailLayout();
            var scroll = Descendants<System.Windows.Controls.ScrollViewer>(JobDetailHost).Single();
            if (Math.Abs(scroll.ActualHeight - 170) > 1 / VisualTreeHelper.GetDpi(scroll).DpiScaleY || scroll.ScrollableHeight <= 0) throw new InvalidOperationException("Long detail is not readable/scrollable.");
            if (App.SmokeInspect)
            {
                WorkArea.ScrollToEnd(); scroll.ScrollToHome(); await Task.Delay(150);
                var inspectRoot = Path.GetDirectoryName(App.SmokeOutput)!;
                await File.WriteAllTextAsync(Path.Combine(inspectRoot,"inspect-ready.txt"),"Ready for physical wheel and thumb drag; fixture only.");
                var inspectDeadline=DateTimeOffset.UtcNow.AddMinutes(5);
                while(!File.Exists(Path.Combine(inspectRoot,"inspect-continue.txt")) && DateTimeOffset.UtcNow<inspectDeadline)
                {
                    await File.WriteAllTextAsync(Path.Combine(inspectRoot,"inspect-offsets.json"),JsonSerializer.Serialize(new { detail=scroll.VerticalOffset, outer=WorkArea.VerticalOffset, max=scroll.ScrollableHeight }));
                    await Task.Delay(300);
                }
                scroll.ScrollToHome(); await Task.Delay(100);
            }
            var parentOffset = WorkArea.VerticalOffset;
            var messageText = Descendants<System.Windows.Controls.TextBlock>(scroll).Single(t => t.Text == selected.Message);
            void Wheel(int delta) => messageText.RaiseEvent(new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, delta) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseWheelEvent });
            Wheel(-120); await Task.Delay(80);
            if (scroll.VerticalOffset <= 0 || WorkArea.VerticalOffset != parentOffset) throw new InvalidOperationException("Wheel did not scroll detail independently.");
            scroll.ScrollToHome(); await Task.Delay(50);
            var detailBar = Descendants<System.Windows.Controls.Primitives.ScrollBar>(scroll).Single(b => b.Orientation == System.Windows.Controls.Orientation.Vertical);
            var thumb = Descendants<System.Windows.Controls.Primitives.Thumb>(detailBar).Single();
            thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
            thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(0,20) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
            thumb.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,20,false) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragCompletedEvent });
            await Task.Delay(80);
            if (scroll.VerticalOffset <= 0 || WorkArea.VerticalOffset != parentOffset) throw new InvalidOperationException("Dragging the detail thumb did not scroll its content.");
            scroll.ScrollToEnd(); WorkArea.ScrollToVerticalOffset(Math.Max(0,WorkArea.ScrollableHeight-40)); await Task.Delay(80); parentOffset = WorkArea.VerticalOffset;
            Wheel(-120); await Task.Delay(80);
            if (WorkArea.VerticalOffset <= parentOffset) throw new InvalidOperationException("Wheel at detail bottom must continue scrolling the page.");
            scroll.ScrollToEnd(); await Task.Delay(50); if (scroll.VerticalOffset <= 0) throw new InvalidOperationException("Detail cannot scroll to end.");
            scroll.ScrollToHome(); WorkArea.ScrollToEnd(); await Task.Delay(100); CheckDetailLayout(requireInViewport: true); CaptureMain("history-narrow-detail-open");
            selected.Message = "Đã tải video và ảnh bìa. Kênh nguồn đã được cập nhật.";
            var media = selected.Media;
            selected.Media = new MediaFile(Path.Combine(App.DataRoot, new string('a', 100), "Video thử.mp4"), 300, 100, "fixture", "video/mp4", 1920, 1080, "h264", CoverStatus: "Đã tải đủ 2 ảnh bìa", QualityStatus: "Chất lượng cao nhất nguồn cung cấp");
            model.SelectedJob = null; model.SelectedJob = selected;
            await Task.Delay(100); WorkArea.ScrollToEnd(); await Task.Delay(50); CheckDetailLayout(requireInViewport: true);
            CaptureMain("normal-detail-layout");
            await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "normal-detail-layout.json"), JsonSerializer.Serialize(new { host=JobDetailHost.ActualHeight, scroll=scroll.ActualHeight, scrollable=scroll.ScrollableHeight, viewport=scroll.ViewportHeight }));
            if (JobDetailHost.ActualHeight > 280) throw new InvalidOperationException("Normal detail takes too much space: " + JobDetailHost.ActualHeight);
            var pathText = Descendants<System.Windows.Controls.TextBlock>(JobDetailHost).Single(t => t.Text == selected.Media.Path);
            if (!Equals(pathText.ToolTip, selected.Media.Path) || pathText.TextTrimming != TextTrimming.CharacterEllipsis || pathText.TextWrapping != TextWrapping.NoWrap) throw new InvalidOperationException("File path not compact or fully available.");
            var savedState = selected.State; var savedProgress = selected.Progress; var savedAction = selected.ProgressAction;
            var savedBytes = selected.DownloadedBytes; var savedTotal = selected.DownloadTotalBytes;
            foreach (var percent in new[] { 37.5, 0.4, 100.0 })
            {
                selected.State = percent == 100 ? JobState.Complete : JobState.Paused;
                selected.Progress = 0; selected.ProgressAction = JobActions.Download; selected.DownloadTotalBytes = 1000; selected.DownloadedBytes = (long)(10 * percent);
                model.SelectedJob = null; model.SelectedJob = selected;
                await Task.Delay(80); UpdateLayout();
                var bar = Descendants<System.Windows.Controls.ProgressBar>(JobDetailHost).Single();
                if (Math.Abs(bar.Value - percent) > 0.001) throw new InvalidOperationException("Saved byte progress is not bound to the paused/completed bar.");
                var percentText = Descendants<System.Windows.Controls.TextBlock>(JobDetailHost).Single(t => t.GetBindingExpression(System.Windows.Controls.TextBlock.TextProperty)?.ParentBinding.Path?.Path == "SelectedJob.DisplayProgress");
                var expected = percent.ToString("0.#", percentText.Language.GetEquivalentCulture()) + "%";
                if (percentText.Text != expected) throw new InvalidOperationException("Saved percent text is inaccurate: " + percentText.Text + " instead of " + expected);
            }
            selected.State = JobState.Paused; selected.DownloadedBytes = 375; selected.Progress = 37.5;
            model.SelectedJob = null; model.SelectedJob = selected;
            await Task.Delay(80); CaptureMain("compact-detail");
            if (Math.Abs(scroll.ActualHeight - 170) > 1 / VisualTreeHelper.GetDpi(scroll).DpiScaleY) throw new InvalidOperationException("Short detail changed the fixed height.");
            if (scroll.ScrollableHeight > 0 || Descendants<System.Windows.Controls.Primitives.ScrollBar>(scroll).Any(b => b.Orientation == System.Windows.Controls.Orientation.Vertical && b.IsVisible)) throw new InvalidOperationException("Short detail must hide its scrollbar when content fits.");
            WorkArea.ScrollToVerticalOffset(Math.Max(0,WorkArea.ScrollableHeight-40)); await Task.Delay(80); parentOffset = WorkArea.VerticalOffset;
            Wheel(-120); await Task.Delay(80);
            if (WorkArea.VerticalOffset <= parentOffset) throw new InvalidOperationException("Wheel over short detail must scroll the page.");
            selected.State = savedState; selected.Progress = savedProgress; selected.ProgressAction = savedAction;
            selected.DownloadedBytes = savedBytes; selected.DownloadTotalBytes = savedTotal;
            selected.Message = message; selected.Media = media; model.SelectedJob = null; model.SelectedJob = selected;
            var expander = Descendants<System.Windows.Controls.Expander>(JobDetailHost).Single();
            expander.IsExpanded = false; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind); CheckDetailLayout();
            if (model.DetailsExpanded || !JobDetailHost.IsVisible) throw new InvalidOperationException("Detail collapse hid section.");
            expander.IsExpanded = true; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind); CheckDetailLayout();
            Width = width; Height = height; await Task.Delay(100);
        }, async () =>
        {
            HistoryFilters.IsExpanded = true; UpdateLayout();
            StateChecklist.OpenForSmoke(true); await Task.Delay(50);
            var state = Descendants<System.Windows.Controls.CheckBox>(StateChecklist.ChoicesForSmoke).Single(b => Equals(b.Content, "Hàng chờ"));
            state.IsChecked = true; StateChecklist.OpenForSmoke(false);
            ActionChecklist.OpenForSmoke(true); await Task.Delay(50);
            var action = Descendants<System.Windows.Controls.CheckBox>(ActionChecklist.ChoicesForSmoke).Single(b => Equals(b.Content, "Tải video"));
            action.IsChecked = true; ActionChecklist.OpenForSmoke(false); HistorySearchBox.Text = "080";
            if (!ReferenceEquals(StateChecklist.ClearForSmoke.Command, model.ClearStateFilterCommand) || !ReferenceEquals(ActionChecklist.ClearForSmoke.Command, model.ClearActionFilterCommand)) throw new InvalidOperationException("Per-filter clear button bindings failed.");
            InvokeButton(StateChecklist.ClearForSmoke); await WaitForCommandAsync();
            if (model.StateChoices.Any(c => c.IsSelected) || !model.ActionChoices.Single(c => c.Value == JobActions.Download).IsSelected || model.HistorySearch != "080") throw new InvalidOperationException("Clearing states changed another filter.");
            state.IsChecked = true; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            InvokeButton(ActionChecklist.ClearForSmoke); await WaitForCommandAsync();
            if (model.ActionChoices.Any(c => c.IsSelected) || !model.StateChoices.Single(c => c.Value == HistoryState.Queued).IsSelected || model.HistorySearch != "080") throw new InvalidOperationException("Clearing actions changed another filter.");
            action.IsChecked = true; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            InvokeButton(ClearHistorySearchButton); await WaitForCommandAsync();
            if (model.HistorySearch != "" || !model.StateChoices.Single(c => c.Value == HistoryState.Queued).IsSelected || !model.ActionChoices.Single(c => c.Value == JobActions.Download).IsSelected) throw new InvalidOperationException("Clearing search changed another filter.");
            HistorySearchBox.Text = "080";
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind); CheckDetailLayout();
            if (!NewJobPanel.IsExpanded || !model.DetailsExpanded || !JobDetailHost.IsVisible) throw new InvalidOperationException("Opening filters changed another section.");
            WorkArea.ScrollToVerticalOffset(NewJobPanel.ActualHeight + 10); await Task.Delay(100); CaptureMain("checklist-filters");
            StateChecklist.OpenForSmoke(true); await Task.Delay(100);
            if (StateChecklist.ChoicesForSmoke.Items.Count != model.StateChoices.Length) throw new InvalidOperationException("Checklist did not show choices.");
            StateChecklist.OpenForSmoke(false);
            var width = Width; var height = Height; Width = MinWidth; Height = MinHeight;
            await Task.Delay(100); CheckDetailLayout(); CaptureMain("all-sections-open-narrow");
            NewJobPanel.IsExpanded = false; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            if (!HistoryFilters.IsExpanded || !model.DetailsExpanded) throw new InvalidOperationException("Manual form collapse changed other sections.");
            NewJobPanel.IsExpanded = true; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
            if (!HistoryFilters.IsExpanded || !model.DetailsExpanded) throw new InvalidOperationException("Manual form expansion changed other sections.");
            Width = width; Height = height;
        }, async () =>
        {
            if (!ReferenceEquals(RefreshHistoryButton.Command, model.RefreshHistoryCommand)) throw new InvalidOperationException("Refresh button binding failed.");
            if (!HistoryToolbar.Children.Contains(RefreshHistoryButton) || !HistoryToolbar.Children.Contains(PauseAllHistoryButton) || !ReferenceEquals(PauseAllHistoryButton.Command, model.StopCommand)) throw new InvalidOperationException("History toolbar placement/binding failed.");
            InvokeButton(RefreshHistoryButton); await WaitForCommandAsync();
            await Task.Delay(80); UpdateLayout();
            if (Math.Abs(Descendants<System.Windows.Controls.ProgressBar>(JobDetailHost).Single().Value - 43.8) > 0.001)
                throw new InvalidOperationException("Refreshed paused progress did not reach the actual WPF bar.");
        });
    }
    private async Task<object> RenderColumnsSmokeAsync()
    {
        for (var channel = 0; channel < 3; channel++)
        {
            var roles = channel == 2 ? 1 : channel == 0 ? 4 : 5;
            var window = new SheetColumnsWindow(channel == 2 ? "Kênh block — tiêu đề đã đổi" : channel == 0 ? "Video — tiêu đề đã đổi" : "Kênh nguồn — tiêu đề đã đổi", channel == 1, CancellationToken.None,
                (_, _) => Task.FromResult(new SheetColumnSelection(0, Enumerable.Range(0, 6).Select(i => new SheetColumnOption(i, ((char)('A' + i)).ToString(), "Tên cột riêng " + (i + 1))).ToArray(), null)), blocked: channel == 2) { Owner = this };
            window.Show(); await Task.Delay(150);
            if (window.CanSave) throw new InvalidOperationException("Empty column choices allowed.");
            for (var i = 0; i < roles; i++) window.SelectForSmoke(i, 0);
            if (roles > 1 && window.CanSave) throw new InvalidOperationException("Duplicate columns allowed.");
            for (var i = 0; i < roles; i++) window.SelectForSmoke(i, 5 - i);
            if (!window.CanSave || !window.Mapping.Columns.SequenceEqual(Enumerable.Range(0, roles).Select(i => 5 - i))) throw new InvalidOperationException("Physical column selection failed.");
            window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "columns-" + channel + ".png"))) encoder.Save(file);
            await CheckCompactDialogAsync(window, "columns-" + channel);
            window.Close();
        }
        return new { bothTabs = true, blockedTab = true, missingOrDuplicateRejected = true, physicalOrderPersisted = true, diagnostic = "Actual column dialogs with mock headers; no Google writes" };
    }
    private async Task<object> CheckThresholdControlsAsync()
    {
        model.UploadSelected = false; model.SheetSelected = false; model.DownloadSelected = true;
        model.FullLink = "https://www.douyin.com/video/7659702794928147739"; model.FullName = "Video thử";
        model.TabIndex = 1; UpdateLayout(); ShortMinutesBox.BringIntoView();
        ShortMinutesBox.Text = "2,5"; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        await model.CheckThresholdSavedAsync(150000);
        if (!model.SubmitFullCommand.CanExecute(null)) throw new InvalidOperationException("Valid threshold disabled new jobs.");
        ShortMinutesBox.Text = "0"; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        if (model.SubmitFullCommand.CanExecute(null)) throw new InvalidOperationException("Invalid threshold accepted.");
        ShortMinutesBox.Text = "5"; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind); await model.CheckThresholdSavedAsync(300000);
        await Task.Delay(150); UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "threshold.png"))) encoder.Save(file);
        model.UploadSelected = true; model.SheetSelected = true; model.TabIndex = 0;
        return new { decimalCommaSupported = true, savedAndReadBack = true, zeroRejected = true, defaultMinutes = 5 };
    }
    private async Task<object> RenderGuidesSmokeAsync()
    {
        for (var guide = 0; guide < 2; guide++)
        {
            var title = guide == 0 ? "Hướng dẫn sử dụng — Tìm video hay" : "Hướng dẫn phiên Douyin";
            (guide == 0 ? model.UserGuideCommand : model.DouyinHelpCommand).Execute(null);
            for (var attempt = 0; model.Busy && attempt < 100; attempt++) await Task.Delay(30);
            var window = Application.Current.Windows.OfType<Window>().Single(x => x.Title == title);
            var content = (System.Windows.Controls.DockPanel)window.Content;
            var text = content.Children.OfType<System.Windows.Controls.TextBox>().Single().Text;
            if (text != File.ReadAllText(Path.Combine(AppContext.BaseDirectory, guide == 0 ? "README.md" : "DOUYIN-SESSION.md"))) throw new InvalidOperationException("Packaged guide did not load.");
            var searchBar = content.Children.OfType<System.Windows.Controls.DockPanel>().First();
            searchBar.Children.OfType<System.Windows.Controls.TextBox>().Single().Text = guide == 0 ? "Douyin" : "phiên";
            var findButton = searchBar.Children.OfType<System.Windows.Controls.Button>().Single();
            var findProvider = new System.Windows.Automation.Peers.ButtonAutomationPeer(findButton).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke) as System.Windows.Automation.Provider.IInvokeProvider;
            if (findProvider is null) throw new InvalidOperationException("Guide search button has no automation provider.");
            findProvider.Invoke(); await Task.Delay(80);
            if (content.Children.OfType<System.Windows.Controls.TextBox>().Single().SelectionLength == 0) throw new InvalidOperationException("Guide text search did not select a match.");
            await Task.Delay(150); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "guide-" + guide + ".png"))) encoder.Save(file);
            window.Close();
        }
        model.TabIndex = 0; UploadOptions.IsExpanded = true; FullPage.ScrollToHome();
        await Task.Delay(150); UpdateLayout();
        var options = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); options.Render(this);
        var optionsEncoder = new PngBitmapEncoder(); optionsEncoder.Frames.Add(BitmapFrame.Create(options));
        using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "upload-options.png"))) optionsEncoder.Save(file);
        return new { guidesLoadedFromPackage = 2, diagnostic = "Actual help windows; no browser launch, extension installation or account cookies" };
    }
    private async Task CheckSettingsLayoutAsync()
    {
        model.TabIndex = 1; GoogleAdvancedSettings.IsExpanded = true; DouyinSettings.IsExpanded = true;
        var width = Width; var height = Height;
        for (var compact = 0; compact < 2; compact++)
        {
            if (compact == 1) { Width = MinWidth; Height = MinHeight; }
            await Task.Delay(80); UpdateLayout(); SettingsPage.ScrollToEnd(); await Task.Delay(80); UpdateLayout();
            var point = OpenDouyinButton.TranslatePoint(new Point(0, OpenDouyinButton.ActualHeight), this);
            if (!OpenDouyinButton.IsVisible || point.Y > ActualHeight - 50 || point.Y < 100)
                throw new InvalidOperationException("Douyin settings button is clipped.");
            var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, compact == 0 ? "settings-advanced.png" : "settings-narrow.png"))) encoder.Save(file);
        }
        Width = width; Height = height; model.TabIndex = 0; await Task.Delay(80); UpdateLayout();
    }
    private async Task CheckDeleteRefreshAsync()
    {
        var store = new TimVideoHay.Infrastructure.LocalStore(Path.Combine(App.DataRoot, "delete-refresh-fixture"));
        var job = new VideoJob { Name = "Chọn xóa giữ nguyên", SheetVerified = true, SheetRow = 2,
            Source = new("7659702794928147739", "https://www.douyin.com/video/7659702794928147739"),
            Sheet = new("mock-refresh-sheet", 7, "Bảng thử", "Video", "mock-key", Columns: new(0, [0,1,2,3]), SourceChannelColumns: new(0, [0,1,2,3,4])) };
        await store.SaveAsync(job);
        var first = (await store.ListAsync()).Single();
        if (ReferenceEquals(first.Sheet!.Columns!.Columns, job.Sheet!.Columns!.Columns)) throw new InvalidOperationException("Fixture did not reload a distinct column array.");
        var checkEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken checkToken = default; var checkCalls = 0;
        var dialog = new DeleteJobWindow(first) { Owner = this, RefreshJob = async () => (await store.ListAsync()).Single(), CheckResults = async token => {
            checkCalls++; checkToken = token; checkEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return first;
        } };
        dialog.Show();
        try
        {
            var sheet = (System.Windows.Controls.CheckBox)dialog.FindName("DeleteSheetBox");
            var history = (System.Windows.Controls.CheckBox)dialog.FindName("RemoveHistoryBox");
            sheet.IsChecked = true; history.IsChecked = true; await Task.Delay(1500);
            if (!dialog.DeleteSheet || !dialog.RemoveHistory) throw new InvalidOperationException("Mapped Sheets selection was cleared by SQLite snapshot refresh.");
            job.SheetRow = 22; await store.SaveAsync(job); await Task.Delay(900);
            if (!dialog.DeleteSheet || !dialog.RemoveHistory) throw new InvalidOperationException("Row relocation cleared source-based deletion selection.");
            job.Sheet = job.Sheet! with { Columns = new(0, [1,0,2,3]) }; await store.SaveAsync(job); await Task.Delay(900);
            if (dialog.DeleteSheet || dialog.RemoveHistory) throw new InvalidOperationException("Changed column target retained stale destructive selection.");
            sheet.IsChecked = true;
            job.Sheet = job.Sheet with { SpreadsheetId = "another-file" }; await store.SaveAsync(job); await Task.Delay(900);
            if (dialog.DeleteSheet) throw new InvalidOperationException("Changed spreadsheet retained stale selection.");
            if (checkCalls != 0) throw new InvalidOperationException("Opening deletion unexpectedly waited for a remote check.");
            sheet.IsChecked = true;
            if (!((System.Windows.Controls.Button)dialog.FindName("ConfirmButton")).IsEnabled) throw new InvalidOperationException("Fixture did not enable deletion before checking.");
            var checkButton = (System.Windows.Controls.Button)dialog.FindName("CheckResultsButton");
            if (!checkButton.IsVisible) throw new InvalidOperationException("Explicit Google check was not visible.");
            InvokeButton(checkButton); await checkEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (((System.Windows.Controls.Button)dialog.FindName("ConfirmButton")).IsEnabled) throw new InvalidOperationException("Deletion remained confirmable while its snapshot was being checked.");
            await Task.Delay(80); dialog.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "delete-check-google.png"))) encoder.Save(file);
            dialog.Close(); await dialog.WaitForChecksAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (!checkToken.IsCancellationRequested || checkCalls != 1) throw new InvalidOperationException("Closing deletion did not cancel and finish its explicit check.");
        }
        finally { if (dialog.IsVisible) dialog.Close(); }
        job.SheetDeleted = true; job.State = JobState.Complete; job.Actions = JobActions.WriteSheet; job.PendingActions = JobActions.None; job.Niche = "Vũ trụ";
        await store.SaveAsync(job); var reads = 0;
        var supplement = model.CreateAddSmoke((await store.ListAsync()).Single(), (_, _) => { reads++; return Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(["Vũ trụ"]); });
        supplement.Owner = this; supplement.RefreshJob = async () => (await store.ListAsync()).Single(); supplement.Show();
        try
        {
            supplement.SelectForSmoke(JobActions.WriteSheet); await Task.Delay(1500);
            if (reads != 1 || !supplement.CanConfirm) throw new InvalidOperationException("Mapped snapshot refresh repeated niche reads or reset the selection.");
        }
        finally { supplement.Close(); }
    }
    private async Task<object> RenderDeletionSmokeAsync()
    {
        await CheckDeleteRefreshAsync();
        for (var step = 0; step < 5; step++)
        {
            var job = new VideoJob { Name = "Video thử giao diện", Mode = step == 4 ? JobMode.DownloadOnly : JobMode.Full,
                DownloadDirectory = step == 0 ? null : @"F:\Video\Video thử giao diện",
                SheetVerified = step is 1 or 2, SheetWriteStarted = step is 1 or 2,
                SourceChannelSheet = step is 1 or 2 ? new("smoke-sheet", 8, "Bảng thử", "Kênh nguồn", "mock-sheets") : null,
                YouTubeId = step == 2 ? "mock-video-id" : null, MediaSendStarted = step is 2 or 3 };
            var dialog = new DeleteJobWindow(job) { Owner = this }; dialog.Show();
            if (dialog.HasSheetChoice != (step is 1 or 2) || dialog.HasVideoChoice != (step == 2) || dialog.HasUnknownUpload != (step == 3) || dialog.HasFolderChoice != (step != 0) || dialog.DeleteSheet || dialog.DeleteYouTube || dialog.DeleteFolder)
                throw new InvalidOperationException("Deletion options smoke failed.");
            await Task.Delay(150); dialog.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "delete-options-" + step + ".png"))) encoder.Save(file);
            await CheckCompactDialogAsync(dialog, "delete-options-" + step);
            var boxes = new[] { "DeleteFolderBox", "DeleteSheetBox", "DeleteVideoBox" }.Select(name => (System.Windows.Controls.CheckBox)dialog.FindName(name)).Where(box => box.Visibility == Visibility.Visible).ToArray();
            foreach (var box in boxes) box.IsChecked = true;
            var history = (System.Windows.Controls.CheckBox)dialog.FindName("RemoveHistoryBox");
            if (history.Visibility != (step == 3 ? Visibility.Collapsed : Visibility.Visible)) throw new InvalidOperationException("Delete history eligibility failed.");
            if (step != 3)
            {
                history.IsChecked = true; if (!dialog.RemoveHistory) throw new InvalidOperationException("History selection failed.");
                if (boxes.Length > 0) { boxes[0].IsChecked = false; if (dialog.RemoveHistory || history.Visibility != Visibility.Collapsed) throw new InvalidOperationException("History did not clear after result deselection."); }
            }
            dialog.Close();
        }
        var selected = new VideoJob { Name = "Video thử giao diện", Mode = JobMode.Full, State = JobState.Paused, Stage = "Đã tạm dừng",
            Message = "Công việc minh họa giao diện; chưa gọi Google.", Progress = 45 };
        model.Jobs.Add(selected); model.SelectedJob = selected;
        await Task.Delay(150); UpdateLayout(); WorkArea.ScrollToEnd(); CheckDetailLayout();
        await Task.Delay(150); UpdateLayout();
        var selectedBitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); selectedBitmap.Render(this);
        var selectedEncoder = new PngBitmapEncoder(); selectedEncoder.Frames.Add(BitmapFrame.Create(selectedBitmap));
        using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "job-actions.png"))) selectedEncoder.Save(file);
        model.Jobs.Remove(selected); model.SelectedJob = null;
        return new { explicitGoogleCheckDoesNotDelayOpening = true, confirmationDisabledWhileChecking = true, closeCancelsAndFinishesCheck = true, mappedCheckboxSurvivesSqliteRefresh = true, mappedNicheReadsNotRepeated = true, movedRowKeepsSelection = true, changedTargetClearsSelection = true, progressVariants = 5, dynamicHistorySelection = true, externalDeletionUncheckedByDefault = true, localFolderDeletionUncheckedByDefault = true, unknownUploadNotGuessed = true,
            diagnostic = "Mock job states; actual WPF deletion dialog, no real deletions" };
    }
    private async Task<object> RenderAddSmokeAsync()
    {
        var reads = 0;
        var job = new VideoJob { Name = "Bổ sung công việc thử", Actions = JobActions.WriteSheet, PendingActions = JobActions.None, State = JobState.Complete, SheetVerified = true,
            Sheet = new("mock-sheet", 7, "Bảng thử", "Video", "mock-sheet-key"), Niche = "Vũ trụ" };
        var dialog = model.CreateAddSmoke(job, (_, _) => { reads++; return Task.FromResult<System.Collections.Generic.IReadOnlyList<string>>(["Vũ trụ", "Động vật"]); });
        dialog.Owner = this; dialog.Show();
        if (dialog.VisibleActions != (JobActions.Download | JobActions.UploadYouTube) || reads != 0) throw new InvalidOperationException("Dynamic add choices failed.");
        dialog.SelectForSmoke(JobActions.UploadYouTube);
        if (!dialog.Actions.HasFlag(JobActions.Download) || !dialog.DownloadLocked || reads != 0) throw new InvalidOperationException("Add upload dependency failed.");
        await Task.Delay(150); dialog.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "add-options.png"))) encoder.Save(file);
        await CheckCompactDialogAsync(dialog, "add-options");
        job.SheetDeleted = true; dialog.ApplyFreshSnapshot(job); if (!dialog.VisibleActions.HasFlag(JobActions.WriteSheet)) throw new InvalidOperationException("Add choices did not update after deletion.");
        dialog.SelectForSmoke(JobActions.WriteSheet); await Task.Delay(100); if (reads != 1) throw new InvalidOperationException("Niches not conditional.");
        dialog.Close();
        var local = Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "cover-fixture"); Directory.CreateDirectory(local);
        var video = Path.Combine(local, "test.mp4"); await File.WriteAllTextAsync(video, "UI fixture");
        job.Actions = JobActions.All; job.SheetDeleted = false; job.YouTubeVerified = true; job.PendingActions = JobActions.None;
        job.DownloadDirectory = local; job.Media = new(video, 10, 10, "fixture", "video/mp4");
        dialog = model.CreateAddSmoke(job, (_, _) => throw new InvalidOperationException("Cover-only must not read niches")); dialog.Owner = this; dialog.Show();
        if (dialog.VisibleActions != JobActions.None || !dialog.CoversAvailable) throw new InvalidOperationException("Cover-only supplemental dialog unavailable.");
        dialog.SelectCoversForSmoke(); if (!dialog.CanConfirm || !dialog.RefreshCovers || dialog.Actions != JobActions.None) throw new InvalidOperationException("Cover-only confirmation failed.");
        await Task.Delay(150); dialog.UpdateLayout();
        bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog);
        encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "cover-options.png"))) encoder.Save(file);
        await CheckCompactDialogAsync(dialog, "cover-options");
        job.YouTubeId = "removed-fixture"; job.YouTubeDeleted = true; dialog.ApplyFreshSnapshot(job);
        if (!dialog.VisibleActions.HasFlag(JobActions.UploadYouTube)) throw new InvalidOperationException("Deleted YouTube result did not enable re-upload.");
        var deletion = new DeleteJobWindow(job) { Owner = this }; deletion.Show();
        if (deletion.HasVideoChoice) throw new InvalidOperationException("Deleted YouTube result still has deletion/confirmation choice.");
        deletion.Close();
        dialog.Close(); return new { dynamicChoices = true, uploadRequiresDownload = true, conditionalNicheReads = true, coverOnlyAvailable = true, deletedYouTubeEnablesUploadWithoutConfirmation = true, diagnostic = "Actual WPF; mock niche reader, no Google" };
    }
    private async Task<object> CheckInputControlsAsync()
    {
        model.TabIndex = 0;
        model.DetailsExpanded = true;
        FullNameBox.Text = new string('a', 20) + " " + new string('b', 20);
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        if (Rules.NameLength(FullNameBox.Text) != 30 || model.FullName != FullNameBox.Text) throw new InvalidOperationException("Name input limit/binding smoke failed.");
        FullNameBox.Text = string.Concat(Enumerable.Repeat("a\u0301", 29)) + "👩‍🔬thừa";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        if (Rules.NameLength(FullNameBox.Text) != 30 || !FullNameBox.Text.EndsWith("👩‍🔬")) throw new InvalidOperationException("Unicode limit failed.");
        // Mock options prove the actual WPF selector binding, not Google acceptance.
        model.Niches.Clear(); model.Niches.Add("Vũ trụ"); model.Niches.Add("Động vật");
        FullNicheBox.SelectedItem = "Động vật";
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        if (FullNicheBox.IsEditable || model.FullNiche != "Động vật") throw new InvalidOperationException("Niche selector smoke failed.");
        var name = FullNameBox.Text; var niche = FullNicheBox.SelectedItem;
        HistoryFilters.IsExpanded = true; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        if (!model.NewJobExpanded || !model.DetailsExpanded || FullNameBox.Text != name || FullNicheBox.SelectedItem != niche) throw new InvalidOperationException("Opening filters changed another section or form values.");
        CheckDetailLayout();
        NewJobPanel.IsExpanded = false; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        NewJobPanel.IsExpanded = true; await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.DataBind);
        if (!HistoryFilters.IsExpanded || !model.DetailsExpanded || FullNameBox.Text != name || FullNicheBox.SelectedItem != niche) throw new InvalidOperationException("Reopening input lost form values.");
        await Task.Delay(150); UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "input-controls.png"))) encoder.Save(file);
        model.FullName = ""; model.FullNiche = ""; model.Niches.Clear();
        return new { maxVisibleNameCharacters = 30, spacesCounted = true, unicodeNotSplit = true, nicheSelectionBound = true, collapsePreservesInput = true,
            diagnostic = "Actual WPF input controls; mock niche items, no Google reads/writes" };
    }
    private async Task<object> CheckRepeatedNicheControlsAsync()
    {
        try
        {
            return await model.CheckRepeatedNicheSmokeAsync(async fixture =>
            {
                DataContext = fixture;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                FullNicheBox.SelectedItem = "Động vật";
                for (var i = 0; i < 3; i++)
                {
                    FullLinkBox.Text = "https://www.douyin.com/video/765970279492814774" + i;
                    FullNameBox.Text = "Video liên tiếp " + (i + 1);
                    await Dispatcher.InvokeAsync(() => CommandManager.InvalidateRequerySuggested(), DispatcherPriority.ContextIdle);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    var submit = Descendants<System.Windows.Controls.Button>(NewJobPanel).Single(b => ReferenceEquals(b.Command, fixture.SubmitFullCommand));
                    if (fixture.FullNiche != "Động vật" || !Equals(FullNicheBox.SelectedItem, "Động vật") || !submit.IsEnabled)
                        throw new InvalidOperationException("Selected niche or enabled submit button lost before video " + (i + 1));
                    InvokeButton(submit);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    for (var wait = 0; fixture.Busy && wait < 200; wait++) await Task.Delay(25);
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                    if (fixture.Busy || fixture.FullLink != "" || fixture.FullName != "" || fixture.FullNiche != "Động vật" || !Equals(FullNicheBox.SelectedItem, "Động vật"))
                        throw new InvalidOperationException($"Submission did not clear video fields and retain the selected niche after video {i + 1}: busy={fixture.Busy}, link={fixture.FullLink}, name={fixture.FullName}, niche={fixture.FullNiche}, selection={FullNicheBox.SelectedItem}, notice={fixture.Notice}");
                    if (submit.IsEnabled) throw new InvalidOperationException("Empty video fields must still disable submit.");
                }
            });
        }
        finally { DataContext = model; }
    }
    private async Task RenderWizardAsync()
    {
        var service = new SmokeGoogleConnections(); var saves = 0;
        var draft = new GoogleConnectionDraft(service, (_, _, _) => { saves++; return Task.CompletedTask; });
        var wizardModel = new ConnectionWizardViewModel(draft, CancellationToken.None);
        var window = new ConnectionWizardWindow(wizardModel) { Owner = this }; window.Show();
        wizardModel.SelectSheetCommand.Execute(null);
        for (var step = 0; step < 3; step++)
        {
            for (var i = 0; wizardModel.Busy && i < 100; i++) await Task.Delay(50);
            if (wizardModel.Busy || !(step == 0 ? wizardModel.IsSheet : step == 1 ? wizardModel.IsChannel : wizardModel.IsConfirm)) throw new InvalidOperationException("Wizard smoke step failed.");
            if (step == 0) {
                if (wizardModel.SelectedBlockedTab?.Id != 9 || !wizardModel.NextCommand.CanExecute(null)) throw new InvalidOperationException("Required block tab not suggested.");
                wizardModel.SelectedBlockedTab = wizardModel.SelectedTab;
                if (wizardModel.NextCommand.CanExecute(null)) throw new InvalidOperationException("Same block/video tab accepted.");
                wizardModel.SelectedBlockedTab = wizardModel.SelectedSourceTab;
                if (wizardModel.NextCommand.CanExecute(null)) throw new InvalidOperationException("Same block/source tab accepted.");
                wizardModel.SelectedBlockedTab = null;
                if (wizardModel.NextCommand.CanExecute(null)) throw new InvalidOperationException("Missing block tab accepted.");
                wizardModel.SelectedBlockedTab = wizardModel.Tabs.Single(t => t.Id == 9);
                wizardModel.Error(new NeedsAuthException("Phiên Google thử đã hết hạn. Chọn lại bảng để cấp quyền."));
                if (!wizardModel.NoticeIsError) throw new InvalidOperationException("Wizard error must be distinguished from progress.");
                await CheckCompactDialogAsync(window, "wizard-error");
            }
            if (step == 0) ((FrameworkElement)window.FindName("BlockedTabBox")).BringIntoView();
            await Task.Delay(200); window.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "wizard-" + step + ".png"))) encoder.Save(file);
            await CheckCompactDialogAsync(window, "wizard-" + step);
            if (step == 0) { wizardModel.NextCommand.Execute(null); wizardModel.SelectChannelCommand.Execute(null); }
            else if (step == 1) wizardModel.NextCommand.Execute(null);
        }
        await wizardModel.CancelAsync(); window.Close();
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, "wizard.json"), JsonSerializer.Serialize(new { stagesRendered = 3, requiredBlockedTab = true, distinctBlockedTab = true, saves, credentialsCleaned = service.Forgotten,
            diagnostic = "Mock Google service; UI and cancellation only, no real login, sheet writes or uploads" }));
    }
    private async Task CheckCompactDialogAsync(Window window, string name)
    {
        var width = window.Width; var height = window.Height;
        window.Width = window.MinWidth; window.Height = window.MinHeight;
        await Task.Delay(100); window.UpdateLayout();
        var root = (System.Windows.Controls.DockPanel)window.Content;
        var footer = root.Children.OfType<FrameworkElement>().Single(x => System.Windows.Controls.DockPanel.GetDock(x) == System.Windows.Controls.Dock.Bottom);
        var bottom = footer.TranslatePoint(new Point(0, footer.ActualHeight), window).Y;
        if (!footer.IsVisible || footer.ActualHeight < 30 || bottom > window.ActualHeight - 12)
            throw new InvalidOperationException("Dialog actions are clipped at minimum size: " + name);
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(Path.GetDirectoryName(App.SmokeOutput)!, name + "-compact.png"))) encoder.Save(file);
        window.Width = width; window.Height = height; await Task.Delay(60); window.UpdateLayout();
    }
    private void CopyVideoName(object sender, RoutedEventArgs e) => CopyHistoryValue(sender, copyLink: false);
    private void CopyVideoLink(object sender, RoutedEventArgs e) => CopyHistoryValue(sender, copyLink: true);
    private void CopyHistoryValue(object sender, bool copyLink)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: VideoJob job }) return;
        model.SelectedJob = job;
        var value = copyLink ? job.VideoLink : job.Name;
        var label = copyLink ? "link video" : "tên video";
        if (string.IsNullOrWhiteSpace(value)) { model.ReportSourceActivity($"Công việc chưa có {label} để sao chép."); return; }
        try
        {
            Clipboard.SetText(value);
            model.ReportSourceActivity($"Đã sao chép {label}.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            model.ReportSourceActivity("Chưa sao chép được vì bộ nhớ tạm đang bận. Bấm lại để thử.");
        }
    }
    private void PauseRow(object sender, RoutedEventArgs e) { if (((System.Windows.Controls.Button)sender).DataContext is VideoJob job) { model.SelectedJob = job; model.TogglePauseCommand.Execute(null); } }
    private void AddRow(object sender, RoutedEventArgs e) { if (((System.Windows.Controls.Button)sender).DataContext is VideoJob job) { model.SelectedJob = job; model.AddActionsCommand.Execute(null); } }
    private void DeleteRow(object sender, RoutedEventArgs e) { if (((System.Windows.Controls.Button)sender).DataContext is VideoJob job) { model.SelectedJob = job; model.RemoveJobCommand.Execute(null); } }
    private Task<object> RenderAttentionSmokeAsync() => model.CheckAttentionSmokeAsync(async (first, second) =>
    {
        model.TabIndex = 0;
        var dialog = new AttentionWindow(model); dialog.Show(); await Task.Delay(100);
        dialog.SelectJob(second); await CheckCompactDialogAsync(dialog, "attention-choices");
        dialog.Close();
        if (model.Attentions.Count != 2) throw new InvalidOperationException("Closing a notification must not dismiss or pause pending work.");
        dialog = new AttentionWindow(model); dialog.Show(); dialog.SelectJob(first); await Task.Delay(100);
        if (dialog.SelectedAttention!.Choices.Any(x => x.Action == AttentionAction.Retry)) throw new InvalidOperationException("Unknown upload must not offer blind retry.");
        dialog.WindowState = WindowState.Minimized; WindowAttention.Present(dialog); await Task.Delay(150);
        if (!dialog.IsVisible || dialog.WindowState != WindowState.Normal || dialog.Topmost) throw new InvalidOperationException("Notification foreground restore must not remain always-on-top.");
        await CheckCompactDialogAsync(dialog, "attention-upload");
        dialog.SelectJob(second); dialog.ChooseForSmoke(AttentionAction.Pause); await WaitForCommandAsync(); await Task.Delay(150);
        if (model.Attentions.Count != 1 || dialog.SelectedAttention?.JobId != first) throw new InvalidOperationException("Resolved notification did not update the pending list.");
        dialog.Close();
        foreach (var link in new[] { "https://youtu.be/abcDEF_1234", "https://www.youtube.com/watch?v=abcDEF_1234", "https://studio.youtube.com/video/abcDEF_1234/edit" })
            if (UploadReviewWindow.ParseVideoId(link) != "abcDEF_1234") throw new InvalidOperationException("Review link parser rejected a valid video ID.");
        if (UploadReviewWindow.ParseVideoId("https://evil.example/watch?v=abcDEF_1234") is not null) throw new InvalidOperationException("Review accepted a foreign domain.");
        var review = new UploadReviewWindow("Video thử kết quả upload"); review.Show(); await CheckCompactDialogAsync(review,"upload-result-review"); review.Close();
    });
    private Task<object> RenderOperationNoticesAsync() => model.CheckOperationNoticesAsync(async () =>
    {
        exerciseNotifications = true;
        try
        {
            var blocking = new ActionConfirmationWindow("Đang xác nhận thao tác", "Yêu cầu mới phải chờ, không mở cửa sổ khác lên trên hộp thoại này.", "Tiếp tục") { Owner = this };
            blocking.Loaded += (_, _) => {
                OnAttentionRequired();
                if (!attentionAnnouncementPending || attentionWindow is not null) throw new InvalidOperationException("A notification covered an active modal interaction.");
                Dispatcher.BeginInvoke(new Action(() => blocking.Close()));
            };
            blocking.ShowDialog(); PumpNotifications();
            if (attentionWindow is null || !attentionWindow.IsVisible || attentionAnnouncementPending) throw new InvalidOperationException("Deferred notifications did not open after the modal interaction ended.");
            await CheckCompactDialogAsync(attentionWindow, "operation-notices"); attentionWindow.Close();
            PumpNotifications();
            if (attentionWindow is not null) throw new InvalidOperationException("A deferred notification reopened after explicit deferral.");
        }
        finally { exerciseNotifications = false; attentionWindow?.Close(); }
        var dialog = new AttentionWindow(model); dialog.Show(); dialog.Close();
        if (model.Attentions.Count != 2) throw new InvalidOperationException("Closing operation inbox lost a request.");
        dialog = new AttentionWindow(model); dialog.Show(); await Task.Delay(100);
        dialog.ChooseForSmoke(AttentionAction.ResolveNotice); await WaitForCommandAsync(); dialog.Close();
        model.TabIndex = 1; model.SetTabsForSmoke(true);
        var originalVideo = model.SelectedTab; var originalSource = model.SelectedSourceChannelTab;
        model.SelectedTab = originalSource; model.SelectedSourceChannelTab = originalVideo;
        await Task.Delay(100);
        if (model.VideoTabError.Length == 0 || model.SourceTabError.Length == 0 || model.SelectedTab?.Id != 7 || model.SelectedSourceChannelTab?.Id != 8)
            throw new InvalidOperationException("Invalid tab selection must be rejected with an inline error and keep the original targets.");
        ConnectSheetButton.BringIntoView(); UpdateLayout(); CaptureMain("inline-tab-errors"); model.SetTabsForSmoke(false);
        var confirmation = new ActionConfirmationWindow("Video đã tồn tại", "Video đã có một công việc và kết quả được giữ. Mở công việc để kiểm tra trước khi làm tiếp.", "Mở công việc đã có") { Owner = this };
        confirmation.Show(); await CheckCompactDialogAsync(confirmation, "explicit-confirmation"); confirmation.Close();
    });
    private void OpenAttention(object sender, RoutedEventArgs e) => PresentAttentions();
    private void OnAttentionRequired()
    {
        if (App.SmokeOutput is not null && !exerciseNotifications || App.SessionDiagnosticOutput is not null || exiting) return;
        if (attentionWindow?.IsActive == true) { attentionAnnouncementPending = false; return; }
        if (DateTimeOffset.UtcNow - lastAttentionPresentation < TimeSpan.FromSeconds(5)) { attentionAnnouncementPending = true; return; }
        if (model.Busy || InteractionInProgress())
        {
            if (!attentionAnnouncementPending && model.HasAttentions && !IsVisible)
            { tray.Visible = true; tray.ShowBalloonTip(5000, "Tìm video hay cần bạn lựa chọn", "Có yêu cầu đang chờ. Bấm để mở; cửa sổ xác minh hiện tại được giữ nguyên.", System.Windows.Forms.ToolTipIcon.Warning); }
            attentionAnnouncementPending = true; return;
        }
        attentionAnnouncementPending = false;
        if (model.HasAttentions) PresentAttentions();
    }
    private void PresentAttentions()
    {
        if (!model.HasAttentions || exiting) return;
        ShowApp();
        if (attentionWindow is null)
        {
            attentionWindow = new AttentionWindow(model);
            attentionWindow.Closed += (_, _) => attentionWindow = null;
        }
        if (model.PreferredAttentionJobId is { } preferred && model.Attentions.Any(x => x.JobId == preferred || x.RelatedJobIds.Contains(preferred))) attentionWindow.SelectJob(preferred);
        model.PreferredAttentionJobId = null;
        lastAttentionPresentation = DateTimeOffset.UtcNow;
        WindowAttention.Present(attentionWindow);
    }
    private bool InteractionInProgress() => ComponentDispatcher.IsThreadModal || WindowAttention.IsInputBlocked(this) || DouyinBrowserReader.HasVisibleInteraction;
    private void PumpNotifications()
    {
        if (attentionAnnouncementPending && !model.Busy && !InteractionInProgress()) OnAttentionRequired();
        if (completedNames.Count > 0 && DateTimeOffset.UtcNow >= completionDue)
        {
            if ((!IsVisible || WindowState == WindowState.Minimized) && !InteractionInProgress() && !model.HasAttentions)
            {
                tray.Visible = true;
                tray.ShowBalloonTip(5000, "Đã hoàn tất " + completedNames.Count + " công việc", string.Join(", ", completedNames.Values.Take(3)) + ". Bấm để mở lịch sử và xem kết quả.", System.Windows.Forms.ToolTipIcon.Info);
            }
            completedNames.Clear();
        }
    }
    private void OnBackgroundNotice(string id, string name)
    {
        if (App.SmokeOutput is not null || exiting) return;
        completedNames[id] = name; completionDue = DateTimeOffset.UtcNow.AddSeconds(3);
    }
    private void FocusNotificationTarget(OperationArea area)
    {
        attentionWindow?.Close(); ShowApp();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (area == OperationArea.GoogleClient) GoogleAdvancedSettings.IsExpanded = true;
            FrameworkElement target = area switch {
                OperationArea.Input => model.FullLinkError.Length > 0 || string.IsNullOrWhiteSpace(model.FullLink) ? FullLinkBox
                    : string.IsNullOrWhiteSpace(model.FullName) || model.FullNameError.Length > 0 ? FullNameBox : FullNicheBox,
                OperationArea.History => FullHistory, OperationArea.YouTube => ConnectChannelButton,
                OperationArea.Storage => ChooseStorageButton, OperationArea.GoogleClient => ImportGoogleButton, _ => ConnectSheetButton };
            target.BringIntoView(); target.Focus(); WindowAttention.Present(this);
        }));
    }
    private async void RevealAttentionJob()
    {
        attentionWindow?.Close(); ShowApp(); await Task.Delay(80); WorkArea.ScrollToEnd(); WindowAttention.Present(this);
    }
    private void ShowApp() { Show(); if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); tray.Visible = false; }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (exiting) return;
        e.Cancel = true;
        if (model.Coordinator.IsRunning || model.Busy || model.HasAttentions)
        {
            attentionWindow?.Close(); tray.Visible = true; Hide();
            var message = model.HasAttentions && !model.Coordinator.IsRunning && !model.Busy ? "Có yêu cầu đang chờ bạn lựa chọn. Mở app từ khay để xử lý; chọn Thoát để đóng app." : "Công việc tiếp tục trong khay hệ thống. Chọn Thoát để dừng và lưu checkpoint.";
            tray.ShowBalloonTip(3500, "Tìm video hay vẫn chạy", message, System.Windows.Forms.ToolTipIcon.Info);
        }
        else _ = ExitAsync();
    }
    private async Task ExitAsync() { if (exiting) return; exiting = true; await ((App)Application.Current).ExitAsync(); tray.Dispose(); }
}
internal sealed class SmokeGoogleConnections : IGoogleConnectionService
{
    public int Forgotten { get; private set; }
    public Task<SheetSelection> SelectSheetAsync(CancellationToken ct) => Task.FromResult(new SheetSelection("ui-sheet", "Bảng thử giao diện — tài khoản A", [new(7, "Video"), new(8, "Kênh nguồn"), new(9, "Kênh block")], "ui-sheets-A"));
    public Task<ChannelTarget> SelectChannelAsync(CancellationToken ct) => Task.FromResult(new ChannelTarget("ui-channel", "Kênh thử giao diện — tài khoản B", "ui-youtube-B"));
    public Task CheckSheetAsync(SheetTarget target, CancellationToken ct) => Task.CompletedTask;
    public Task ForgetAsync(string credential, CancellationToken ct) { Forgotten++; return Task.CompletedTask; }
}
