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
        private const int MaxCommits = 2000;
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

        private string _loadedKey;
        private bool _isLoading;
        private int _searchCursor = -1;

        public override string Title => GitText.Get("tab.explorer");

        private string CurrentKey => $"{Services.Repository.HeadCommit}|{Services.Repository.UpstreamCommit}|{Services.Repository.Branch?.Name}|{Services.Repository.LastFetchTime}|{_state.RangeIndex}";

        public BranchExplorerView(GitServices services, BranchExplorerState state, ITabNavigator navigator) : base(services, "BranchExplorerView")
        {
            _state = state;
            _navigator = navigator;

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

            List<GitCommitInfo> commits = new();
            string error = null;
            if (Services.Repository.HeadCommit != null)
            {
                (commits, error) = await Services.History.GraphLogAsync(since, MaxCommits);
            }
            (List<GitBranchRef> branches, string branchError) = await Services.History.BranchesAsync();
            _isLoading = false;
            _loadedKey = key;

            _errorNotice.SetVisible(string.IsNullOrEmpty(error ?? branchError) == false);
            _errorText.text = error ?? branchError ?? string.Empty;

            GitGraph graph = GitGraph.Build(commits, branches, Services.Repository.HeadCommit);
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

        private void PopulateNodeMenu(DropdownMenu menu, GitGraphNode node)
        {
            menu.AppendAction(GitText.Get("common.copyHash"), _ => Services.Assets.CopyToClipboard(node.Commit.Hash));
            menu.AppendAction(GitText.Get("common.copySubject"), _ => Services.Assets.CopyToClipboard(node.Commit.Subject));
            menu.AppendSeparator();
            menu.AppendAction(GitText.Get("common.openBranchesTab"), _ => _navigator.SelectTab<BranchesView>());
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
