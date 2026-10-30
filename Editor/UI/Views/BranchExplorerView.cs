using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Branch Explorer 탭: Unity Version Control 의 Branch Explorer 처럼 브랜치 흐름을 그래프로 보여 준다.
    /// 그래프 배치는 <see cref="GitGraph"/>, 그리기·조작은 <see cref="BranchGraphElement"/>, 상세는 오른쪽 인스펙터 <see cref="GraphCommitDetail"/>.
    /// 이 클래스는 기간·검색 같은 도구 막대와 데이터 읽기만 맡는다.
    /// </summary>
    public sealed class BranchExplorerView : ExplorerView
    {
        private readonly BranchExplorerState _state;
        private readonly ITabNavigator _navigator;
        private readonly DropdownField _range;
        private readonly Label _summary;
        private readonly ToolbarSearchField _search;
        private readonly ToolbarButton _nextMatchButton;
        private readonly VisualElement _errorNotice;
        private readonly Label _errorText;
        private readonly VisualElement _emptyNotice;
        private readonly TwoPaneSplitView _split;
        private readonly BranchGraphElement _graph;
        /// <summary>
        /// 오른쪽 인스펙터(그래프 탭 오른쪽과 같은 세로형 커밋 상세). 늘 보이고, 고른 게 없으면 안내 문구.
        /// </summary>
        private readonly GraphCommitDetail _detail;
        private readonly CommitMenu _commitMenu;

        /// <summary>
        /// 다른 탭이 보여 달라고 한 커밋. 그래프를 다 읽은 뒤 선택·이동한다(<see cref="Reveal"/>).
        /// </summary>
        private string _pendingReveal;
        private string _loadedKey;

        public override bool HasLoaded => _loadedKey != null && _isLoading == false;

        public override System.Collections.Generic.IEnumerable<(VisualElement Host, SkeletonShape Shape)> SkeletonHosts
        {
            get
            {
                yield return (Root.Require<VisualElement>("graph-host"), SkeletonShape.Commits);
            }
        }
        private bool _isLoading;
        private int _searchCursor = -1;

        private string CurrentKey => $"{Services.Repository.HeadCommit}|{Services.Repository.UpstreamCommit}|{Services.Repository.Branch?.Name}|{Services.Repository.LastFetchTime}|{Services.Repository.RefsFingerprint}|{_state.RangeIndex}";

        public BranchExplorerView(GitServices services, BranchExplorerState state, ITabNavigator navigator) : base(services, "BranchExplorerView")
        {
            _state = state;
            _navigator = navigator;
            _commitMenu = new CommitMenu(services);

            _range = Root.Require<DropdownField>("range");
            _summary = Root.Require<Label>("summary");
            _search = Root.Require<ToolbarSearchField>("search");
            // 테마는 검색창 바깥 상자에 포커스 표시를 하므로 안쪽 입력칸의 포커스를 바깥에 알린다(FocusIn/Out 은 위로 전달된다).
            _search.RegisterCallback<FocusInEvent>(_ => _search.AddToClassList("git-search--focused"));
            _search.RegisterCallback<FocusOutEvent>(_ => _search.RemoveFromClassList("git-search--focused"));
            _nextMatchButton = Root.Require<ToolbarButton>("next-match-button");
            _errorNotice = Root.Require<VisualElement>("error-notice");
            _errorText = Root.Require<Label>("error-text");
            _emptyNotice = Root.Require<VisualElement>("empty-notice");
            _split = Root.Require<TwoPaneSplitView>("split");

            _graph = new BranchGraphElement(state.Zoom, state.Pan, services.Avatars);
            Root.Require<VisualElement>("graph-host").Add(_graph);
            _detail = new GraphCommitDetail(Root.Require<VisualElement>("detail-host"), services);
            // "parent: 해시" 를 누르면 그래프에서 그 커밋으로 가서 고른다.
            _detail.ParentRequested += hash =>
            {
                if (_graph.Graph != null && _graph.Graph.NodeByHash.TryGetValue(hash, out GitGraphNode parent))
                {
                    _graph.FocusNode(parent);
                    Select(parent);
                }
            };

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

            OverlayButton("head-button", ActionGlyphKind.Home).clicked += () => _graph.FocusNode(_graph.FindHead());
            OverlayButton("zoom-in-button", ActionGlyphKind.ZoomIn).clicked += () => _graph.ZoomBy(1.25f);
            OverlayButton("zoom-out-button", ActionGlyphKind.ZoomOut).clicked += () => _graph.ZoomBy(1f / 1.25f);

            _graph.NodeSelected += Select;
            _graph.NodeContextMenu += PopulateNodeMenu;
            _graph.ViewportChanged += () =>
            {
                _state.Zoom = _graph.Viewport.Zoom;
                _state.Pan = _graph.Viewport.Pan;
            };
        }

        protected override void OnActivated()
        {
            ReloadIfNeeded();
            Render();
        }

        public override void Dispose()
        {
            _detail.Dispose();
        }

        public override void OnRepositoryChanged()
        {
            if (IsActive)
            {
                ReloadIfNeeded();
            }
        }

        public override void Reload()
        {
            if (_isLoading == false)
            {
                Load();
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

            (GitGraph graph, string error) = await GraphLoader.LoadAsync(Services.History, Services.Repository.HeadCommit, SinceOf(_state.RangeIndex), MaxCommits);
            _isLoading = false;
            _loadedKey = key;

            _errorNotice.SetVisible(string.IsNullOrEmpty(error) == false);
            _errorText.text = error ?? string.Empty;

            _graph.SetGraph(graph, GitText.Get("explorer.emptyRange"));
            if (_detail.ShownHash != null && graph.NodeByHash.ContainsKey(_detail.ShownHash) == false)
            {
                _detail.ShowPlaceholder();
                _graph.SetSelected(null);
            }

            _summary.text = GitText.Format("explorer.summary", graph.Nodes.Count, graph.Lanes.Count) + (graph.Nodes.Count >= MaxCommits ? GitText.Format("explorer.capped", MaxCommits) : string.Empty);
            Render();
            TryReveal();
        }

        public override void Reveal(string hash)
        {
            _pendingReveal = hash;
            ReloadIfNeeded();
            // 탭을 막 연 경우 그래프 크기가 아직 0 이라 가운데 맞추기가 틀어진다. 배치가 끝난 뒤에 한다.
            Root.schedule.Execute(TryReveal);
        }

        private void TryReveal()
        {
            if (_pendingReveal == null || _isLoading || _graph.Graph == null)
            {
                return;
            }
            if (_graph.Graph.NodeByHash.TryGetValue(_pendingReveal, out GitGraphNode node))
            {
                _pendingReveal = null;
                Select(node);
                _graph.FocusNode(node);
                return;
            }
            if (_state.RangeIndex != AllRangeIndex)
            {
                // 값이 바뀌면 다시 읽고, 다 읽은 뒤 여기로 돌아온다.
                _range.index = AllRangeIndex;
                return;
            }
            // 전체 기간에서도 없으면(읽는 커밋 수 상한 밖) 포기한다.
            _pendingReveal = null;
        }

        private void Render()
        {
            bool hasCommits = Services.Repository.HeadCommit != null;
            _emptyNotice.SetVisible(hasCommits == false);
            _split.SetVisible(hasCommits);
            if (_isLoading)
            {
                _summary.text = GitText.Get("common.loading");
            }
        }

        /// <summary>그래프 오른쪽 아래 떠 있는 버튼에 선 아이콘(ActionGlyph)을 넣는다.</summary>
        private Button OverlayButton(string name, ActionGlyphKind icon)
        {
            Button button = Root.Require<Button>(name);
            ActionGlyph glyph = new(icon);
            glyph.AddToClassList("branch-explorer__overlay-icon");
            button.Add(glyph);
            return button;
        }

        private void Select(GitGraphNode node)
        {
            _graph.SetSelected(node.Commit.Hash);
            if (_detail.ShownHash != node.Commit.Hash)
            {
                _detail.Show(node.Commit);
            }
        }

        private void PopulateNodeMenu(DropdownMenu menu, GitGraphNode node)
        {
            GitGraphNode parent = ParentNodeOf(node);
            _commitMenu.Populate(menu, node.Commit, new CommitMenu.Hooks
            {
                GoToParent = parent == null ? null : () =>
                {
                    Select(parent);
                    _graph.FocusNode(parent);
                },
                Branches = _graph.Graph?.Branches,
            });
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
