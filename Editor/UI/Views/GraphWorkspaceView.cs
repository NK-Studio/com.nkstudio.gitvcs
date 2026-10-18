using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitKraken 형태의 작업 탭.
    /// 가운데: 세로 커밋 그래프(<see cref="CommitGraphList"/>). 오른쪽 파일을 고르면 그 자리에 diff 가 열리고 × 로 그래프로 돌아온다.
    /// 오른쪽: 맨 위 "작업 중" 줄을 고르면 Unstaged·Staged·커밋(푸시) 패널, 커밋 줄을 고르면 그 커밋의 메시지·바뀐 파일.
    /// </summary>
    public sealed class GraphWorkspaceView : GitView
    {
        /// <summary>
        /// 한 번에 읽을 커밋 수. 세로 목록은 가상화라 그리기 부담은 적고, git log·그래프 배치(작업 스레드)가 커밋 수에 비례한다.
        /// 커밋 ~4500·브랜치 3000 저장소에서 읽기~배치 ~0.3초, 스크롤 한 장 ~4ms(2026-10-18 실측).
        /// </summary>
        private const int MaxCommits = 5000;

        private readonly ITabNavigator _navigator;
        private readonly ToolbarButton _reloadButton;
        private readonly Label _summary;
        private readonly VisualElement _errorNotice;
        private readonly Label _errorText;
        private readonly VisualElement _graphHost;
        private readonly VisualElement _diffHost;
        private readonly VisualElement _wipPanel;
        private readonly VisualElement _detailHost;
        private readonly Label _wipTitle;
        private readonly CommitGraphList _graph;
        private readonly GitDiffPanel _diff;
        private readonly StagingLists _staging;
        private readonly CommitBox _commit;
        private readonly CommitDetailPanel _detail;

        private string _loadedKey;
        private bool _isLoading;

        public override string Title => GitText.Get("tab.graph");
        public override string Badge => Services.Repository.Entries.Count > 0 ? Services.Repository.Entries.Count.ToString() : null;

        private string CurrentKey => $"{Services.Repository.HeadCommit}|{Services.Repository.UpstreamCommit}|{Services.Repository.Branch?.Name}|{Services.Repository.LastFetchTime}";

        public GraphWorkspaceView(GitServices services, StagingState state, ITabNavigator navigator) : base(services, "GraphWorkspaceView")
        {
            _navigator = navigator;
            _reloadButton = Root.Require<ToolbarButton>("reload-button");
            _summary = Root.Require<Label>("summary");
            _errorNotice = Root.Require<VisualElement>("error-notice");
            _errorText = Root.Require<Label>("error-text");
            _graphHost = Root.Require<VisualElement>("graph-host");
            _diffHost = Root.Require<VisualElement>("diff-host");
            _wipPanel = Root.Require<VisualElement>("wip-panel");
            _detailHost = Root.Require<VisualElement>("detail-host");
            _wipTitle = Root.Require<Label>("wip-title");

            _errorNotice.SetVisible(false);
            _graph = new CommitGraphList(_graphHost, services.Avatars);
            _diff = new GitDiffPanel(services.Diff);
            _diff.Root.style.flexGrow = 1;
            _diff.ShowCloseButton(true);
            _diffHost.Add(_diff.Root);
            _staging = new StagingLists(Root.Require<VisualElement>("staging-host"), services);
            _commit = new CommitBox(Root.Require<VisualElement>("commit-host"), services, state, showPush: true);
            _detail = new CommitDetailPanel(_detailHost, services, closable: false);
            _detail.ShowPlaceholder();

            ToolbarIcons.SetRefreshIcon(_reloadButton);
            _reloadButton.clicked += Load;
            _graph.WipSelected += ShowWip;
            _graph.CommitSelected += ShowCommit;
            _graph.ContextMenuRequested += PopulateCommitMenu;
            _staging.FileSelected += ShowDiff;
            _diff.CloseRequested += () =>
            {
                _staging.ClearSelection();
                ShowDiff(null);
            };

            ShowDiff(null);
            ShowWip();
        }

        protected override void OnActivated()
        {
            RefreshWorkingTree();
            ReloadIfNeeded();
        }

        public override void OnRepositoryChanged()
        {
            RefreshWorkingTree();
            if (IsActive)
            {
                ReloadIfNeeded();
            }
        }

        public override void OnBusyChanged(bool isBusy)
        {
            _staging.OnBusyChanged();
            _commit.Render();
            _reloadButton.SetEnabled(_isLoading == false);
        }

        public override void Dispose()
        {
            _staging.Dispose();
        }

        /// <summary>
        /// 작업 트리 변경은 커밋 그래프와 따로 새로 칠한다 — 파일을 저장할 때마다 git log 를 다시 돌리지 않게.
        /// </summary>
        private void RefreshWorkingTree()
        {
            IReadOnlyList<GitStatusEntry> entries = Services.Repository.Entries;
            _staging.SetEntries(entries);
            _commit.SetStagedCount(_staging.StagedCount);
            _wipTitle.text = GitText.Format("graph.wipTitle", entries.Count, Services.Repository.Branch?.Name ?? "-");
            if (_graph.Graph != null)
            {
                _graph.SetGraph(_graph.Graph, entries.Count);
            }
            if (_diff.Target != null)
            {
                _diff.Reload();
            }
            NotifyBadgeChanged();
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
            _isLoading = true;
            _reloadButton.SetEnabled(false);
            _summary.text = GitText.Get("common.loading");
            string key = CurrentKey;

            (GitGraph graph, string error) = await GraphLoader.LoadAsync(Services.History, Services.Repository.HeadCommit, null, MaxCommits);
            _isLoading = false;
            _loadedKey = key;
            _reloadButton.SetEnabled(true);

            _errorNotice.SetVisible(string.IsNullOrEmpty(error) == false);
            _errorText.text = error ?? string.Empty;

            _graph.SetGraph(graph, Services.Repository.Entries.Count);
            _summary.text = GitText.Format("explorer.summary", graph.Nodes.Count, graph.Lanes.Count)
                            + (graph.Nodes.Count >= MaxCommits ? GitText.Format("explorer.capped", MaxCommits) : string.Empty);

            // 기다리는 동안 저장소가 또 바뀌었으면 한 번 더 읽는다.
            ReloadIfNeeded();
        }

        private void ShowWip()
        {
            _wipPanel.SetVisible(true);
            _detailHost.SetVisible(false);
        }

        private void ShowCommit(GitGraphNode node)
        {
            _wipPanel.SetVisible(false);
            _detailHost.SetVisible(true);
            _staging.ClearSelection();
            ShowDiff(null);
            if (_detail.ShownHash != node.Commit.Hash)
            {
                _detail.Show(node.Commit, node.Lane.IsAnonymous ? null : node.Lane.Name);
            }
        }

        /// <summary>
        /// 가운데를 diff 로 바꾼다. null 이면 그래프로 돌아간다.
        /// </summary>
        private void ShowDiff(GitDiffTarget target)
        {
            _diffHost.SetVisible(target != null);
            _graphHost.SetVisible(target == null);
            if (target != null)
            {
                _diff.SetTarget(target);
            }
        }

        private void PopulateCommitMenu(DropdownMenu menu, GitGraphNode node)
        {
            menu.AppendAction(GitText.Get("common.copyHash"), _ => Services.Assets.CopyToClipboard(node.Commit.Hash));
            menu.AppendAction(GitText.Get("common.copySubject"), _ => Services.Assets.CopyToClipboard(node.Commit.Subject));
            menu.AppendSeparator();
            menu.AppendAction(GitText.Get("common.openBranchesTab"), _ => _navigator.SelectTab<BranchesView>());
        }
    }
}
