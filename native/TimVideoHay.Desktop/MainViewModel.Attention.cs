using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using TimVideoHay.Core;

namespace TimVideoHay.Desktop;

public sealed partial class MainViewModel
{
    private readonly JobAttentionInbox attentionInbox = new();
    public ObservableCollection<JobAttention> Attentions { get; } = new();
    public string AttentionLabel => $"Cần bạn lựa chọn ({Attentions.Count})";
    public bool HasAttentions => Attentions.Count > 0;
    public event Action? AttentionRequired;
    internal string? PreferredAttentionJobId { get; set; }
    public event Action? AttentionJobOpened;
    private void UpdateAttentions(IEnumerable<VideoJob> jobs)
    {
        var announce = attentionInbox.Update(jobs);
        jobAttentions = attentionInbox.Items;
        MergeNotifications();
        if (announce) AttentionRequired?.Invoke();
    }
    public void DecideAttention(JobAttention item, AttentionAction action)
    {
        var command = Command(async () =>
        {
            if (item.OperationId is not null) { await DecideOperationAsync(item, action); return; }
            VideoJob? job;
            try { job = await Coordinator.AcknowledgeAsync(item, action); }
            catch { await RefreshAsync(); throw; }
            if (job is null) { await RefreshAsync(); return; }
            if (action == AttentionAction.Inspect)
            {
                ResetHistoryFilters(); await RefreshAsync(); SelectedJob = Jobs.FirstOrDefault(x => x.Id == item.JobId);
                TabIndex = 0; DetailsExpanded = true; AttentionJobOpened?.Invoke(); return;
            }
            if (action == AttentionAction.OmitAffected) await Coordinator.OmitAffectedAsync(job.Id);
            else if (action == AttentionAction.Pause)
            {
                foreach (var id in item.RelatedJobIds.DefaultIfEmpty(item.JobId))
                {
                    var pending = (await store.ListAsync()).SingleOrDefault(x => x.Id == id);
                    if (pending is not null && JobHistory.NeedsAttention(pending) && (id == item.JobId || pending.Issue?.DependencyKey == item.DependencyKey))
                    {
                        if (id != item.JobId) await Coordinator.AcknowledgeAsync(JobAttention.From(pending)!, AttentionAction.Pause);
                        await Coordinator.PauseJobAsync(id);
                    }
                }
            }
            else if (action == AttentionAction.Retry && item.DependencyKey is not null && item.RelatedJobIds.Count > 1)
            {
                foreach (var id in item.RelatedJobIds)
                {
                    var pending = (await store.ListAsync()).SingleOrDefault(x => x.Id == id);
                    if (pending is null || !JobHistory.NeedsAttention(pending) || pending.Issue?.DependencyKey != item.DependencyKey) continue;
                    if (id != item.JobId) await Coordinator.AcknowledgeAsync(JobAttention.From(pending)!, AttentionAction.Retry);
                    await Coordinator.ResumeAsync(id);
                }
            }
            else
            {
                if (action == AttentionAction.ReconnectGoogle)
                {
                    await ReconnectJobAsync(job!.Id);
                    await RefreshAsync();
                    return; // Reconnect already resumes related jobs using the new credential.
                }
                if (action == AttentionAction.Reconcile && !await ReconcileAsync(job!.Id)) return;
                if (action == AttentionAction.LocateFolder && await ChooseMovedFolderAsync(job!.Id) is null) return;
                // Existing resume checks original destinations and remote checkpoints before replaying any write.
                if (job!.DeletionRequested && job.DeleteYouTubeRequested && !job.YouTubeDeleted) await EnsureDeletePermissionAsync(job);
                await Coordinator.ResumeAsync(job.Id);
            }
            await RefreshAsync();
            Notice = action == AttentionAction.Pause ? "Đã tạm dừng công việc theo lựa chọn của bạn." : "Đang tiếp tục từ tiến độ đã lưu; các kết quả đã có được kiểm tra trước khi gửi lại.";
        }, area: OperationArea.History);
        command.Execute(null);
    }
    internal async Task<object> CheckAttentionSmokeAsync(Func<string, string, Task> capture)
    {
        var first = new VideoJob { Name = "Video đang chờ xác nhận kết quả", State = JobState.NeedsAction, Actions = JobActions.All,
            MediaSendStarted = true, Stage = "Đăng YouTube", Message = "Lần gửi trước chưa trả ID video. Kiểm tra kết quả trước khi gửi lại để tránh đăng trùng." };
        var second = new VideoJob { Name = "Video cần đăng nhập lại", State = JobState.NeedsAuth, Actions = JobActions.WriteSheet,
            Stage = "Ghi Google Sheets", Message = "Quyền Google đã hết hạn. Đăng nhập lại đúng tài khoản để tiếp tục.", Progress = 37.5 };
        await store.SaveAsync(first); await store.SaveAsync(second);
        try
        {
            await RefreshAsync();
            if (Attentions.Count != 2) throw new InvalidOperationException("Pending notifications must ignore history filters.");
            await capture(first.Id, second.Id);
            var saved = (await store.ListAsync()).Single(x => x.Id == second.Id);
            if (saved.State != JobState.Paused || saved.Progress != 37.5 || Attentions.Any(x => x.JobId == second.Id))
                throw new InvalidOperationException("Notification pause must keep progress and apply only to the selected job.");
            if (Attentions.Single().JobId != first.Id) throw new InvalidOperationException("Unanswered notification was lost.");
            return new { pendingNotificationsGrouped = true, historyFiltersDoNotHideRequests = true, notificationChoicesMatchState = true,
                explicitPauseKeepsProgress = true, otherRequestsRemainPending = true, foregroundRestoredWithoutPermanentTopmost = true,
                diagnostic = "Actual WPF/SQLite fixture; no Google or Douyin requests, no real jobs" };
        }
        finally
        {
            first.RemovedFromHistory = true; second.RemovedFromHistory = true;
            await store.SaveAsync(first); await store.SaveAsync(second); await RefreshAsync();
        }
    }
}
