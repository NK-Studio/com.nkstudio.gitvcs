using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Branch Explorer 탭: Unity Version Control 의 Branch Explorer 처럼 브랜치 흐름을 그래프로 보여 준다.
    /// 그래프 배치는 <see cref="GitGraph"/>, 그리기·조작은 <see cref="BranchGraphElement"/>, 상세는 <see cref="CommitDetailPanel"/>.
    /// 이 클래스는 기간·검색 같은 도구 막대와 데이터 읽기만 맡는다.
    /// </summary>
    public sealed class BranchExplorerView : GitView
    {
        /// <summary>
        /// 한 번에 읽을 커밋 수. 커밋 ~4500·브랜치 3000 저장소에서 읽기~배치 ~0.3초, 그리기 한 장 ~7ms(2026-10-18 실측).
        /// </summary>
        private const int MaxCommits = 5000;
        private static readonly string[] RangeKeys = { "explorer.rangeWeek", "explorer.rangeMonth", "explorer.rangeQuarter", "explorer.rangeAll" };

        private readonly BranchExplorerState _state;
        private readonly ITabNavigator _navigator;
        private readonly ToolbarButton _reloadButton;
        private readonly DropdownField _range;
        private readonly Label _summary;
        private readonly ToolbarSearchField _search;
        private readonly ToolbarButton _nextMatchButton;
        private readonly VisualElement _errorNotice;
        private readonly Label _errorText;
        private readonly VisualElement _emptyNotice;
        private readonly TwoPaneSplitView _split;
        private readonly BranchGraphElement _graph;
        private readonly CommitDetailPanel _detail;
        private readonly CommitActions _commitActions;

        /// <summary>
        /// "비교 기준으로 표시" 한 커밋. 다른 커밋의 메뉴에서 "표시한 커밋과 비교" 로 둘을 비교한다.
        /// </summary>
        private GitCommitInfo _compareBase;
        private string _loadedKey;
        private bool _isLoading;
        private int _searchCursor = -1;

        public override string Title => GitText.Get("tab.explorer");

        private string CurrentKey => $"{Services.Repository.HeadCommit}|{Services.Repository.UpstreamCommit}|{Services.Repository.Branch?.Name}|{Services.Repository.LastFetchTime}|{_state.RangeIndex}";

        public BranchExplorerView(GitServices services, BranchExplorerState state, ITabNavigator navigator) : base(services, "BranchExplorerView")
        {
            _state = state;
            _navigator = navigator;
            _commitActions = new CommitActions(services);

            _reloadButton = Root.Require<ToolbarButton>("reload-button");
            _range = Root.Require<DropdownField>("range");
            _summary = Root.Require<Label>("summary");
            _search = Root.Require<ToolbarSearchField>("search");
            _nextMatchButton = Root.Require<ToolbarButton>("next-match-button");
            _errorNotice = Root.Require<VisualElement>("error-notice");
            _errorText = Root.Require<Label>("error-text");
            _emptyNotice = Root.Require<VisualElement>("empty-notice");
            _split = Root.Require<TwoPaneSplitView>("split");

            _graph = new BranchGraphElement(state.Zoom, state.Pan, services.Avatars);

            ToolbarToggle avatars = Root.Require<ToolbarToggle>("avatars");
            avatars.SetValueWithoutNotify(GitVcsSettings.instance.ShowAvatars);
            avatars.RegisterValueChangedCallback(evt =>
            {
                GitVcsSettings.instance.ShowAvatars = evt.newValue;
                _graph.Redraw();
            });
            Root.Require<VisualElement>("graph-host").Add(_graph);
            _detail = new CommitDetailPanel(Root.Require<VisualElement>("detail-host"), services, closable: true);
            _detail.Hide();
            _split.CollapseChild(1);

            ToolbarIcons.SetRefreshIcon(_reloadButton);
            _reloadButton.clicked += Load;

            _range.choices = RangeKeys.Select(GitText.Get).ToList();
            _range.index = Math.Max(0, Math.Min(state.RangeIndex, RangeKeys.Length - 1));
            _range.RegisterValueChangedCallback(_ =>
            {
                _state.RangeIndex = _range.index;
                ReloadIfNeeded();
            });

            _search.SetValueWithoutNotify(state.Search);
            _graph.SetSearch(state.Search);
            _search.RegisterValueChangedCallback(evt =>
            {
                _state.Search = evt.newValue;
                _searchCursor = -1;
                _graph.SetSearch(evt.newValue);
                _nextMatchButton.SetEnabled(string.IsNullOrWhiteSpace(evt.newValue) == false);
            });
            _nextMatchButton.SetEnabled(string.IsNullOrWhiteSpace(state.Search) == false);
            _nextMatchButton.clicked += FocusNextMatch;

            Root.Require<Button>("head-button").clicked += () => _graph.FocusNode(_graph.FindHead());
            Root.Require<Button>("zoom-in-button").clicked += () => _graph.ZoomBy(1.25f);
            Root.Require<Button>("zoom-out-button").clicked += () => _graph.ZoomBy(1f / 1.25f);

            _graph.NodeSelected += Select;
            _graph.NodeContextMenu += PopulateNodeMenu;
            _graph.ViewportChanged += () =>
            {
                _state.Zoom = _graph.Viewport.Zoom;
                _state.Pan = _graph.Viewport.Pan;
            };
            _detail.Closed += () =>
            {
                _graph.SetSelected(null);
                _split.CollapseChild(1);
            };
        }

        protected override void OnActivated()
        {
            ReloadIfNeeded();
            Render();
        }

        public override void OnRepositoryChanged()
        {
            if (IsActive)
            {
                ReloadIfNeeded();
            }
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
            string key = CurrentKey;
            Render();

            DateTime? since = _state.RangeIndex switch
            {
                0 => DateTime.Now.AddDays(-7),
                1 => DateTime.Now.AddMonths(-1),
                2 => DateTime.Now.AddMonths(-3),
                _ => null,
            };

            (GitGraph graph, string error) = await GraphLoader.LoadAsync(Services.History, Services.Repository.HeadCommit, since, MaxCommits);
            _isLoading = false;
            _loadedKey = key;

            _errorNotice.SetVisible(string.IsNullOrEmpty(error) == false);
            _errorText.text = error ?? string.Empty;

            _graph.SetGraph(graph, GitText.Get("explorer.emptyRange"));
            if (_detail.ShownHash != null && graph.NodeByHash.ContainsKey(_detail.ShownHash) == false)
            {
                _detail.Hide();
                _graph.SetSelected(null);
                _split.CollapseChild(1);
            }

            _summary.text = GitText.Format("explorer.summary", graph.Nodes.Count, graph.Lanes.Count) + (graph.Nodes.Count >= MaxCommits ? GitText.Format("explorer.capped", MaxCommits) : string.Empty);
            Render();
        }

        private void Render()
        {
            bool hasCommits = Services.Repository.HeadCommit != null;
            _emptyNotice.SetVisible(hasCommits == false);
            _split.SetVisible(hasCommits);
            _reloadButton.SetEnabled(_isLoading == false);
            if (_isLoading)
            {
                _summary.text = GitText.Get("common.loading");
            }
        }

        private void Select(GitGraphNode node)
        {
            _graph.SetSelected(node.Commit.Hash);
            _split.UnCollapse();
            if (_detail.ShownHash != node.Commit.Hash)
            {
                _detail.Show(node.Commit, node.Lane.Name);
            }
        }

        /// <summary>
        /// Unity Version Control 의 체인지 세트 메뉴 순서를 따른다(비교 → 만들기·전환 → 병합 → 이동 → 고급). git 에 없는 항목은 뺐다(<see cref="CommitActions"/>).
        /// 오른쪽 클릭이 곧 선택이라 "선택한 두 커밋 비교" 대신 "비교 기준으로 표시" 후 "표시한 커밋과 비교" 로 두 커밋을 고른다.
        /// </summary>
        private void PopulateNodeMenu(DropdownMenu menu, GitGraphNode node)
        {
            GitCommitInfo commit = node.Commit;
            DropdownMenuAction.Status writable = Services.Runner.IsBusy ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            DropdownMenuAction.Status notHead = node.IsHead ? DropdownMenuAction.Status.Disabled : writable;
            bool canCompareWithMarked = _compareBase != null && _compareBase.Hash != commit.Hash;

            menu.AppendAction(GitText.Get("commit.compareWithParent"), _ => _commitActions.CompareWithParent(commit));
            menu.AppendAction(GitText.Get("commit.compareWithRevision"), action => _ = _commitActions.CompareWithRevisionAsync(commit));
            menu.AppendAction(canCompareWithMarked ? GitText.Format("commit.compareWithMarked", _compareBase.ShortHash) : GitText.Get("commit.compareWithMarkedNone"),
                _ => GitCompareWindow.OpenRange(_compareBase.Hash, commit.Hash),
                canCompareWithMarked ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction(GitText.Get("commit.markForCompare"), _ => _compareBase = commit,
                _compareBase?.Hash == commit.Hash ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("commit.createBranch"), action => _ = _commitActions.CreateBranchAsync(commit), writable);
            menu.AppendAction(GitText.Get("commit.tag"), action => _ = _commitActions.CreateTagAsync(commit), writable);
            menu.AppendAction(GitText.Get("commit.switchTo"), action => _ = _commitActions.SwitchToAsync(commit), notHead);
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("commit.merge"), action => _ = _commitActions.MergeAsync(commit, GitMergeMode.Default), notHead);
            menu.AppendAction(GitText.Get("commit.cherryPick"), action => _ = _commitActions.CherryPickAsync(commit), notHead);
            menu.AppendAction(GitText.Get("commit.advancedMerge") + "/" + GitText.Get("commit.mergeNoFf"), action => _ = _commitActions.MergeAsync(commit, GitMergeMode.NoFastForward), notHead);
            menu.AppendAction(GitText.Get("commit.advancedMerge") + "/" + GitText.Get("commit.mergeSquash"), action => _ = _commitActions.MergeAsync(commit, GitMergeMode.Squash), notHead);
            menu.AppendAction(GitText.Get("commit.advancedMerge") + "/" + GitText.Get("commit.revert"), action => _ = _commitActions.RevertAsync(commit), writable);
            menu.AppendSeparator();

            GitGraphNode parent = ParentNodeOf(node);
            menu.AppendAction(GitText.Get("commit.goToParent"), _ =>
            {
                Select(parent);
                _graph.FocusNode(parent);
            }, parent == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            menu.AppendSeparator();

            string advanced = GitText.Get("commit.advanced") + "/";
            string reset = advanced + GitText.Get("commit.resetHere") + "/";
            menu.AppendAction(reset + GitText.Get("commit.resetSoft"), action => _ = _commitActions.ResetAsync(commit, GitResetMode.Soft), notHead);
            menu.AppendAction(reset + GitText.Get("commit.resetMixed"), action => _ = _commitActions.ResetAsync(commit, GitResetMode.Mixed), notHead);
            menu.AppendAction(reset + GitText.Get("commit.resetHard"), action => _ = _commitActions.ResetAsync(commit, GitResetMode.Hard), notHead);
            menu.AppendAction(advanced + GitText.Get("common.copyHash"), _ => Services.Assets.CopyToClipboard(commit.Hash));
            menu.AppendAction(advanced + GitText.Get("common.copySubject"), _ => Services.Assets.CopyToClipboard(commit.Subject));
            menu.AppendAction(advanced + GitText.Get("common.openBranchesTab"), _ => _navigator.SelectTab<BranchesView>());
        }

        /// <summary>
        /// 첫 부모의 노드. 기간 밖이라 그래프에 없으면 null.
        /// </summary>
        private GitGraphNode ParentNodeOf(GitGraphNode node)
        {
            string[] parents = node.Commit.Parents;
            if (parents.Length == 0 || _graph.Graph == null)
            {
                return null;
            }
            return _graph.Graph.NodeByHash.TryGetValue(parents[0], out GitGraphNode parent) ? parent : null;
        }

        /// <summary>최신 커밋부터 거꾸로 돌며 다음 일치 항목으로 이동·선택한다.</summary>
        private void FocusNextMatch()
        {
            GitGraph graph = _graph.Graph;
            if (graph == null || graph.Nodes.Count == 0)
            {
                return;
            }
            int count = graph.Nodes.Count;
            for (int step = 1; step <= count; step++)
            {
                int index = ((_searchCursor < 0 ? count : _searchCursor) - step + count) % count;
                GitGraphNode node = graph.Nodes[index];
                if (_graph.Matches(node))
                {
                    _searchCursor = index;
                    Select(node);
                    _graph.FocusNode(node);
                    return;
                }
            }
            Services.Runner.SetStatus(GitText.Format("explorer.noMatch", _state.Search));
        }
    }
}
