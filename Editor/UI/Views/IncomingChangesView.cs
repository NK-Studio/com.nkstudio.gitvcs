using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Incoming Changes 탭: 마지막 fetch 기준 upstream 에만 있는 커밋과, 그 커밋들이 바꾼 파일(<c>HEAD...@{u}</c>).
    /// 커밋하지 않은 로컬 변경과 겹치는 파일을 표시한다 — Unity 씬·프리팹은 텍스트 병합이 사실상 안 되므로 Pull 전에 알아야 한다.
    /// </summary>
    public sealed class IncomingChangesView : GitView
    {
        private const int MaxCommits = 500;

        private readonly Label _summary;
        private readonly ToolbarButton _fetchButton;
        private readonly ToolbarButton _pullButton;
        private readonly VisualElement _errorNotice;
        private readonly Label _errorText;
        private readonly VisualElement _infoNotice;
        private readonly Label _infoText;
        private readonly VisualElement _divergedNotice;
        private readonly Label _divergedText;
        private readonly VisualElement _overlapNotice;
        private readonly Label _overlapText;
        private readonly VisualElement _split;
        private readonly CommitList _commits;
        private readonly FileBrowser _files;

        private List<GitFileChange> _incomingFiles = new();
        private HashSet<string> _overlapPaths = new(StringComparer.OrdinalIgnoreCase);
        private string _loadedKey;
        private bool _isLoading;
        private string _error;

        public override string Title => GitText.Get("tab.incoming");

        public override string Badge
        {
            get
            {
                int behind = Services.Repository.Branch?.Behind ?? 0;
                return behind > 0 ? behind.ToString() : null;
            }
        }

        private bool HasUpstream => string.IsNullOrEmpty(Services.Repository.UpstreamCommit) == false;
        private string CurrentKey => $"{Services.Repository.HeadCommit}|{Services.Repository.UpstreamCommit}";

        public IncomingChangesView(GitServices services) : base(services, "IncomingChangesView")
        {
            _summary = Root.Require<Label>("summary");
            _fetchButton = Root.Require<ToolbarButton>("fetch-button");
            _pullButton = Root.Require<ToolbarButton>("pull-button");
            _errorNotice = Root.Require<VisualElement>("error-notice");
            _errorText = Root.Require<Label>("error-text");
            _infoNotice = Root.Require<VisualElement>("info-notice");
            _infoText = Root.Require<Label>("info-text");
            _divergedNotice = Root.Require<VisualElement>("diverged-notice");
            _divergedText = Root.Require<Label>("diverged-text");
            _overlapNotice = Root.Require<VisualElement>("overlap-notice");
            _overlapText = Root.Require<Label>("overlap-text");
            _split = Root.Require<VisualElement>("split");

            _commits = new CommitList(Root.Require<ListView>("commit-list"), services.Assets);
            _files = new FileBrowser(Root.Require<VisualElement>("file-list"), Root.Require<VisualElement>("view-mode-host"), services);

            _fetchButton.clicked += () => _ = Services.Runner.RunAsync("Fetch", Services.Commands.FetchAsync, refreshAssets: false);
            _pullButton.clicked += Pull;
        }

        protected override void OnActivated()
        {
            ReloadIfNeeded();
            Render();
        }

        public override void OnRepositoryChanged()
        {
            // 겹침은 로컬 변경이 바뀔 때마다 다시 계산한다(git 호출 없음).
            UpdateOverlaps();
            if (IsActive)
            {
                ReloadIfNeeded();
            }
            Render();
            NotifyBadgeChanged();
        }

        public override void OnBusyChanged(bool isBusy)
        {
            Render();
        }

        private void ReloadIfNeeded()
        {
            if (_isLoading == false && _loadedKey != CurrentKey)
            {
                Load();
            }
        }

        private async void Load()
        {
            string key = CurrentKey;
            if (HasUpstream == false || Services.Repository.HeadCommit == null)
            {
                _commits.SetItems(Array.Empty<GitCommitInfo>());
                _incomingFiles = new List<GitFileChange>();
                _loadedKey = key;
                UpdateOverlaps();
                Render();
                return;
            }

            _isLoading = true;
            Render();
            (List<GitCommitInfo> commits, string error) = await Services.History.LogAsync("HEAD..@{u}", false, 0, MaxCommits);
            List<GitFileChange> files = commits.Count > 0
                ? await Services.History.ChangedSinceMergeBaseAsync("HEAD", "@{u}")
                : new List<GitFileChange>();
            _isLoading = false;

            _error = error;
            _loadedKey = key;
            _commits.SetItems(commits, c => c.Hash);
            _incomingFiles = files;
            UpdateOverlaps();
            Render();
        }

        private void UpdateOverlaps()
        {
            HashSet<string> localPaths = new(StringComparer.OrdinalIgnoreCase);
            foreach (GitStatusEntry entry in Services.Repository.Entries)
            {
                localPaths.Add(entry.RepoPath);
                if (entry.OriginalRepoPath != null)
                {
                    localPaths.Add(entry.OriginalRepoPath);
                }
            }

            _overlapPaths = new HashSet<string>(
                _incomingFiles.Where(f => localPaths.Contains(f.RepoPath) || (f.OriginalRepoPath != null && localPaths.Contains(f.OriginalRepoPath)))
                    .Select(f => f.RepoPath),
                StringComparer.OrdinalIgnoreCase);

            _files.SetItems(_incomingFiles.Select(change =>
            {
                FileListItem item = FileListItem.From(change);
                item.Note = _overlapPaths.Contains(change.RepoPath) ? GitText.Get("incoming.localChange") : null;
                return item;
            }));
        }

        private void Render()
        {
            GitBranchInfo branch = Services.Repository.Branch;
            bool busy = Services.Runner.IsBusy;

            DateTime? lastFetch = Services.Repository.LastFetchTime;
            string fetched = lastFetch.HasValue ? GitText.Format("incoming.lastFetch", lastFetch.Value.ToString("MM-dd HH:mm")) : GitText.Get("incoming.noFetch");
            _summary.text = _isLoading ? GitText.Get("common.loading") : GitText.Format("incoming.summary", branch?.Upstream ?? "-", _commits.Items.Count, _incomingFiles.Count, fetched);

            _fetchButton.SetEnabled(busy == false && HasUpstream);
            _pullButton.SetEnabled(busy == false && branch != null && branch.Behind > 0 && branch.Ahead == 0);

            _errorNotice.SetVisible(string.IsNullOrEmpty(_error) == false);
            _errorText.text = _error ?? string.Empty;

            string info = null;
            if (HasUpstream == false)
            {
                info = Services.Repository.HeadCommit == null
                    ? GitText.Get("common.noCommits")
                    : GitText.Get("incoming.noUpstream");
            }
            else if (_commits.Items.Count == 0 && _isLoading == false)
            {
                info = GitText.Get("incoming.nothing");
            }
            _infoNotice.SetVisible(info != null);
            _infoText.text = info ?? string.Empty;

            bool diverged = branch != null && branch.Ahead > 0 && branch.Behind > 0;
            _divergedNotice.SetVisible(diverged);
            _divergedText.text = diverged ? GitText.Format("incoming.diverged", branch.Ahead, branch.Behind) : string.Empty;

            _overlapNotice.SetVisible(_overlapPaths.Count > 0);
            _overlapText.text = GitText.Format("incoming.overlap", _overlapPaths.Count);

            _split.SetVisible(HasUpstream && _commits.Items.Count > 0);
        }

        private async void Pull()
        {
            if (_overlapPaths.Count > 0 && Services.Dialogs.Confirm("Pull",
                    GitText.Format("incoming.pullConfirm", _overlapPaths.Count), "Pull") == false)
            {
                return;
            }
            await Services.Runner.RunAsync("Pull", Services.Commands.PullAsync, refreshAssets: true);
        }
    }
}
