using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
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
    public sealed class RefSidebar : IDisposable
    {
        private const string Block = "ref-tree";
        private const float RowHeight = 24f;

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

        /// <summary>
        /// 참조를 다시 읽었을 때. 접힌 사이드바(<see cref="SidebarRail"/>)가 구역별 개수(<see cref="SectionCount"/>)를 새로 칠한다.
        /// </summary>
        public event Action Loaded;

        private string CurrentKey => $"{_services.Repository.HeadCommit}|{_services.Repository.UpstreamCommit}|{_services.Repository.Branch?.Name}|{_services.Repository.LastFetchTime}";

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
                if (selected.FirstOrDefault() is RefNode node && node.Commit != null)
                {
                    RevealRequested?.Invoke(node.Commit);
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
            Root.Add(_tree);

            _services.Runner.StateChanged += OnRunnerStateChanged;
        }

        public void Dispose()
        {
            _services.Runner.StateChanged -= OnRunnerStateChanged;
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
            Loaded?.Invoke();
            LoadPullRequests();
            if (_reloadQueued)
            {
                _reloadQueued = false;
                Load();
            }
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
            string key = $"{GitHubAccount.Login}|{target?.FullName}";
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
            ApplyPullRequests(new GitPullRequestSet { Repository = target.FullName, Login = GitHubAccount.Login, All = pulls, Error = error });
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
            List<RefNode> roots = RefTreeBuilder.Build(_refs, _state.Filter, section => GitText.Get("refs.section" + section), GitText.Get);
            List<int> selected = _tree.selectedIds.ToList();
            _rebuilding = true;
            _tree.SetRootItems(roots.Select(ToItemData).ToList());
            _tree.Rebuild();
            bool filtering = string.IsNullOrWhiteSpace(_state.Filter) == false;
            foreach (RefNode node in Containers(roots))
            {
                int id = _idByKey[node.Id];
                if (filtering || IsAlwaysOpen(node) || IsExpanded(node.Id))
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
        private static bool IsAlwaysOpen(RefNode node)
        {
            return node.Kind is RefNodeKind.Folder or RefNodeKind.Remote;
        }

        private bool IsExpanded(string key)
        {
            return RefTreeBuilder.ExpandedByDefault(key) != _state.Toggled.Contains(key);
        }

        private void OnExpandedChanged(TreeViewExpansionChangedArgs args)
        {
            // 폴더·원격은 키보드(←)나 Alt+클릭으로 접혀도 다시 펼친다.
            if (_rebuilding == false && args.isExpanded == false && _tree.GetItemDataForId<RefNode>(args.id) is { } folded && IsAlwaysOpen(folded))
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
            // 줄에 마우스를 올리면 GitKraken 처럼 줄 아래에 전체 이름(sample/main, origin/main)이나 워크트리 폴더를 바로 띄운다.
            HoverCallout.Attach(row, () => row.userData is int index ? HoverText(_tree.GetItemDataForIndex<RefNode>(index)) : null,
                () => _services.PopupHost, HoverCalloutPlacement.Below);
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (row.userData is int index)
                {
                    _tree.SetSelectionWithoutNotify(new[] { index });
                    PopulateMenu(evt.menu, _tree.GetItemDataForIndex<RefNode>(index));
                }
            }));
            return row;
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
            row.EnableInClassList(Block + "__row--current", node.IsCurrent);
            // 펼침 화살표는 row 바깥(TreeView 항목)에 있어, 손가락 커서와 화살표 숨김은 항목에 단다. 줄은 재사용되니 매번 다시 정한다.
            // 폴더·원격은 늘 펼쳐 두므로 화살표를 감추고 누를 수 없게 한다(자리는 남겨 형제 브랜치와 들여쓰기를 맞춘다).
            bool isFixed = IsAlwaysOpen(node);
            for (VisualElement item = row.parent; item != null; item = item.parent)
            {
                if (item.ClassListContains(TreeView.itemUssClassName))
                {
                    item.EnableInClassList(Block + "__item--section", isSection);
                    item.EnableInClassList(Block + "__item--fixed", isFixed);
                    item.Q(className: TreeView.itemToggleUssClassName)?.SetEnabled(isFixed == false);
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
                case RefNodeKind.Info when _pullRequests?.NeedsSignIn == true:
                    UnityEditor.SettingsService.OpenUserPreferences(GitMainToolbarMenu.PreferencesPath);
                    break;
                case RefNodeKind.Submodule when node.Submodule.State != GitSubmoduleState.NotInitialized:
                    // Fork 처럼 두 번 누르면 그 서브모듈 저장소를 Git 창에서 연다.
                    GitRepository.OpenRepository(RefActions.SubmoduleFullPath(node.Submodule));
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
                    menu.AppendAction(GitText.Get("refs.fetch"), action => _ = _actions.FetchAsync(), writable);
                    break;
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
                case RefNodeKind.Submodule:
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

        private void PopulatePullRequestMenu(DropdownMenu menu, GitPullRequestInfo pr, DropdownMenuAction.Status writable)
        {
            menu.AppendAction(GitText.Get("prList.openInBrowser"), action => UnityEngine.Application.OpenURL(pr.Url));
            GitBranchRef head = PullRequestHead(pr);
            menu.AppendAction(head == null ? GitText.Get("prList.checkoutUnavailable") : GitText.Format("prList.checkout", GitDisplay.MenuSafe(pr.HeadRef)),
                action => _ = _branchActions.SwitchAsync(head, LocalNames()), head == null ? DropdownMenuAction.Status.Disabled : writable);
            menu.AppendSeparator();
            menu.AppendAction(GitText.Get("prList.copyUrl"), action => _services.Assets.CopyToClipboard(pr.Url));
            menu.AppendAction(GitText.Get("prList.copyBranch"), action => _services.Assets.CopyToClipboard(pr.HeadRef ?? string.Empty));
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
            string remote = DefaultRemote();
            menu.AppendAction(GitText.Format("refs.tagCheckout", name), action => _ = _actions.CheckoutTagAsync(tag), writable);
            menu.AppendAction(GitText.Format("fork.mergeInto", name, current), action => _ = _actions.MergeTagAsync(tag), writable);
            menu.AppendSeparator();
            menu.AppendAction(GitText.Format("refs.tagPush", name, remote), action => _ = _actions.PushTagAsync(tag, remote), writable);
            menu.AppendAction(GitText.Format("fork.delete", name), action => _ = _actions.DeleteTagAsync(tag), writable);
            menu.AppendAction(GitText.Format("refs.tagDeleteRemote", name, remote), action => _ = _actions.DeleteRemoteTagAsync(tag, remote), writable);
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
        /// 태그를 올리고 지울 원격. origin 이 있으면 origin, 없으면 첫 원격.
        /// </summary>
        private string DefaultRemote()
        {
            List<string> remotes = _refs.Branches.Where(b => b.IsRemote).Select(b => GitCommitCommands.SplitRemoteBranch(b.Name).remote).Distinct().ToList();
            return remotes.Contains("origin") || remotes.Count == 0 ? "origin" : remotes[0];
        }
    }
}
