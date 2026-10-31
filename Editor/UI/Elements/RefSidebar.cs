using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Git 창 왼쪽 아래의 참조 트리(GitKraken·Fork 의 왼쪽 패널): 로컬 · 리모트 · 워크트리 · 스태시 · 서브모듈 · 태그.
    ///
    /// 이 트리는 그래프를 조종한다: 브랜치·태그·스태시를 고르면 <see cref="RevealRequested"/> 로 커밋을 알리고, 창이 보고 있는
    /// 그래프(Graph 또는 Branch Explorer)에서 그 커밋을 보인다. 더블클릭은 기본 동작(체크아웃·변경 보기·폴더 열기),
    /// 우클릭은 종류별 메뉴다 — 브랜치는 <see cref="BranchMenu"/>, 나머지는 <see cref="RefActions"/>.
    /// 펼침 상태와 필터는 <see cref="RefSidebarState"/> 에 남아 창을 다시 열어도 이어진다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed class RefSidebar : IDisposable
    {
        private const string Block = "ref-tree";
        /// <summary>줄 높이. 24 는 밝은 테마에서 줄이 빽빽해 보여 GitKraken 처럼 여유를 둔다.</summary>
        private const float RowHeight = 28f;

        private readonly GitServices _services;
        private readonly RefSidebarState _state;
        private readonly BranchMenu _branchMenu;
        private readonly BranchActions _branchActions;
        private readonly RefActions _actions;
        private readonly ToolbarSearchField _filter;
        private readonly TreeView _tree;
        private readonly Label _error;
        private readonly Dictionary<string, int> _idByKey = new();
        private readonly Dictionary<int, string> _keyById = new();
        private readonly Dictionary<RefSection, int> _sectionCounts = new();
        /// <summary>마지막으로 고른 (구역 머리가 아닌) 줄. 구역 머리를 누르면 이 선택으로 되돌린다.</summary>
        private readonly List<int> _selectedIds = new();

        /// <summary>지금 처리 중인 키보드 이동 방향(↑ -1, ↓ +1, 아니면 0).</summary>
        private int _navigationStep;
        private GitRefSet _refs = new();
        private int _nextId;
        private bool _rebuilding;
        private bool _isLoading;
        private bool _reloadQueued;
        private bool _wasBusy;
        private string _loadedKey;

        /// <summary>
        /// GitHub 에서 읽은 Pull Request 목록. git 참조와 따로 읽어(네트워크) 작업이 끝날 때마다 다시 받지 않는다 —
        /// 처음 열 때, 새로고침(<see cref="Reload"/>), PR 을 만든 뒤(<see cref="ReloadPullRequests"/>), GitHub 원격이 바뀔 때만 다시 받는다.
        /// </summary>
        private GitPullRequestSet _pullRequests;

        private string _pullRequestsKey;
        private int _pullRequestsRequest;

        public VisualElement Root { get; }

        /// <summary>
        /// 브랜치·태그·스태시를 골랐을 때 그 커밋. 창이 지금 그래프에서 보인다.
        /// </summary>
        public event Action<string> RevealRequested;

        /// <summary>
        /// 브랜치 메뉴의 "Branch Explorer 에서 보기".
        /// </summary>
        public event Action<string> ShowInExplorerRequested;

        /// <summary>
        /// 리모트·워크트리·서브모듈 머리 줄의 + 또는 우클릭 "…추가". 창이 그 구역의 서랍(<see cref="SideDrawer"/>)을 연다.
        /// </summary>
        public event Action<RefSection> AddRequested;

        /// <summary>
        /// 브랜치 메뉴의 "Pull Request 만들기…". 창이 Pull Request 서랍을 연다.
        /// </summary>
        public event Action<GitBranchRef> CreatePullRequestRequested;

        /// <summary>서브모듈 줄을 두 번 누르거나 편집… 을 골랐을 때(서브모듈 편집 서랍).</summary>
        public event Action<GitSubmoduleRef> SubmoduleEditRequested;

        /// <summary>PR 필터(묶음)를 새로 만들 서랍을 연다(-1) 또는 그 순번을 고칠 서랍을 연다.</summary>
        public event Action<int> PullRequestFilterRequested;

        /// <summary>
        /// PR 을 눌렀을 때 상세 서랍을 연다: PR, 그 PR 이 있는 GitHub 저장소, 로컬에서 바뀐 것 보기, 보내는 브랜치 체크아웃
        /// (보내는 브랜치를 아직 fetch 하지 않았으면 뒤 둘은 null).
        /// </summary>
        public event Action<GitPullRequestInfo, GitHubRemoteRepo, Action, Action> PullRequestOpened;

        /// <summary>참조(원격 브랜치)를 골라 둔 워크트리 만들기 서랍을 연다(PR 메뉴).</summary>
        public event Action<string> CreateWorktreeRequested;

        /// <summary>Pull Requests 구역 검색 칸의 글.</summary>
        private string _prSearch = string.Empty;

        /// <summary>
        /// 참조를 다시 읽었을 때. 접힌 사이드바(<see cref="SidebarRail"/>)가 구역별 개수(<see cref="SectionCount"/>)를 새로 칠한다.
        /// </summary>
        public event Action Loaded;

        private string CurrentKey => $"{_services.Repository.HeadCommit}|{_services.Repository.UpstreamCommit}|{_services.Repository.Branch?.Name}|{_services.Repository.LastFetchTime}|{_services.Repository.RefsFingerprint}";

        public RefSidebar(VisualElement host, GitServices services, RefSidebarState state)
        {
            _services = services;
            _state = state;
            _branchMenu = new BranchMenu(services);
            _branchActions = new BranchActions(services);
            _actions = new RefActions(services);

            Root = new VisualElement();
            Root.AddToClassList(Block);
            host.Add(Root);

            _filter = new ToolbarSearchField();
            _filter.AddToClassList(Block + "__filter");
            ActionGlyph.ReplaceSearchIcon(_filter);
            // Fork 처럼 비어 있으면 "Filter" 자리표시. 포커스 표시는 바깥 상자에(브랜치 탐색기 검색창과 같다).
            TextField filterInput = _filter.Q<TextField>();
            if (filterInput != null)
            {
                filterInput.textEdition.placeholder = GitText.Get("refs.filterPlaceholder");
                filterInput.textEdition.hidePlaceholderOnFocus = false;
            }
            _filter.RegisterCallback<FocusInEvent>(_ => _filter.AddToClassList("git-search--focused"));
            _filter.RegisterCallback<FocusOutEvent>(_ => _filter.RemoveFromClassList("git-search--focused"));
            _filter.SetValueWithoutNotify(state.Filter ?? string.Empty);
            _filter.RegisterValueChangedCallback(evt =>
            {
                _state.Filter = evt.newValue;
                Rebuild();
            });
            Root.Add(_filter);

            _error = new Label();
            _error.AddToClassList(Block + "__error");
            _error.SetVisible(false);
            Root.Add(_error);

            _tree = new TreeView
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            _tree.AddToClassList(Block + "__tree");
            _tree.selectionChanged += selected =>
            {
                if (selected.FirstOrDefault() is not RefNode node)
                {
                    return;
                }
                // 구역 머리 줄(로컬·리모트…)은 고르는 줄이 아니다: 고른 줄을 그대로 두고, 누르면 접고 펼친다(MakeRow 의 ClickEvent).
                // PR 검색 줄도 고르는 줄이 아니다. 키보드 ↑↓ 로 닿았으면 그 방향의 다음 줄로 건너뛴다(되돌리기만 하면 구역을 넘어가지 못한다).
                if (node.Kind is RefNodeKind.Section or RefNodeKind.PullRequestSearch)
                {
                    if (_navigationStep == 0 || SelectNextSelectable(_tree.selectedIndex, _navigationStep) == false)
                    {
                        _tree.SetSelectionByIdWithoutNotify(_selectedIds);
                    }
                    return;
                }
                _selectedIds.Clear();
                _selectedIds.AddRange(_tree.selectedIds);
                if (node.Commit != null)
                {
                    RevealRequested?.Invoke(node.Commit);
                }
                // GitKraken 처럼 PR 을 누르면 PR 화면(상세 서랍)을 연다.
                if (node.Kind == RefNodeKind.PullRequest)
                {
                    OpenPullRequest(node.PullRequest);
                }
            };
            _tree.itemsChosen += chosen =>
            {
                if (chosen.FirstOrDefault() is RefNode node)
                {
                    RunDefault(node);
                }
            };
            _tree.itemExpandedChanged += OnExpandedChanged;
            // ↑↓ 로 고를 때 그 방향을 기억한다(이 이벤트를 처리하는 동안 고른 줄이 바뀐다). 다음 프레임에 지운다.
            _tree.RegisterCallback<NavigationMoveEvent>(evt =>
            {
                _navigationStep = evt.direction switch
                {
                    NavigationMoveEvent.Direction.Up => -1,
                    NavigationMoveEvent.Direction.Down => 1,
                    _ => 0,
                };
                _tree.schedule.Execute(() => _navigationStep = 0);
            }, TrickleDown.TrickleDown);
            // 처음 읽는 동안은 트리 대신 같은 모양의 스켈레톤(위에서 아래로 물결)을 보이고, 다 읽으면 트리가 서서히 나타난다(ShowTree).
            _skeleton = BuildSkeleton();
            Root.Add(_skeleton);
            // 빨리 읽히면 스켈레톤을 아예 보이지 않는다(잔상 방지).
            GitSkeleton.AppearLater(_skeleton);
            _tree.style.display = DisplayStyle.None;
            Root.Add(_tree);

            _services.Runner.StateChanged += OnRunnerStateChanged;
            GitPullRequestFilters.Changed += OnPullRequestFiltersChanged;
        }

        public void Dispose()
        {
            _services.Runner.StateChanged -= OnRunnerStateChanged;
            GitPullRequestFilters.Changed -= OnPullRequestFiltersChanged;
        }

        public void OnRepositoryChanged()
        {
            if (_loadedKey != CurrentKey)
            {
                Load();
            }
        }

        public void Reload()
        {
            _pullRequestsKey = null;
            Load();
        }

        /// <summary>
        /// Pull Request 목록만 다시 받는다(PR 을 만든 뒤).
        /// </summary>
        /// <summary>마지막으로 읽은 풀 리퀘스트(읽기 전이면 null). <see cref="Loaded"/> 때마다 바뀐다.</summary>
        public GitPullRequestSet PullRequests => _pullRequests;

        public void ReloadPullRequests()
        {
            _pullRequestsKey = null;
            LoadPullRequests();
        }

        /// <summary>
        /// 도구 막대의 브랜치 버튼: 로컬 구역을 펼치고 필터에 포커스.
        /// </summary>
        public void FocusFilter()
        {
            if (_idByKey.TryGetValue("section:" + RefSection.Local, out int id))
            {
                _tree.ExpandItem(id);
            }
            _filter.Focus();
        }

        /// <summary>
        /// 구역의 항목 수(필터와 상관없이). 트리 구역 머리 줄의 개수와 같다.
        /// </summary>
        public int SectionCount(RefSection section)
        {
            return _sectionCounts.TryGetValue(section, out int count) ? count : 0;
        }

        /// <summary>
        /// 이 저장소에서 보이는 구역인지(<see cref="RefTreeBuilder.IsShown"/>).
        /// </summary>
        public bool IsSectionShown(RefSection section)
        {
            return RefTreeBuilder.IsShown(section, _refs);
        }

        /// <summary>
        /// 접힌 사이드바에서 구역 아이콘을 눌렀을 때: 그 구역을 펼치고 머리 줄로 스크롤한다.
        /// </summary>
        public void RevealSection(RefSection section)
        {
            if (_idByKey.TryGetValue("section:" + section, out int id) == false)
            {
                return;
            }
            _tree.ExpandItem(id);
            int index = _tree.viewController.GetIndexForId(id);
            if (index >= 0)
            {
                _tree.ScrollToItem(index);
            }
        }

        /// <summary>
        /// 태그·스태시·브랜치를 지우거나 만드는 작업은 HEAD 를 바꾸지 않아 키로는 알 수 없다. 작업이 끝날 때마다 다시 읽는다.
        /// </summary>
        private void OnRunnerStateChanged()
        {
            bool isBusy = _services.Runner.IsBusy;
            if (_wasBusy && isBusy == false)
            {
                Load();
            }
            _wasBusy = isBusy;
        }

        private async void Load()
        {
            if (_isLoading)
            {
                _reloadQueued = true;
                return;
            }
            _isLoading = true;
            string key = CurrentKey;
            GitRefSet refs = await _services.History.RefsAsync();
            _isLoading = false;
            _loadedKey = key;
            _refs = refs;
            refs.PullRequests = _pullRequests;
            _error.text = refs.Error ?? string.Empty;
            _error.SetVisible(string.IsNullOrEmpty(refs.Error) == false);
            UpdateCounts();
            Rebuild();
            if (HoldReveal == false)
            {
                ShowTree();
            }
            Loaded?.Invoke();
            LoadPullRequests();
            if (_reloadQueued)
            {
                _reloadQueued = false;
                Load();
            }
        }

        private VisualElement _skeleton;

        /// <summary>
        /// 지금 저장소 상태로 다 읽었는지(트리를 보일 수 있는지). 에디터를 막 켜거나 컴파일한 직후에는 git 연동이 준비되기 전에 빈 참조로 한 번 읽기를
        /// 마치는데, 그때 걷으면 빈 트리만 1초쯤 보인다 — 읽은 때의 상태(_loadedKey)가 지금 상태와 같을 때만 다 읽은 것으로 본다.
        /// </summary>
        public bool HasLoaded => _refs != null && _isLoading == false && _loadedKey == CurrentKey;

        /// <summary>
        /// 참이면 다 읽어도 스켈레톤을 걷지 않는다 — 창이 다른 칸과 한꺼번에 걷으려고(<see cref="Reveal"/>).
        /// </summary>
        public bool HoldReveal { get; set; }

        /// <summary>스켈레톤을 걷고 트리를 보인다(처음 한 번만 효과가 있다).</summary>
        public void Reveal()
        {
            HoldReveal = false;
            if (HasLoaded)
            {
                ShowTree();
            }
        }

        /// <summary>스켈레톤이 걷힐 때 트리가 나타나는 시간(ms).</summary>
        private const int TreeFadeMs = 220;

        /// <summary>
        /// 스켈레톤 줄: (들여쓰기 단계, 이름 막대 폭). 단계 -1 은 구역 머리 줄(접기 화살표·아이콘·이름·오른쪽 개수).
        /// 실제 트리처럼 로컬·리모트를 펼친 모양과 아래 접힌 구역들을 흉내 낸다.
        /// </summary>
        private static readonly (int Depth, float Width)[] SkeletonRows =
        {
            (-1, 46), (1, 58), (2, 70), (2, 82), (1, 54),
            (-1, 52), (1, 50), (2, 56), (3, 76), (3, 72), (3, 88), (2, 40), (3, 60), (2, 62), (1, 54),
            (-1, 56), (-1, 44), (-1, 40), (-1, 60), (-1, 84),
        };

        /// <summary>구역 머리 줄의 아이콘 x 와 단계마다 들여 쓰는 폭(트리의 실제 배치와 맞춘다 — .ref-tree__tree 의 줄 여백·펼침 화살표·TreeView 들여쓰기).</summary>
        private const float SkeletonIconX = 27f;
        private const float SkeletonIndent = 21f;

        private static VisualElement BuildSkeleton()
        {
            VisualElement group = new() { pickingMode = PickingMode.Ignore };
            group.AddToClassList(Block + "__skeleton");
            foreach ((int depth, float width) in SkeletonRows)
            {
                VisualElement row = GitSkeleton.Row(group);
                row.AddToClassList(Block + "__skeleton-row");
                if (depth < 0)
                {
                    row.AddToClassList(Block + "__skeleton-row--section");
                    GitSkeleton.Bar(row, 8, 8).AddToClassList(Block + "__skeleton-chevron");
                    GitSkeleton.Bar(row, 14, 14).AddToClassList(Block + "__skeleton-icon");
                    GitSkeleton.Bar(row, width, 11);
                    GitSkeleton.Bar(row, 10, 11).AddToClassList(Block + "__skeleton-count");
                    continue;
                }
                row.style.paddingLeft = SkeletonIconX + depth * SkeletonIndent;
                GitSkeleton.Bar(row, 14, 14).AddToClassList(Block + "__skeleton-icon");
                GitSkeleton.Bar(row, width, 10);
            }
            GitSkeleton.Pulse(group);
            return group;
        }

        /// <summary>
        /// 처음 다 읽었을 때: 스켈레톤을 걷고 트리를 보인다. 스켈레톤이 아직 나타나지 않았으면(빨리 읽혔으면) 트리를 곧바로,
        /// 나타났으면 최소 표시 시간을 채운 뒤 트리를 투명에서 서서히 나타낸다. 그 뒤 다시 읽을 때는 트리를 그대로 둔다.
        /// </summary>
        private void ShowTree()
        {
            VisualElement skeleton = _skeleton;
            if (skeleton == null)
            {
                return;
            }
            _skeleton = null;
            if (GitSkeleton.WasShown(skeleton) == false)
            {
                skeleton.RemoveFromHierarchy();
                _tree.style.display = DisplayStyle.Flex;
                return;
            }
            // 예약은 보이는 사이드바 루트에 건다 — 숨겨 둔(display: none) 트리에 걸면 Unity 6.0 은 실행하지 않아 스켈레톤이 남았다.
            Root.schedule.Execute(() => SwapToTree(skeleton)).StartingIn(GitSkeleton.RemainingShowMs(skeleton));
        }

        private void SwapToTree(VisualElement skeleton)
        {
            skeleton.RemoveFromHierarchy();
            _tree.style.display = DisplayStyle.Flex;
            _tree.style.opacity = 0f;
            _tree.schedule.Execute(() =>
            {
                _tree.style.transitionProperty = new List<StylePropertyName> { "opacity" };
                _tree.style.transitionDuration = new List<TimeValue> { new(TreeFadeMs, TimeUnit.Millisecond) };
                _tree.style.opacity = 1f;
            }).StartingIn(16);
            _tree.schedule.Execute(() =>
            {
                _tree.style.transitionProperty = StyleKeyword.Null;
                _tree.style.transitionDuration = StyleKeyword.Null;
                _tree.style.opacity = StyleKeyword.Null;
            }).StartingIn(16 + TreeFadeMs + 60);
        }

        private void UpdateCounts()
        {
            _sectionCounts.Clear();
            foreach (RefNode section in RefTreeBuilder.Build(_refs, null, _ => string.Empty))
            {
                _sectionCounts[section.Section] = section.Section == RefSection.PullRequests ? RefTreeBuilder.PullRequestCount(_refs.PullRequests) : section.LeafCount;
            }
        }

        /// <summary>
        /// 받을 저장소(upstream 원격이 있으면 그쪽, 없으면 origin)의 열린 PR 을 읽는다. 로그인·원격이 그대로면 다시 받지 않는다.
        /// </summary>
        private async void LoadPullRequests()
        {
            List<GitHubRemoteRepo> remotes = GitPullRequestForm.GitHubRemotes(_refs.RemoteUrls);
            GitHubRemoteRepo target = remotes.FirstOrDefault(r => r.Remote == "upstream") ?? remotes.FirstOrDefault();
            // Fetch 할 때마다(수동·자동) 다시 받는다 — GitHub 에서 병합·닫힌 PR 이 목록에 남지 않게.
            string key = $"{GitHubAccount.Login}|{target?.FullName}|{_services.Repository.LastFetchTime}";
            if (key == _pullRequestsKey)
            {
                return;
            }
            _pullRequestsKey = key;
            int request = ++_pullRequestsRequest;
            if (GitHubAccount.IsSignedIn == false || target == null)
            {
                ApplyPullRequests(new GitPullRequestSet { NeedsSignIn = GitHubAccount.IsSignedIn == false, NoGitHubRemote = target == null });
                return;
            }
            // 처음 읽을 때만 "읽는 중" 을 보인다. 다시 읽을 때는 받을 때까지 지금 목록을 그대로 둔다.
            if (_pullRequests == null || _pullRequests.Repository != target.FullName)
            {
                ApplyPullRequests(new GitPullRequestSet { IsLoading = true, Repository = target.FullName });
            }
            (List<GitPullRequestInfo> pulls, string error) = await GitHubPullRequests.ListOpenAsync(target);
            if (request != _pullRequestsRequest)
            {
                return;
            }
            if (error != null)
            {
                // 다음 새로고침 때 다시 시도한다.
                _pullRequestsKey = null;
            }
            GitPullRequestSet set = new() { Repository = target.FullName, Remote = target.Remote, Login = GitHubAccount.Login, All = pulls, Error = error };
            ApplyPullRequests(set);
            if (error == null)
            {
                await SearchFiltersAsync(set, target, request);
            }
        }

        /// <summary>
        /// 받은 PR 정보로 풀 수 없는 필터 조건(review:·status:·created: …)을 GitHub 검색으로 푼다. 받는 대로 다시 그린다.
        /// </summary>
        private async Task SearchFiltersAsync(GitPullRequestSet set, GitHubRemoteRepo target, int request)
        {
            foreach (string query in GitPullRequestFilters.Load().Select(f => f.Query ?? string.Empty).Distinct())
            {
                if (GitPullRequestFilters.NeedsSearch(query) == false || set.SearchResults.ContainsKey(query))
                {
                    continue;
                }
                (HashSet<int> numbers, string error) = await GitHubPullRequests.SearchNumbersAsync(target, query);
                if (request != _pullRequestsRequest || _pullRequests != set)
                {
                    return;
                }
                if (error == null)
                {
                    set.SearchResults[query] = numbers;
                    Rebuild();
                }
            }
        }

        private void OnPullRequestFiltersChanged()
        {
            Rebuild();
            if (_pullRequests != null && _pullRequests.Error == null && _pullRequests.IsLoading == false)
            {
                List<GitHubRemoteRepo> remotes = GitPullRequestForm.GitHubRemotes(_refs.RemoteUrls);
                GitHubRemoteRepo target = remotes.FirstOrDefault(r => r.Remote == _pullRequests.Remote) ?? remotes.FirstOrDefault();
                if (target != null)
                {
                    _ = SearchFiltersAsync(_pullRequests, target, _pullRequestsRequest);
                }
            }
        }

        private void ApplyPullRequests(GitPullRequestSet set)
        {
            _pullRequests = set;
            _refs.PullRequests = set;
            UpdateCounts();
            Rebuild();
            Loaded?.Invoke();
        }

        private void Rebuild()
        {
            List<RefNode> roots = RefTreeBuilder.Build(_refs, _state.Filter, section => GitText.Get("refs.section" + section), GitText.Get, _prSearch);
            List<int> selected = _tree.selectedIds.ToList();
            _rebuilding = true;
            _tree.SetRootItems(roots.Select(ToItemData).ToList());
            _tree.Rebuild();
            bool filtering = string.IsNullOrWhiteSpace(_state.Filter) == false;
            foreach (RefNode node in Containers(roots))
            {
                int id = _idByKey[node.Id];
                if (filtering || KeepsOpen(node) || IsExpanded(node.Id))
                {
                    _tree.ExpandItem(id);
                }
                else
                {
                    _tree.CollapseItem(id);
                }
            }
            _tree.SetSelectionByIdWithoutNotify(selected.Where(id => _tree.viewController.GetIndexForId(id) >= 0));
            _rebuilding = false;
        }

        private static IEnumerable<RefNode> Containers(IEnumerable<RefNode> nodes)
        {
            foreach (RefNode node in nodes)
            {
                if (node.IsContainer)
                {
                    yield return node;
                    foreach (RefNode child in Containers(node.Children))
                    {
                        yield return child;
                    }
                }
            }
        }

        /// <summary>
        /// GitKraken 처럼 구역 머리 줄과 PR 묶음만 접는다. 폴더·원격은 늘 펼쳐 둔다.
        /// </summary>
        /// <summary>
        /// from 에서 step 방향으로 처음 만나는 고를 수 있는 줄(구역 머리·PR 검색 줄이 아닌 줄)을 고른다. 없으면 false.
        /// </summary>
        private bool SelectNextSelectable(int from, int step)
        {
            int count = _tree.viewController.itemsSource.Count;
            for (int index = from + step; index >= 0 && index < count; index += step)
            {
                if (_tree.GetItemDataForIndex<RefNode>(index) is { Kind: not (RefNodeKind.Section or RefNodeKind.PullRequestSearch) })
                {
                    _tree.SetSelection(index);
                    _tree.ScrollToItem(index);
                    return true;
                }
            }
            return false;
        }

        private static bool IsAlwaysOpen(RefNode node)
        {
            return node.Kind is RefNodeKind.Folder or RefNodeKind.Remote;
        }

        /// <summary>
        /// 아이가 없는 담는 줄(빈 스태시·서브모듈 구역 등). 화살표를 펼친 모양(⌄)으로 그리므로 트리 안의 펼침 상태도 늘 펼침으로 둔다 —
        /// 접힘으로 바뀌면 TreeView 가 다음에 줄을 다시 그릴 때 화살표를 > 로 되돌려, 누른 적 없는 다른 빈 구역까지 모양이 뒤섞였다.
        /// </summary>
        private static bool IsEmptyContainer(RefNode node)
        {
            return node.IsContainer && node.Children.Count == 0;
        }

        private static bool KeepsOpen(RefNode node)
        {
            return IsAlwaysOpen(node) || IsEmptyContainer(node);
        }

        private bool IsExpanded(string key)
        {
            return RefTreeBuilder.ExpandedByDefault(key) != _state.Toggled.Contains(key);
        }

        private void OnExpandedChanged(TreeViewExpansionChangedArgs args)
        {
            // 폴더·원격·빈 담는 줄은 키보드(←)나 Alt+클릭으로 접혀도 다시 펼친다.
            if (_rebuilding == false && args.isExpanded == false && _tree.GetItemDataForId<RefNode>(args.id) is { } folded && KeepsOpen(folded))
            {
                int id = args.id;
                _tree.schedule.Execute(() => _tree.ExpandItem(id));
                return;
            }
            // 필터 중에는 모두 펼쳐 보이므로 사용자의 펼침 기록을 바꾸지 않는다.
            if (_rebuilding || string.IsNullOrWhiteSpace(_state.Filter) == false || _keyById.TryGetValue(args.id, out string key) == false)
            {
                return;
            }
            bool toggled = args.isExpanded != RefTreeBuilder.ExpandedByDefault(key);
            if (toggled && _state.Toggled.Contains(key) == false)
            {
                _state.Toggled.Add(key);
            }
            else if (toggled == false)
            {
                _state.Toggled.Remove(key);
            }
        }

        private TreeViewItemData<RefNode> ToItemData(RefNode node)
        {
            if (_idByKey.TryGetValue(node.Id, out int id) == false)
            {
                id = ++_nextId;
                _idByKey[node.Id] = id;
                _keyById[id] = node.Id;
            }
            List<TreeViewItemData<RefNode>> children = node.Children.Count == 0 ? null : node.Children.Select(ToItemData).ToList();
            return new TreeViewItemData<RefNode>(id, node, children);
        }

        private VisualElement MakeRow()
        {
            VisualElement row = new();
            row.AddToClassList(Block + "__row");
            VisualElement icon = new();
            icon.AddToClassList(Block + "__icon");
            row.Add(icon);
            Label label = new();
            label.AddToClassList(Block + "__label");
            label.AddToClassList("ellipsis");
            row.Add(label);
            Label badge = new();
            badge.AddToClassList(Block + "__badge");
            row.Add(badge);
            // GitKraken 처럼 리모트·워크트리·서브모듈 머리 줄에 마우스를 올리면 개수 자리가 초록 + 로 바로 바뀐다(USS :hover, 움직임 없음).
            // 누르는 순간 멈춰 줄 선택·펼침으로 넘어가지 않게 한다. 줄은 재사용되므로 어느 구역인지는 BindRow 가 userData 에 적는다.
            Button add = new();
            add.clicked += () =>
            {
                if (add.userData is RefSection section)
                {
                    AddRequested?.Invoke(section);
                }
            };
            add.text = "+";
            add.AddToClassList(Block + "__add");
            add.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            // 기본 tooltip 대신 GitKraken 처럼 버튼 오른쪽에 바로 뜨는 말풍선으로 무엇을 하는지 알린다.
            HoverCallout.Attach(add, () => add.userData is RefSection section ? AddHintKey(section) is { } key ? GitText.Get(key) : null : null,
                () => _services.PopupHost);
            row.Add(add);
            // 로컬·리모트 브랜치 줄 끝의 ⋮: 우클릭과 같은 메뉴를 GitKraken 처럼 버튼 오른쪽 위를 기점으로 연다.
            // 떼는 순간(클릭) 열어야 그 떼기가 막 뜬 메뉴 줄을 고르지 않는다.
            VisualElement more = new();
            more.AddToClassList(Block + "__more");
            more.Add(new ActionGlyph(ActionGlyphKind.MoreVert));
            more.AddManipulator(new Clickable(() =>
            {
                if (row.userData is int index)
                {
                    _tree.SetSelectionWithoutNotify(new[] { index });
                    DropdownMenu menu = new();
                    PopulateMenu(menu, _tree.GetItemDataForIndex<RefNode>(index));
                    GitContextMenu.ShowAt(_services.PopupHost, more, menu);
                }
            }));
            more.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            HoverCallout.Attach(more, () => GitText.Get("refs.branchMenuHint"), () => _services.PopupHost, HoverCalloutPlacement.Below);
            row.Add(more);
            // 줄에 마우스를 올리면 GitKraken 처럼 줄 아래에 전체 이름(sample/main, origin/main)이나 워크트리 폴더를 바로 띄운다.
            HoverCallout.Attach(row, () => row.userData is int index ? HoverText(_tree.GetItemDataForIndex<RefNode>(index)) : null,
                () => _services.PopupHost, HoverCalloutPlacement.Below);
            // 구역 머리 줄은 GitKraken 처럼 줄 어디를 눌러도 접고 펼친다. 줄 안의 버튼(+ 등)을 누른 것은 빼고 — 그 클릭도 줄로 올라온다.
            row.RegisterCallback<ClickEvent>(evt =>
            {
                bool onButton = evt.target is VisualElement target && (target is Button || target.GetFirstAncestorOfType<Button>() is { } button && row.Contains(button));
                if (evt.button != 0 || onButton || row.userData is not int index || _tree.GetItemDataForIndex<RefNode>(index) is not { Kind: RefNodeKind.Section } section || IsEmptyContainer(section))
                {
                    return;
                }
                int id = _tree.GetIdForIndex(index);
                if (_tree.IsExpanded(id))
                {
                    _tree.CollapseItem(id);
                }
                else
                {
                    _tree.ExpandItem(id);
                }
            });
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (row.userData is int index)
                {
                    _tree.SetSelectionWithoutNotify(new[] { index });
                    PopulateMenu(evt.menu, _tree.GetItemDataForIndex<RefNode>(index));
                }
            }));
            // Pull Requests 구역 맨 위 줄: GitKraken 처럼 PR 검색 칸과 필터 추가(깔때기+) 버튼. 그 줄에서만 보인다.
            VisualElement search = new();
            search.AddToClassList(Block + "__pr-search");
            // 위 필터·커밋 검색과 같은 검색창(지우기·바깥 상자 포커스 테두리)을 쓴다. 돋보기는 USS 가 숨긴다.
            ToolbarSearchField searchField = new();
            searchField.AddToClassList(Block + "__pr-search-field");
            TextField searchInput = searchField.Q<TextField>();
            if (searchInput != null)
            {
                searchInput.textEdition.placeholder = GitText.Get("prList.searchPlaceholder");
                searchInput.textEdition.hidePlaceholderOnFocus = false;
            }
            searchField.RegisterCallback<FocusInEvent>(_ => searchField.AddToClassList("git-search--focused"));
            searchField.RegisterCallback<FocusOutEvent>(_ => searchField.RemoveFromClassList("git-search--focused"));
            searchField.RegisterValueChangedCallback(evt =>
            {
                _prSearch = evt.newValue ?? string.Empty;
                RebuildForPrSearch();
            });
            // 트리의 화살표·Enter 처리로 넘어가지 않게 한다(글을 쓰는 칸이다).
            searchField.RegisterCallback<KeyDownEvent>(evt => evt.StopPropagation());
            searchField.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            searchField.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            search.Add(searchField);
            Button addFilter = new(() => PullRequestFilterRequested?.Invoke(-1));
            addFilter.AddToClassList(Block + "__pr-filter-add");
            addFilter.Add(new ActionGlyph(ActionGlyphKind.FilterAdd));
            addFilter.tooltip = GitText.Get("prFilter.createTitle");
            addFilter.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            search.Add(addFilter);
            search.SetVisible(false);
            row.Add(search);
            // 서브모듈이 저장소에 기록된 커밋과 맞으면 GitKraken 처럼 아이콘 왼쪽(펼침 화살표 자리)에 초록 체크. 앞 칸 번호가 그대로이도록 맨 뒤에 둔다.
            ActionGlyph synced = new(ActionGlyphKind.Check) { pickingMode = PickingMode.Ignore };
            synced.AddToClassList(Block + "__synced");
            row.Add(synced);
            return row;
        }

        /// <summary>검색 칸에 쓰는 중에 트리를 다시 그렸으면 다시 그린 칸에 포커스를 돌려준다.</summary>
        private bool _refocusPrSearch;

        /// <summary>
        /// IME 가 조합 중인 글자(없으면 빈 문자열). GUIUtility.compositionString 은 internal 이라 리플렉션으로 읽는다.
        /// Input.compositionString 은 새 Input System 만 켠 프로젝트에서 예외를 던져 쓰지 않는다. 못 찾으면 늘 빈 문자열(= 바로 다시 그림).
        /// </summary>
        private static readonly System.Reflection.PropertyInfo CompositionProperty = typeof(GUIUtility).GetProperty("compositionString",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        private static string ImeComposition => CompositionProperty?.GetValue(null) as string;

        /// <summary>한글 IME 조합이 끝나기를 기다리는 동안 도는 확인(<see cref="RebuildForPrSearch"/>).</summary>
        private IVisualElementScheduledItem _awaitComposition;

        /// <summary>
        /// PR 검색어가 바뀌면 트리를 다시 그린다. 다시 그리면 줄 요소가 새로 묶여 검색 칸도 다른 요소로 바뀌는데,
        /// 한글 IME 가 글자를 조합하는 중이면 조합이 끊겨 "내" 가 "ㄴ내" 가 된다 — 그래서 조합 중에는 미루고 조합이 끝나면(글자 확정) 한 번 그린다.
        /// </summary>
        private void RebuildForPrSearch()
        {
            if (string.IsNullOrEmpty(ImeComposition) == false)
            {
                _awaitComposition ??= Root.schedule.Execute(() =>
                {
                    if (string.IsNullOrEmpty(ImeComposition))
                    {
                        _awaitComposition.Pause();
                        RebuildForPrSearch();
                    }
                }).Every(100);
                _awaitComposition.Resume();
                return;
            }
            _awaitComposition?.Pause();
            // 다시 그리면 이 칸이 다른 줄 요소로 바뀔 수 있으니 포커스를 되찾을 수 있게 표시해 둔다.
            _refocusPrSearch = true;
            Rebuild();
        }

        private void BindRow(VisualElement row, int index)
        {
            row.userData = index;
            RefNode node = _tree.GetItemDataForIndex<RefNode>(index);
            VisualElement icon = row[0];
            Label label = (Label)row[1];
            Label badge = (Label)row[2];

            ActionGlyphKind kind = IconOf(node);
            if (icon.userData is not ActionGlyphKind shown || shown != kind)
            {
                icon.Clear();
                icon.Add(new ActionGlyph(kind));
                icon.userData = kind;
            }
            label.text = GitDisplay.Nfc(node.Label);
            badge.text = node.Badge;
            bool isSection = node.Kind == RefNodeKind.Section;
            row.EnableInClassList(Block + "__row--section", isSection);
            // PR 묶음 줄과 안내 줄은 GitKraken 처럼 아이콘 없이 글자만.
            row.EnableInClassList(Block + "__row--plain", node.Kind is RefNodeKind.PullRequestGroup or RefNodeKind.Info);
            row.EnableInClassList(Block + "__row--info", node.Kind == RefNodeKind.Info);
            string addKey = isSection ? AddHintKey(node.Section) : null;
            row.EnableInClassList(Block + "__row--addable", addKey != null);
            if (addKey != null)
            {
                row[3].userData = node.Section;
            }
            // GitKraken 처럼 브랜치·PR 묶음(필터)·PR 줄 끝에 ⋮ 메뉴.
            row.EnableInClassList(Block + "__row--menu", node.Kind is RefNodeKind.Branch or RefNodeKind.PullRequestGroup or RefNodeKind.PullRequest);
            bool isSearch = node.Kind == RefNodeKind.PullRequestSearch;
            row.EnableInClassList(Block + "__row--pr-search", isSearch);
            // GitKraken 처럼 검색 칸은 아래 자식 줄의 펼침 화살표 위치부터 시작한다: 이 줄만 트리의 화살표 자리를 숨긴다.
            // (줄 요소는 다시 쓰이므로 다른 줄에서는 반드시 되돌린다.)
            row.parent?.parent?.EnableInClassList(Block + "__item--pr-search", isSearch);
            VisualElement searchRow = row[5];
            searchRow.SetVisible(isSearch);
            // 검색 줄에서만 끈다. 다른 줄은 인라인 값을 지워 USS(묶음·안내 줄은 아이콘 없음)가 정하게 둔다.
            StyleEnum<DisplayStyle> hidden = isSearch ? DisplayStyle.None : StyleKeyword.Null;
            icon.style.display = hidden;
            label.style.display = hidden;
            badge.style.display = hidden;
            if (isSearch)
            {
                // 쓰는 중인 칸은 건드리지 않는다: 값을 다시 넣거나 포커스·커서를 다시 잡으면 한글 IME 가 조합 중인 글자를 끊는다
                // (글자마다 트리를 다시 그리므로 매번 일어난다). 칸이 다른 줄 요소로 바뀌어 포커스를 잃었을 때만 되찾는다.
                ToolbarSearchField box = searchRow.Q<ToolbarSearchField>();
                if (box.value != _prSearch)
                {
                    box.SetValueWithoutNotify(_prSearch);
                }
                bool refocus = _refocusPrSearch && box.ClassListContains("git-search--focused") == false;
                _refocusPrSearch = false;
                if (refocus)
                {
                    TextField field = searchRow.Q<TextField>();
                    field.schedule.Execute(() =>
                    {
                        field.Focus();
                        field.SelectRange(field.value.Length, field.value.Length);
                    });
                }
            }
            row.EnableInClassList(Block + "__row--current", node.IsCurrent);
            row.EnableInClassList(Block + "__row--synced", node.Kind == RefNodeKind.Submodule && node.Submodule?.State == GitSubmoduleState.UpToDate);
            // 펼침 화살표는 row 바깥(TreeView 항목)에 있어, 손가락 커서와 화살표 숨김은 항목에 단다. 줄은 재사용되니 매번 다시 정한다.
            // 폴더·원격은 늘 펼쳐 두므로 화살표를 감추고 누를 수 없게 한다(자리는 남겨 형제 브랜치와 들여쓰기를 맞춘다).
            bool isFixed = IsAlwaysOpen(node);
            // 담는 줄(구역·PR 묶음·원격)은 비어 있어도 GitKraken 처럼 화살표를 그린다 — 0 인 줄만 화살표가 빠져 들쭉날쭉하지 않게.
            // TreeView 는 아이가 없는 줄의 화살표를 직접(인라인 visibility) 숨기므로 그 뒤에 덮어쓴다. 빈 줄은 펼친 모양(⌄)이고 누를 것이 없다.
            bool isEmptyContainer = IsEmptyContainer(node);
            for (VisualElement item = row.parent; item != null; item = item.parent)
            {
                if (item.ClassListContains(TreeView.itemUssClassName))
                {
                    item.EnableInClassList(Block + "__item--section", isSection);
                    // 구역 사이 가로 구분선. 맨 위 구역 위에는 긋지 않는다(USS 에 :first-child 가 없어 줄 번호로 가린다).
                    item.EnableInClassList(Block + "__item--section-divided", isSection && index > 0);
                    item.EnableInClassList(Block + "__item--fixed", isFixed);
                    item.EnableInClassList(Block + "__item--current", node.IsCurrent);
                    VisualElement toggleElement = item.Q(className: TreeView.itemToggleUssClassName);
                    if (toggleElement != null)
                    {
                        toggleElement.SetEnabled(isFixed == false);
                        // 안쪽(화살표 그림)까지 막는다 — 바깥만 막으면 화살표 그림이 클릭을 받아 값이 접힘(>)으로 바뀐다.
                        PickingMode picking = isEmptyContainer ? PickingMode.Ignore : PickingMode.Position;
                        toggleElement.Query().ForEach(part => part.pickingMode = picking);
                        if (isEmptyContainer)
                        {
                            toggleElement.style.visibility = Visibility.Visible;
                            (toggleElement as Toggle)?.SetValueWithoutNotify(true);
                        }
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// 구역 머리 줄 아이콘. 접힌 사이드바(<see cref="SidebarRail"/>)도 같은 아이콘을 쓴다.
        /// </summary>
        public static ActionGlyphKind SectionIcon(RefSection section)
        {
            return section switch
            {
                RefSection.Local => ActionGlyphKind.Laptop,
                RefSection.Remote => ActionGlyphKind.Cloud,
                RefSection.Worktrees => ActionGlyphKind.Worktree,
                RefSection.Stashes => ActionGlyphKind.Stash,
                RefSection.Submodules => ActionGlyphKind.Submodule,
                RefSection.PullRequests => ActionGlyphKind.PullRequest,
                _ => ActionGlyphKind.Tag,
            };
        }

        /// <summary>
        /// 줄 아래 말풍선 글(rich text). 브랜치는 폴더까지 붙인 전체 이름, 워크트리는 작업 폴더, 나머지는 줄의 설명. 구역 머리 줄은 없다.
        /// </summary>
        private static string HoverText(RefNode node)
        {
            if (node == null || node.Kind == RefNodeKind.Section)
            {
                return null;
            }
            if (node.Kind == RefNodeKind.Branch && node.Branch != null)
            {
                return NoParse(GitDisplay.Nfc(node.Branch.Name));
            }
            if (node.Kind == RefNodeKind.Worktree && node.Worktree != null)
            {
                return $"<b>{GitText.Get("worktree.path")}:</b>  {NoParse(GitDisplay.Nfc(node.Worktree.Path))}";
            }
            return string.IsNullOrEmpty(node.Tooltip) ? null : NoParse(node.Tooltip);
        }

        private static string NoParse(string text)
        {
            return $"<noparse>{text}</noparse>";
        }

        /// <summary>
        /// 머리 줄에 + 를 두는 구역과 그 말풍선 글 키. 없으면 null.
        /// </summary>
        private static string AddHintKey(RefSection section)
        {
            return section switch
            {
                RefSection.Remote => "refs.addRemoteHint",
                RefSection.Worktrees => "refs.addWorktreeHint",
                RefSection.Submodules => "refs.addSubmoduleHint",
                RefSection.PullRequests => "refs.createPullRequestHint",
                _ => null,
            };
        }

        private static ActionGlyphKind IconOf(RefNode node)
        {
            switch (node.Kind)
            {
                case RefNodeKind.Section:
                    return SectionIcon(node.Section);
                case RefNodeKind.Folder:
                    return ActionGlyphKind.Folder;
                case RefNodeKind.Remote:
                    return node.IsGitHub ? ActionGlyphKind.GitHub : ActionGlyphKind.Cloud;
                case RefNodeKind.Tag:
                    return ActionGlyphKind.Tag;
                case RefNodeKind.Stash:
                    return ActionGlyphKind.Stash;
                case RefNodeKind.Worktree:
                    return ActionGlyphKind.Folder;
                case RefNodeKind.Submodule:
                    return ActionGlyphKind.Submodule;
                case RefNodeKind.PullRequest:
                    return ActionGlyphKind.PullRequest;
                case RefNodeKind.PullRequestRemote:
                    return ActionGlyphKind.GitHub;
                default:
                    return ActionGlyphKind.Branch;
            }
        }

        /// <summary>
        /// 더블클릭: 브랜치는 체크아웃, 태그는 그 커밋으로 전환, 스태시는 변경 보기, 워크트리·서브모듈은 폴더 열기.
        /// </summary>
        private void RunDefault(RefNode node)
        {
            if (_services.Runner.IsBusy)
            {
                return;
            }
            switch (node.Kind)
            {
                case RefNodeKind.Branch when node.Branch.IsCurrent == false:
                    _ = _branchActions.SwitchAsync(node.Branch, LocalNames());
                    break;
                case RefNodeKind.Tag:
                    _ = _actions.CheckoutTagAsync(node.Tag);
                    break;
                case RefNodeKind.Stash:
                    _actions.ShowStash(node.Stash);
                    break;
                case RefNodeKind.Worktree:
                    _actions.OpenFolder(node.Worktree.Path);
                    break;
                case RefNodeKind.PullRequest:
                    UnityEngine.Application.OpenURL(node.PullRequest.Url);
                    break;
                case RefNodeKind.Info when node.Section == RefSection.PullRequests && _pullRequests?.NeedsSignIn == true:
                    UnityEditor.SettingsService.OpenUserPreferences(GitMainToolbarMenu.PreferencesPath);
                    break;
                case RefNodeKind.Submodule:
                    // GitKraken 처럼 두 번 누르면 서브모듈 편집 서랍(주소·상태·현재 커밋, 그 저장소 열기·지우기).
                    SubmoduleEditRequested?.Invoke(node.Submodule);
                    break;
            }
        }

        private void PopulateMenu(DropdownMenu menu, RefNode node)
        {
            DropdownMenuAction.Status writable = _services.Runner.IsBusy ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            switch (node.Kind)
            {
                case RefNodeKind.Section:
                    PopulateSectionMenu(menu, node.Section, writable);
                    break;
                case RefNodeKind.Remote:
                {
                    string remote = node.Label;
                    _refs.RemoteUrls.TryGetValue(remote, out string url);
                    menu.AppendAction(GitText.Format("remote.fetchOne", GitDisplay.MenuSafe(remote)), action => _ = _actions.FetchRemoteAsync(remote), writable);
                    menu.AppendAction(GitText.Get("remote.prune"), action => _ = _actions.PruneRemoteAsync(remote), writable);
                    menu.AppendSeparator();
                    menu.AppendAction(GitText.Get("remote.rename"), action => _ = _actions.RenameRemoteAsync(remote, _refs.RemoteUrls.Keys), writable);
                    menu.AppendAction(GitText.Get("remote.editUrl"), action => _ = _actions.EditRemoteUrlAsync(remote, url), writable);
                    menu.AppendAction(GitText.Get("remote.copyUrl"), action => _services.Assets.CopyToClipboard(url ?? string.Empty),
                        string.IsNullOrEmpty(url) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                    menu.AppendSeparator();
                    menu.AppendAction(GitText.Format("remote.remove", GitDisplay.MenuSafe(remote)), action => _ = _actions.RemoveRemoteAsync(remote), writable);
                    break;
                }
                case RefNodeKind.Branch:
                    _branchMenu.Populate(menu, node.Branch, new BranchMenu.Hooks
                    {
                        LocalNames = LocalNames(),
                        ShowInExplorer = hash => ShowInExplorerRequested?.Invoke(hash),
                        CreatePullRequest = branch => CreatePullRequestRequested?.Invoke(branch),
                    });
                    break;
                case RefNodeKind.Tag:
                    PopulateTagMenu(menu, node.Tag, writable);
                    break;
                case RefNodeKind.Stash:
                    PopulateStashMenu(menu, node.Stash, writable);
                    break;
                case RefNodeKind.Worktree:
                    PopulateWorktreeMenu(menu, node.Worktree, writable);
                    break;
                case RefNodeKind.PullRequest:
                    PopulatePullRequestMenu(menu, node.PullRequest, writable);
                    break;
                case RefNodeKind.PullRequestGroup:
                    PopulatePullRequestFilterMenu(menu, node);
                    break;
                case RefNodeKind.Submodule:
                    menu.AppendAction(GitText.Get("refs.editSubmodule"), action => SubmoduleEditRequested?.Invoke(node.Submodule));
                    menu.AppendAction(GitText.Get("refs.openSubmodule"), action => GitRepository.OpenRepository(RefActions.SubmoduleFullPath(node.Submodule)),
                        node.Submodule.State == GitSubmoduleState.NotInitialized ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                    menu.AppendSeparator();
                    menu.AppendAction(GitText.Get("refs.submoduleUpdate"), action => _ = _actions.UpdateSubmoduleAsync(node.Submodule.Path), writable);
                    menu.AppendSeparator();
                    menu.AppendAction(GitText.Get("refs.openFolder"), action => _actions.OpenFolder(RefActions.SubmoduleFullPath(node.Submodule)));
                    menu.AppendAction(GitText.Get("refs.openTerminal"), action => _actions.OpenTerminal(RefActions.SubmoduleFullPath(node.Submodule)));
                    menu.AppendAction(GitText.Get("common.copyPath"), action => _services.Assets.CopyToClipboard(node.Submodule.Path));
                    break;
            }
        }

        private void PopulateSectionMenu(DropdownMenu menu, RefSection section, DropdownMenuAction.Status writable)
        {
            bool hasCommits = _services.Repository.HeadCommit != null;
            DropdownMenuAction.Status needsHead = hasCommits ? writable : DropdownMenuAction.Status.Disabled;
            switch (section)
            {
                case RefSection.Local:
                    menu.AppendAction(GitText.Get("refs.newBranch"), action => _ = _actions.NewBranchAtHeadAsync(), needsHead);
                    break;
                case RefSection.Remote:
                    menu.AppendAction(GitText.Get("refs.addRemote"), action => AddRequested?.Invoke(RefSection.Remote), writable);
                    menu.AppendAction(GitText.Get("refs.fetch"), action => _ = _actions.FetchAsync(), writable);
                    break;
                case RefSection.Stashes:
                    menu.AppendAction(GitText.Get("refs.stashNow"), action => _ = _actions.StashNowAsync(),
                        _services.Repository.Entries.Count > 0 ? writable : DropdownMenuAction.Status.Disabled);
                    break;
                case RefSection.PullRequests:
                    menu.AppendAction(GitText.Get("pr.menu"), action => AddRequested?.Invoke(RefSection.PullRequests));
                    menu.AppendAction(GitText.Get("prList.refresh"), action => ReloadPullRequests());
                    break;
                case RefSection.Submodules:
                    menu.AppendAction(GitText.Get("refs.addSubmodule"), action => AddRequested?.Invoke(RefSection.Submodules), writable);
                    menu.AppendAction(GitText.Get("refs.submoduleUpdateAll"), action => _ = _actions.UpdateSubmoduleAsync("."),
                        _refs.Submodules.Count > 0 ? writable : DropdownMenuAction.Status.Disabled);
                    break;
                case RefSection.Tags:
                    menu.AppendAction(GitText.Get("refs.newTag"), action => _ = _actions.NewTagAtHeadAsync(), needsHead);
                    break;
                case RefSection.Worktrees:
                    menu.AppendAction(GitText.Get("refs.addWorktree"), action => AddRequested?.Invoke(RefSection.Worktrees), writable);
                    menu.AppendAction(GitText.Get("refs.openFolder"), action => _actions.OpenFolder(GitRepository.RepoRoot ?? GitRepository.ProjectRoot));
                    break;
            }
        }

        /// <summary>
        /// GitKraken 의 PR ⋮ 메뉴와 같은 구성: GitHub 에서 보기·링크 복사 / 바뀐 것 보기·그래프에서 보기 / 체크아웃·워크트리 만들기.
        /// </summary>
        private void PopulatePullRequestMenu(DropdownMenu menu, GitPullRequestInfo pr, DropdownMenuAction.Status writable)
        {
            GitBranchRef head = PullRequestHead(pr);
            string headName = GitDisplay.MenuSafe(pr.HeadRef ?? string.Empty);
            DropdownMenuAction.Status needsHead = head == null ? DropdownMenuAction.Status.Disabled : writable;
            menu.AppendAction(GitText.Format("prList.viewOnGitHub", pr.Number), action => UnityEngine.Application.OpenURL(pr.Url));
            menu.AppendAction(GitText.Format("prList.copyLink", pr.Number), action => _services.Assets.CopyToClipboard(pr.Url));
            menu.AppendSeparator();
            menu.AppendAction(GitText.Format("prList.viewChanges", pr.Number), action => ViewPullRequestChanges(pr, head),
                head == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            menu.AppendAction(GitText.Format("prList.goToInGraph", headName), action => RevealRequested?.Invoke(head.Hash),
                head == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            menu.AppendSeparator();
            menu.AppendAction(head == null ? GitText.Get("prList.checkoutUnavailable") : GitText.Format("prList.checkoutRemote", GitDisplay.MenuSafe(head.Name)),
                action => _ = _branchActions.SwitchAsync(head, LocalNames()), needsHead);
            menu.AppendAction(GitText.Get("prList.createWorktree"), action => CreateWorktreeRequested?.Invoke(head.Name), needsHead);
        }

        private void OpenPullRequest(GitPullRequestInfo pr)
        {
            List<GitHubRemoteRepo> remotes = GitPullRequestForm.GitHubRemotes(_refs.RemoteUrls);
            GitHubRemoteRepo repository = remotes.FirstOrDefault(r => r.Remote == _refs.PullRequests?.Remote) ?? remotes.FirstOrDefault();
            GitBranchRef head = PullRequestHead(pr);
            Action changes = head == null ? null : () => ViewPullRequestChanges(pr, head);
            Action checkout = head == null ? null : () => _ = _branchActions.SwitchAsync(head, LocalNames());
            PullRequestOpened?.Invoke(pr, repository, changes, checkout);
        }

        /// <summary>PR 이 바꾼 것: 받는 브랜치와 갈라진 곳부터 보내는 브랜치 끝까지(GitHub 의 Files changed 와 같다).</summary>
        private void ViewPullRequestChanges(GitPullRequestInfo pr, GitBranchRef head)
        {
            string baseRef = string.IsNullOrEmpty(_refs.PullRequests?.Remote) ? pr.BaseRef : _refs.PullRequests.Remote + "/" + pr.BaseRef;
            GitCompareWindow.OpenSinceMergeBase(baseRef, head.Name);
        }

        /// <summary>
        /// GitKraken 의 PR 묶음(필터) ⋮ 메뉴: 고치기·지우기 / 위로·아래로(맨 위·맨 아래면 그쪽은 없다).
        /// </summary>
        private void PopulatePullRequestFilterMenu(DropdownMenu menu, RefNode node)
        {
            int index = node.FilterIndex;
            if (index < 0)
            {
                return;
            }
            menu.AppendAction(GitText.Get("prFilter.editMenu"), action => PullRequestFilterRequested?.Invoke(index));
            menu.AppendAction(GitText.Get("prFilter.removeMenu"), action => _ = RemoveFilterAsync(index, node.Label));
            menu.AppendSeparator();
            if (index > 0)
            {
                menu.AppendAction(GitText.Get("prFilter.moveUp"), action => GitPullRequestFilters.Move(index, -1));
            }
            if (index < node.FilterCount - 1)
            {
                menu.AppendAction(GitText.Get("prFilter.moveDown"), action => GitPullRequestFilters.Move(index, +1));
            }
        }

        private async Task RemoveFilterAsync(int index, string name)
        {
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("prFilter.removeTitle"), GitText.Format("prFilter.removeConfirm", name), GitText.Get("common.delete"),
                    danger: true, icon: ActionGlyphKind.PullRequest))
            {
                GitPullRequestFilters.Remove(index);
            }
        }

        /// <summary>
        /// PR 의 보내는 브랜치가 이 저장소의 원격 브랜치로 받아져 있으면 그 브랜치(체크아웃할 수 있다). 포크 PR 이나 아직 fetch 하지 않았으면 null.
        /// </summary>
        private GitBranchRef PullRequestHead(GitPullRequestInfo pr)
        {
            GitHubRemoteRepo remote = GitPullRequestForm.GitHubRemotes(_refs.RemoteUrls)
                .FirstOrDefault(r => string.Equals(r.FullName, pr.HeadRepository, StringComparison.OrdinalIgnoreCase));
            return remote == null || string.IsNullOrEmpty(pr.HeadRef) ? null : _refs.Branches.FirstOrDefault(b => b.IsRemote && b.Name == remote.Remote + "/" + pr.HeadRef);
        }

        private void PopulateTagMenu(DropdownMenu menu, GitTagRef tag, DropdownMenuAction.Status writable)
        {
            string name = GitDisplay.MenuSafe(tag.Name);
            string current = GitDisplay.MenuSafe(_services.Repository.Branch?.Name ?? "HEAD");
            List<string> remotes = Remotes();
            menu.AppendAction(GitText.Format("refs.tagCheckout", name), action => _ = _actions.CheckoutTagAsync(tag), writable);
            menu.AppendAction(GitText.Format("fork.mergeInto", name, current), action => _ = _actions.MergeTagAsync(tag), writable);
            menu.AppendSeparator();
            // 원격이 여럿이면 원격마다 고를 수 있게 한다(어느 하나로 정해 올리지 않는다).
            foreach (string remote in remotes)
            {
                menu.AppendAction(GitText.Format("refs.tagPush", name, remote), action => _ = _actions.PushTagAsync(tag, remote), writable);
            }
            menu.AppendAction(GitText.Format("fork.delete", name), action => _ = _actions.DeleteTagAsync(tag), writable);
            foreach (string remote in remotes)
            {
                menu.AppendAction(GitText.Format("refs.tagDeleteRemote", name, remote), action => _ = _actions.DeleteRemoteTagAsync(tag, remote), writable);
            }
            menu.AppendSeparator();
            menu.AppendAction(GitText.Get("common.copyName"), action => _services.Assets.CopyToClipboard(tag.Name));
            menu.AppendAction(GitText.Get("fork.copySha"), action => _services.Assets.CopyToClipboard(tag.Commit));
        }

        private void PopulateStashMenu(DropdownMenu menu, GitStashRef stash, DropdownMenuAction.Status writable)
        {
            menu.AppendAction(GitText.Get("refs.stashApply"), action => _ = _actions.ApplyStashAsync(stash, drop: false), writable);
            menu.AppendAction(GitText.Get("refs.stashPop"), action => _ = _actions.ApplyStashAsync(stash, drop: true), writable);
            menu.AppendAction(GitText.Get("refs.stashDrop"), action => _ = _actions.DropStashAsync(stash), writable);
            menu.AppendSeparator();
            menu.AppendAction(GitText.Get("refs.stashShow"), action => _actions.ShowStash(stash));
            menu.AppendAction(GitText.Get("refs.stashBranch"), action => _ = _actions.BranchFromStashAsync(stash), writable);
            menu.AppendSeparator();
            menu.AppendAction(GitText.Get("refs.copyMessage"), action => _services.Assets.CopyToClipboard(stash.Message));
        }

        private void PopulateWorktreeMenu(DropdownMenu menu, GitWorktreeRef worktree, DropdownMenuAction.Status writable)
        {
            menu.AppendAction(GitText.Get("refs.openFolder"), action => _actions.OpenFolder(worktree.Path));
            menu.AppendAction(GitText.Get("refs.openTerminal"), action => _actions.OpenTerminal(worktree.Path));
            menu.AppendAction(GitText.Get("common.copyPath"), action => _services.Assets.CopyToClipboard(worktree.Path));
            menu.AppendSeparator();
            // 지금 쓰고 있는 워크트리와 원본(bare) 저장소는 지울 수 없다.
            menu.AppendAction(GitText.Get("refs.worktreeRemove"), action => _ = _actions.RemoveWorktreeAsync(worktree),
                worktree.IsCurrent || worktree.IsBare ? DropdownMenuAction.Status.Disabled : writable);
        }

        private HashSet<string> LocalNames()
        {
            return new HashSet<string>(_refs.Branches.Where(b => b.IsRemote == false).Select(b => b.Name));
        }

        /// <summary>
        /// 태그를 올리고 지울 원격들(origin 을 앞에). 원격 브랜치를 아직 받지 않은 원격도 넣는다.
        /// </summary>
        private List<string> Remotes()
        {
            IEnumerable<string> fromBranches = _refs.Branches.Where(b => b.IsRemote).Select(b => GitCommitCommands.SplitRemoteBranch(b.Name).remote);
            List<string> remotes = fromBranches.Concat(_refs.RemoteUrls.Keys).Distinct().ToList();
            return remotes.OrderBy(r => r == "origin" ? 0 : 1).ThenBy(r => r, System.StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
