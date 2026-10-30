using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Fork 형태 브랜치 탐색기(Preferences > Git 의 브랜치 탐색기 모양 = 커밋 목록).
    /// 위는 커밋 목록(<see cref="CommitGraphList"/> 에 작성자 · SHA · 날짜 칸), 아래는 고른 커밋을 세 칸으로 본다:
    /// Commit(작성자 · 커미터 · 참조 · SHA · 부모 · 메시지 · 바뀐 파일), Changes(바뀐 파일 | diff), File Tree(그 시점 전체 파일 | 내용).
    /// 기간 · 검색 도구 막대와 읽기는 <see cref="BranchExplorerView"/> 와 같다.
    /// </summary>
    public sealed class CommitHistoryView : ExplorerView
    {
        private enum Page
        {
            Commit,
            Changes,
            Tree,
        }

        private const float ContentLineHeight = 18f;

        private readonly BranchExplorerState _state;
        private readonly IAvatarProvider _avatars;
        private readonly DropdownField _range;
        private readonly Label _summary;
        private readonly ToolbarSearchField _search;
        private readonly ToolbarButton _nextMatchButton;
        private readonly VisualElement _errorNotice;
        private readonly Label _errorText;
        private readonly VisualElement _emptyNotice;
        private readonly TwoPaneSplitView _split;
        private readonly CommitGraphList _list;
        private readonly CommitMenu _commitMenu;
        private readonly CommitFileMenu _fileMenu;

        private readonly Label _placeholder;
        private readonly Dictionary<Page, (Button tab, VisualElement page)> _pages = new();

        // Commit 칸
        private readonly VisualElement _author;
        private readonly VisualElement _committer;
        private readonly VisualElement _refs;
        private readonly Label _sha;
        private readonly VisualElement _parents;
        private readonly Label _subject;
        private readonly Label _body;
        /// <summary>
        /// Commit 칸의 바뀐 파일(Fork 처럼 경로만 있는 평평한 목록). 칸 전체가 한 스크롤이라 목록 요소가 아니라 줄을 직접 넣는다.
        /// </summary>
        private readonly VisualElement _commitFiles;

        private readonly CommitFileMenu _compactMenu;

        /// <summary>
        /// Commit 칸에 줄로 넣는 파일 수 한도. 넘으면 나머지는 Changes 칸에서 보라고 한 줄 남긴다(줄을 다 만들면 칸이 멈춘다).
        /// </summary>
        private const int MaxCommitFileRows = 500;

        // Changes 칸
        private readonly VisualElement _changesHeader;
        private readonly VisualElement _changesSummary;
        private readonly FileBrowser _changesFiles;
        private readonly GitDiffPanel _diff;

        // File Tree 칸
        private readonly VisualElement _treeHeader;
        private readonly TreeView _tree;
        private readonly Label _contentPath;
        private readonly Label _contentNotice;
        private readonly ListView _content;
        private string[] _contentLines = Array.Empty<string>();

        private GitGraphNode _shown;
        private List<GitFileChange> _shownFiles = new();

        /// <summary>
        /// File Tree 칸을 채운 커밋. 칸을 처음 열 때만 읽는다(전체 파일 목록은 크다).
        /// </summary>
        private string _treeHash;

        private string _contentKey;

        /// <summary>
        /// "File Tree 에서 보기" 로 고를 파일. 트리를 다 읽은 뒤 펼쳐 고른다.
        /// </summary>
        private string _pendingTreePath;

        /// <summary>
        /// 지금 트리의 경로 → 항목 번호(펼치고 고를 때 쓴다).
        /// </summary>
        private readonly Dictionary<string, int> _treeIds = new();
        private Page _page = Page.Commit;
        private string _pendingReveal;
        private string _loadedKey;

        public override bool HasLoaded => _loadedKey != null && _isLoading == false;

        public override System.Collections.Generic.IEnumerable<(VisualElement Host, SkeletonShape Shape)> SkeletonHosts
        {
            get
            {
                yield return (Root.Require<VisualElement>("list-host"), SkeletonShape.Commits);
            }
        }
        private bool _isLoading;
        private int _searchCursor = -1;

        private string CurrentKey => $"{Services.Repository.HeadCommit}|{Services.Repository.UpstreamCommit}|{Services.Repository.Branch?.Name}|{Services.Repository.LastFetchTime}|{Services.Repository.RefsFingerprint}|{_state.RangeIndex}";

        public CommitHistoryView(GitServices services, BranchExplorerState state, ITabNavigator navigator) : base(services, "CommitHistoryView")
        {
            _state = state;
            _avatars = services.Avatars;
            _commitMenu = new CommitMenu(services);

            _range = Root.Require<DropdownField>("range");
            _summary = Root.Require<Label>("summary");
            _search = Root.Require<ToolbarSearchField>("search");
            _search.RegisterCallback<FocusInEvent>(_ => _search.AddToClassList("git-search--focused"));
            _search.RegisterCallback<FocusOutEvent>(_ => _search.RemoveFromClassList("git-search--focused"));
            _nextMatchButton = Root.Require<ToolbarButton>("next-match-button");
            _errorNotice = Root.Require<VisualElement>("error-notice");
            _errorText = Root.Require<Label>("error-text");
            _emptyNotice = Root.Require<VisualElement>("empty-notice");
            _split = Root.Require<TwoPaneSplitView>("history-split");

            _list = new CommitGraphList(Root.Require<VisualElement>("list-host"), services.Avatars, detailColumns: true);
            _list.CommitSelected += Show;
            _list.ContextMenuRequested += PopulateCommitMenu;

            _placeholder = Root.Require<Label>("placeholder");
            AddPage(Page.Commit, "tab-commit", "commit-page");
            AddPage(Page.Changes, "tab-changes", "changes-page");
            AddPage(Page.Tree, "tab-tree", "tree-page");

            _author = Root.Require<VisualElement>("author");
            _committer = Root.Require<VisualElement>("committer");
            _refs = Root.Require<VisualElement>("refs");
            _sha = Root.Require<Label>("sha");
            _parents = Root.Require<VisualElement>("parents");
            _subject = Root.Require<Label>("subject");
            _body = Root.Require<Label>("message-body");
            _subject.selection.isSelectable = true;
            _body.selection.isSelectable = true;
            _sha.selection.isSelectable = true;
            _sha.RegisterCallback<ClickEvent>(_ =>
            {
                if (_shown != null)
                {
                    services.Assets.CopyToClipboard(_shown.Commit.Hash);
                }
            });
            _commitFiles = Root.Require<VisualElement>("commit-files");

            _changesHeader = Root.Require<VisualElement>("changes-header");
            _changesSummary = Root.Require<VisualElement>("changes-summary");
            _changesFiles = new FileBrowser(Root.Require<VisualElement>("changes-files"), Root.Require<VisualElement>("changes-view-mode"), services)
            {
                DiffTargetOf = TargetOf,
            };
            _changesFiles.SelectionChanged += item =>
            {
                if (item != null)
                {
                    _diff.SetTarget(TargetOf(item));
                }
            };
            // Fork 처럼 바뀐 파일 우클릭: 열기 · 외부 diff · Finder · 파일 되돌리기 · Blame · 히스토리 · 이 버전 저장 · File Tree 에서 보기 · 경로 복사.
            _fileMenu = new CommitFileMenu(services) { ShowInFileTree = ShowInFileTree };
            _compactMenu = new CommitFileMenu(services) { ShowInFileTree = ShowInFileTree, Compact = true };
            _changesFiles.FileMenu = (menu, item) => _fileMenu.Populate(menu, _shown?.Commit, item);
            _diff = new GitDiffPanel(services.Diff);
            _diff.Root.style.flexGrow = 1;
            _diff.ShowCloseButton(false);
            _diff.ShowReloadButton(false);
            _diff.PopupHostProvider = () => services.PopupHost;
            Root.Require<VisualElement>("diff-host").Add(_diff.Root);

            _treeHeader = Root.Require<VisualElement>("tree-header");
            _tree = new TreeView
            {
                fixedItemHeight = 22f,
                selectionType = SelectionType.Single,
                makeItem = MakeTreeRow,
                bindItem = BindTreeRow,
            };
            _tree.AddToClassList("list");
            _tree.AddToClassList("commit-history__tree");
            _tree.selectionChanged += _ => ShowSelectedFile();
            Root.Require<VisualElement>("tree-host").Add(_tree);
            _contentPath = Root.Require<Label>("content-path");
            _contentNotice = Root.Require<Label>("content-notice");
            _content = Root.Require<ListView>("content-lines");
            _content.fixedItemHeight = ContentLineHeight;
            _content.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            _content.selectionType = SelectionType.None;
            _content.makeItem = MakeContentRow;
            _content.bindItem = BindContentRow;
            _content.itemsSource = _contentLines;

            _range.choices = RangeKeys.Select(GitText.Get).ToList();
            _range.index = Math.Max(0, Math.Min(state.RangeIndex, RangeKeys.Length - 1));
            _range.RegisterValueChangedCallback(_ =>
            {
                _state.RangeIndex = _range.index;
                ReloadIfNeeded();
            });

            _search.SetValueWithoutNotify(state.Search);
            _search.RegisterValueChangedCallback(evt =>
            {
                _state.Search = evt.newValue;
                _searchCursor = -1;
                _nextMatchButton.SetEnabled(string.IsNullOrWhiteSpace(evt.newValue) == false);
            });
            // 검색창에서 Enter 를 누르면 다음 일치 커밋으로 간다.
            _search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                {
                    FocusNextMatch();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            _nextMatchButton.SetEnabled(string.IsNullOrWhiteSpace(state.Search) == false);
            _nextMatchButton.clicked += FocusNextMatch;

            if (_avatars != null)
            {
                Root.RegisterCallback<AttachToPanelEvent>(_ => _avatars.AvatarLoaded += RefreshAvatars);
                Root.RegisterCallback<DetachFromPanelEvent>(_ => _avatars.AvatarLoaded -= RefreshAvatars);
            }
            ShowPlaceholder();
        }

        protected override void OnActivated()
        {
            ReloadIfNeeded();
            Render();
        }

        public override void Dispose()
        {
            _changesFiles.Dispose();
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

            string shownHash = _shown?.Commit.Hash;
            _list.SetGraph(graph, default);
            if (shownHash != null && _list.SelectCommit(shownHash) == false)
            {
                ShowPlaceholder();
            }

            _summary.text = GitText.Format("explorer.summary", graph.Nodes.Count, graph.Lanes.Count) + (graph.Nodes.Count >= MaxCommits ? GitText.Format("explorer.capped", MaxCommits) : string.Empty);
            Render();
            TryReveal();
        }

        public override void Reveal(string hash)
        {
            _pendingReveal = hash;
            ReloadIfNeeded();
            Root.schedule.Execute(TryReveal);
        }

        private void TryReveal()
        {
            if (_pendingReveal == null || _isLoading || _list.Graph == null)
            {
                return;
            }
            if (_list.SelectCommit(_pendingReveal))
            {
                _pendingReveal = null;
                return;
            }
            if (_state.RangeIndex != AllRangeIndex)
            {
                // 값이 바뀌면 다시 읽고, 다 읽은 뒤 여기로 돌아온다.
                _range.index = AllRangeIndex;
                return;
            }
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

        private void AddPage(Page page, string tabName, string pageName)
        {
            Button tab = Root.Require<Button>(tabName);
            tab.clicked += () => SelectPage(page);
            _pages[page] = (tab, Root.Require<VisualElement>(pageName));
        }

        private void SelectPage(Page page)
        {
            _page = page;
            foreach (KeyValuePair<Page, (Button tab, VisualElement page)> pair in _pages)
            {
                pair.Value.tab.EnableInClassList("commit-history__tab--selected", pair.Key == page);
                pair.Value.page.SetVisible(_shown != null && pair.Key == page);
            }
            if (page == Page.Tree)
            {
                LoadTree();
            }
        }

        private void ShowPlaceholder()
        {
            _shown = null;
            _shownFiles = new List<GitFileChange>();
            _treeHash = null;
            _placeholder.SetVisible(true);
            _diff.SetTarget(null);
            SelectPage(_page);
        }

        // ── 고른 커밋 ──

        private async void Show(GitGraphNode node)
        {
            if (node == null)
            {
                ShowPlaceholder();
                return;
            }
            if (_shown == node)
            {
                return;
            }
            _shown = node;
            GitCommitInfo commit = node.Commit;
            _placeholder.SetVisible(false);
            _treeHash = null;
            SelectPage(_page);

            BindPerson(_author, GitText.Get("history.author"), commit.AuthorName, commit.AuthorEmail, commit.Date, commit.Hash);
            _committer.SetVisible(false);
            BindRefs(node);
            _sha.text = commit.Hash;
            BindParents(commit);
            SetMessage(commit.Subject);
            BindHeaderLine(_changesHeader, commit);
            BindHeaderLine(_treeHeader, commit);
            _shownFiles = new List<GitFileChange>();
            BindFiles();
            _diff.SetTarget(null);

            string hash = commit.Hash;
            string message = await Services.History.CommitMessageAsync(hash);
            List<GitFileChange> files = await Services.History.CommitFilesAsync(hash);
            GitCommitPerson committer = await GitCommitTree.CommitterAsync(hash);
            if (_shown?.Commit.Hash != hash)
            {
                return;
            }
            SetMessage(message);
            // 작성자와 같은 사람·같은 시각이면(대부분의 커밋) 커미터 칸을 따로 두지 않는다.
            bool sameAsAuthor = committer == null
                || (committer.Email == commit.AuthorEmail && committer.Name == commit.AuthorName && Math.Abs((committer.Date - commit.Date).TotalSeconds) < 1);
            _committer.SetVisible(sameAsAuthor == false);
            if (sameAsAuthor == false)
            {
                BindPerson(_committer, GitText.Get("history.committer"), committer.Name, committer.Email, committer.Date, null);
            }
            _shownFiles = files;
            BindFiles();
            if (_page == Page.Tree)
            {
                LoadTree();
            }
        }

        private void BindFiles()
        {
            FileListItem[] items = _shownFiles.Select(FileListItem.From).ToArray();
            BindCommitFileRows();
            _changesFiles.SetItems(items);
            BindSummary(_changesSummary, _shownFiles);
        }

        /// <summary>
        /// Commit 칸 파일 줄: [상태][아이콘][저장소 경로][→]. Fork 처럼 링크 모양(마우스를 올리면 파란 글씨 · 손가락 커서 · → 는 USS)이고,
        /// 왼쪽 클릭이면 Changes 칸에서 diff 를 연다. 우클릭은 Fork 의 Commit 메뉴(<see cref="CommitFileMenu.Compact"/>).
        /// ClickEvent 는 왼쪽 버튼에만 오므로 우클릭으로는 넘어가지 않는다.
        /// </summary>
        private void BindCommitFileRows()
        {
            _commitFiles.Clear();
            foreach (GitFileChange change in _shownFiles.Take(MaxCommitFileRows))
            {
                FileListItem item = FileListItem.From(change);
                VisualElement row = new();
                row.AddToClassList("commit-history__file");
                Label badge = new();
                badge.AddToClassList("state-badge");
                badge.AddToClassList("commit-history__file-badge");
                StateBadge.Apply(badge, item.State);
                row.Add(badge);
                Image icon = new();
                icon.AddToClassList("commit-history__file-icon");
                Services.Icons.BindIcon(icon, item.ProjectPath, item.RepoPath);
                row.Add(icon);
                Label path = new(string.IsNullOrEmpty(change.OriginalRepoPath) ? change.RepoPath : $"{change.OriginalRepoPath} → {change.RepoPath}");
                path.AddToClassList("commit-history__file-path");
                path.AddToClassList("ellipsis");
                row.Add(path);
                Label arrow = new("→");
                arrow.AddToClassList("commit-history__file-arrow");
                row.Add(arrow);
                row.RegisterCallback<ClickEvent>(_ => OpenInChanges(item));
                row.AddManipulator(new ContextualMenuManipulator(evt => _compactMenu.Populate(evt.menu, _shown?.Commit, item)));
                _commitFiles.Add(row);
            }
            if (_shownFiles.Count > MaxCommitFileRows)
            {
                Label more = new(GitText.Format("history.moreFiles", _shownFiles.Count - MaxCommitFileRows));
                more.AddToClassList("commit-history__file-more");
                more.RegisterCallback<ClickEvent>(_ => SelectPage(Page.Changes));
                _commitFiles.Add(more);
            }
        }

        /// <summary>
        /// Commit 칸에서 고른 파일: Changes 칸으로 넘어가 그 파일 diff 를 본다.
        /// </summary>
        private void OpenInChanges(FileListItem item)
        {
            SelectPage(Page.Changes);
            _diff.SetTarget(TargetOf(item));
            // Fork 처럼 왼쪽 목록에서도 그 파일을 펼쳐 고르고 보이게 한다. 칸이 막 보였을 때는 아직 배치 전이라 스크롤이 먹지 않아 한 박자 미룬다.
            string path = item.RepoPath;
            Root.schedule.Execute(() =>
            {
                if (_page == Page.Changes && _changesFiles.SelectFile(path) == false)
                {
                    _changesFiles.ClearSelection();
                }
            });
        }

        private GitDiffTarget TargetOf(FileListItem item)
        {
            return GitDiffTarget.ForCommit(_shown?.Commit.Hash, (GitFileChange)item.Source);
        }

        private void SetMessage(string message)
        {
            (string subject, string body) = GraphCommitDetail.SplitMessage(GitDisplay.Nfc(message));
            _subject.text = subject;
            _body.text = body;
            _body.SetVisible(body.Length > 0);
        }

        /// <summary>
        /// "AUTHOR" 머리, 아바타, 이름 · 이메일, 날짜.
        /// </summary>
        private void BindPerson(VisualElement host, string caption, string name, string email, DateTime date, string commitHash)
        {
            host.Clear();
            host.userData = (name, email, commitHash);
            Label title = new(caption);
            title.AddToClassList("commit-history__person-caption");
            host.Add(title);
            VisualElement row = new();
            row.AddToClassList("commit-history__person-row");
            VisualElement avatar = new();
            avatar.AddToClassList("commit-history__avatar");
            Label initials = new();
            initials.AddToClassList("commit-history__initials");
            avatar.Add(initials);
            row.Add(avatar);
            VisualElement text = new();
            text.AddToClassList("commit-history__person-text");
            VisualElement nameLine = new();
            nameLine.AddToClassList("commit-history__person-name-line");
            Label nameLabel = new(GitDisplay.Nfc(name));
            nameLabel.AddToClassList("commit-history__person-name");
            nameLabel.selection.isSelectable = true;
            nameLine.Add(nameLabel);
            Label emailLabel = new(email);
            emailLabel.AddToClassList("commit-history__person-email");
            emailLabel.selection.isSelectable = true;
            nameLine.Add(emailLabel);
            text.Add(nameLine);
            Label dateLabel = new(GitDisplay.FormatDate(date));
            dateLabel.AddToClassList("commit-history__person-date");
            text.Add(dateLabel);
            row.Add(text);
            host.Add(row);
            BindAvatar(avatar, initials, name, email, commitHash);
        }

        private void BindRefs(GitGraphNode node)
        {
            _refs.Clear();
            // 상세에는 원격까지 모두 늘어놓는다(그래프 줄처럼 합치지 않는다).
            IReadOnlyList<CommitRef> refs = CommitRefs.Of(node, mergeRemotes: false);
            _refs.parent.SetVisible(refs.Count > 0);
            foreach (CommitRef commitRef in refs)
            {
                Label pill = new(commitRef.Name);
                pill.AddToClassList("commit-history__ref");
                pill.AddToClassList("commit-history__ref--" + commitRef.Kind.ToString().ToLowerInvariant());
                _refs.Add(pill);
            }
        }

        private void BindParents(GitCommitInfo commit)
        {
            _parents.Clear();
            _parents.parent.SetVisible(commit.Parents.Length > 0);
            foreach (string parent in commit.Parents)
            {
                Label link = new(GitDisplay.ShortRevision(parent)) { tooltip = GitText.Get("graphDetail.goToParent") };
                link.AddToClassList("graph-detail__parent-link");
                link.RegisterCallback<ClickEvent>(_ => _list.SelectCommit(parent));
                _parents.Add(link);
            }
        }

        /// <summary>
        /// Changes · File Tree 칸 위 한 줄: 아바타, 작성자, 짧은 해시, 날짜, 제목(Fork 와 같다).
        /// </summary>
        private void BindHeaderLine(VisualElement host, GitCommitInfo commit)
        {
            host.Clear();
            host.userData = (commit.AuthorName, commit.AuthorEmail, commit.Hash);
            VisualElement avatar = new();
            avatar.AddToClassList("commit-history__avatar");
            avatar.AddToClassList("commit-history__avatar--small");
            Label initials = new();
            initials.AddToClassList("commit-history__initials");
            avatar.Add(initials);
            host.Add(avatar);
            BindAvatar(avatar, initials, commit.AuthorName, commit.AuthorEmail, commit.Hash);
            AddText(GitDisplay.Nfc(commit.AuthorName), "commit-history__header-author");
            AddText(commit.ShortHash, "commit-history__header-hash");
            AddText(GitDisplay.FormatDate(commit.Date), "commit-history__header-date");
            AddText(GitDisplay.Nfc(commit.Subject), "commit-history__header-subject");

            void AddText(string text, string className)
            {
                Label label = new(text);
                label.AddToClassList(className);
                label.AddToClassList("ellipsis");
                host.Add(label);
            }
        }

        /// <summary>
        /// "✎ 2  + 1  − 1" — 종류마다 상태 색(커밋 상세와 같다).
        /// </summary>
        private static void BindSummary(VisualElement host, IReadOnlyCollection<GitFileChange> files)
        {
            host.Clear();
            AddCount(files.Count(f => f.State == GitFileState.Modified), "graphDetail.modified", "modified", "✎");
            AddCount(files.Count(f => f.State == GitFileState.Added), "graphDetail.added", "added", "+");
            AddCount(files.Count(f => f.State == GitFileState.Deleted), "graphDetail.deleted", "deleted", "−");
            AddCount(files.Count(f => f.State == GitFileState.Renamed), "graphDetail.renamed", "renamed", "→");

            void AddCount(int count, string key, string kind, string sign)
            {
                if (count == 0)
                {
                    return;
                }
                Label label = new($"{sign} {GitText.Format(key, count)}");
                label.AddToClassList("graph-detail__count");
                label.AddToClassList("graph-detail__count--" + kind);
                host.Add(label);
            }
        }

        private void BindAvatar(VisualElement avatar, Label initials, string name, string email, string commitHash)
        {
            Texture2D texture = _avatars != null && _avatars.IsEnabled ? _avatars.TryGet(email, commitHash) : null;
            avatar.style.backgroundImage = texture == null ? StyleKeyword.Null : new StyleBackground(texture);
            avatar.style.backgroundColor = texture == null ? GraphStyle.AuthorColor(name) : Color.clear;
            initials.text = texture == null ? GitDisplay.Initials(name) : string.Empty;
        }

        /// <summary>
        /// 아바타를 받으면 보이는 사람 칸·머리 줄을 다시 칠한다(아바타 요소의 userData 에 이름 · 이메일 · 해시를 둔다).
        /// </summary>
        private void RefreshAvatars()
        {
            foreach (VisualElement host in new[] { _author, _committer, _changesHeader, _treeHeader })
            {
                if (host.userData is ValueTuple<string, string, string> person)
                {
                    VisualElement avatar = host.Q(className: "commit-history__avatar");
                    if (avatar != null)
                    {
                        BindAvatar(avatar, avatar.Q<Label>(), person.Item1, person.Item2, person.Item3);
                    }
                }
            }
        }

        // ── File Tree 칸 ──

        private async void LoadTree()
        {
            if (_shown == null || _treeHash == _shown.Commit.Hash)
            {
                return;
            }
            string hash = _shown.Commit.Hash;
            _treeHash = hash;
            _tree.SetRootItems(new List<TreeViewItemData<GitPathNode>>());
            _tree.Rebuild();
            ShowContent(null, null);
            (List<string> paths, string error) = await GitCommitTree.ListFilesAsync(hash);
            if (_treeHash != hash)
            {
                return;
            }
            if (error != null)
            {
                ShowContent(null, error);
                return;
            }
            int nextId = 0;
            _treeIds.Clear();
            _tree.SetRootItems(GitPathTree.Build(paths).Select(ToItem).ToList());
            _tree.Rebuild();
            RevealPendingTreePath();

            TreeViewItemData<GitPathNode> ToItem(GitPathNode node)
            {
                int id = nextId++;
                _treeIds[node.Path] = id;
                return new TreeViewItemData<GitPathNode>(id, node, node.IsFolder ? node.Children.Select(ToItem).ToList() : null);
            }
        }

        /// <summary>
        /// File Tree 칸으로 넘어가 그 파일을 펼쳐 고른다(트리를 아직 안 읽었으면 읽은 뒤).
        /// </summary>
        private void ShowInFileTree(string repoPath)
        {
            _pendingTreePath = repoPath;
            SelectPage(Page.Tree);
            if (_treeHash == _shown?.Commit.Hash && _treeIds.Count > 0)
            {
                RevealPendingTreePath();
            }
        }

        private void RevealPendingTreePath()
        {
            string path = _pendingTreePath;
            if (path == null || _treeIds.TryGetValue(path, out int id) == false)
            {
                return;
            }
            _pendingTreePath = null;
            // 위 폴더부터 차례로 펼친다.
            for (int slash = path.IndexOf('/'); slash > 0; slash = path.IndexOf('/', slash + 1))
            {
                if (_treeIds.TryGetValue(path.Substring(0, slash), out int folder))
                {
                    _tree.ExpandItem(folder);
                }
            }
            _tree.SetSelectionById(id);
            _tree.ScrollToItemById(id);
        }

        private VisualElement MakeTreeRow()
        {
            VisualElement row = new();
            row.AddToClassList("commit-history__tree-row");
            // Fork 의 File Tree 처럼 우클릭으로 열기 · Finder · 히스토리 · Blame · 이 버전 저장 · 경로 복사.
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (row.userData is GitPathNode node)
                {
                    PopulateTreeMenu(evt.menu, node);
                }
            }));
            Image icon = new();
            icon.AddToClassList("commit-history__tree-icon");
            row.Add(icon);
            Label label = new();
            label.AddToClassList("commit-history__tree-label");
            label.AddToClassList("ellipsis");
            row.Add(label);
            return row;
        }

        private void BindTreeRow(VisualElement row, int index)
        {
            GitPathNode node = _tree.GetItemDataForIndex<GitPathNode>(index);
            Image icon = row.Q<Image>();
            icon.image = GitTheme.SkinIcon(node.IsFolder ? EditorGUIUtility.IconContent("Folder Icon").image : InternalEditorUtility.GetIconForFile(node.Name));
            row.Q<Label>().text = node.Name;
            row.tooltip = node.Path;
            row.userData = node;
        }

        /// <summary>
        /// File Tree 우클릭 메뉴. 여는 항목은 작업 폴더에 그 파일이 지금 있을 때만 켠다(그 커밋에만 있던 파일은 "이 버전 저장" 으로 꺼낸다).
        /// </summary>
        private void PopulateTreeMenu(DropdownMenu menu, GitPathNode node)
        {
            string fullPath = CommitFileMenu.FullPath(node.Path);
            if (node.IsFolder)
            {
                menu.AppendAction(GitFileManager.OpenLabel, _ => GitFileManager.OpenFolder(fullPath), CommitFileMenu.StatusOf(Directory.Exists(fullPath)));
                menu.AppendSeparator();
                menu.AppendAction(GitText.Get("history.copyFolderPath"), _ => Services.Assets.CopyToClipboard(node.Path));
                return;
            }
            bool exists = File.Exists(fullPath);
            menu.AppendAction(GitText.Get("menu.externalEditor"), _ => InternalEditorUtility.OpenFileAtLineExternal(fullPath, 1), CommitFileMenu.StatusOf(exists));
            menu.AppendAction(GitText.Get("menu.defaultProgram"), _ => EditorUtility.OpenWithDefaultApp(fullPath), CommitFileMenu.StatusOf(exists));
            menu.AppendAction(GitFileManager.RevealLabel, _ => GitFileManager.Reveal(fullPath), CommitFileMenu.StatusOf(exists));
            menu.AppendSeparator();
            menu.AppendAction(GitText.Get("menu.fileHistory"), _ => Services.OpenFileHistory(node.Path));
            menu.AppendAction(GitText.Get("menu.fileBlame"), _ => GitBlameWindow.Open(node.Path), CommitFileMenu.StatusOf(exists));
            menu.AppendSeparator();
            menu.AppendAction(GitText.Get("history.saveRevisionAs"), _ => _fileMenu.SaveRevisionAs(_shown.Commit, node.Path), CommitFileMenu.StatusOf(_shown != null));
            menu.AppendSeparator();
            menu.AppendAction(GitText.Get("menu.copyPath"), _ => Services.Assets.CopyToClipboard(node.Path));
        }

        private async void ShowSelectedFile()
        {
            GitPathNode node = _tree.selectedItem as GitPathNode;
            if (node == null || node.IsFolder || _shown == null)
            {
                return;
            }
            string hash = _shown.Commit.Hash;
            string key = hash + ":" + node.Path;
            _contentKey = key;
            _contentPath.text = node.Path;
            ShowContent(null, GitText.Get("common.loading"));
            GitFileContent content = await GitCommitTree.FileContentAsync(hash, node.Path);
            if (_contentKey != key)
            {
                return;
            }
            if (content.Error != null)
            {
                ShowContent(null, content.Error);
            }
            else if (content.IsBinary)
            {
                ShowContent(null, GitText.Get("history.binaryFile"));
            }
            else
            {
                ShowContent(GitCommitTree.SplitLines(content.Text), content.IsTruncated ? GitText.Format("history.truncated", GitCommitTree.MaxPreviewChars) : null);
            }
        }

        /// <param name="lines">null 이면 내용을 비운다.</param>
        /// <param name="notice">내용 위(또는 대신) 보일 안내. null 이면 숨긴다.</param>
        private void ShowContent(string[] lines, string notice)
        {
            if (lines == null && notice == null)
            {
                _contentPath.text = string.Empty;
            }
            _contentLines = lines ?? Array.Empty<string>();
            _content.itemsSource = _contentLines;
            _content.Rebuild();
            _content.SetVisible(lines != null);
            _contentNotice.text = notice ?? string.Empty;
            _contentNotice.SetVisible(notice != null);
        }

        private static VisualElement MakeContentRow()
        {
            VisualElement row = new();
            row.AddToClassList("commit-history__line");
            Label number = new();
            number.AddToClassList("commit-history__line-number");
            row.Add(number);
            Label text = new();
            text.AddToClassList("commit-history__line-text");
            row.Add(text);
            return row;
        }

        private void BindContentRow(VisualElement row, int index)
        {
            ((Label)row[0]).text = (index + 1).ToString();
            // 탭은 공백 넷으로(글꼴마다 탭 폭이 달라 줄이 어긋나지 않게).
            ((Label)row[1]).text = _contentLines[index].Replace("\t", "    ");
        }

        // ── 목록 ──

        private void PopulateCommitMenu(DropdownMenu menu, GitGraphNode node)
        {
            string[] parents = node.Commit.Parents;
            GitGraphNode parent = parents.Length > 0 && _list.Graph != null && _list.Graph.NodeByHash.TryGetValue(parents[0], out GitGraphNode found) ? found : null;
            _commitMenu.Populate(menu, node.Commit, new CommitMenu.Hooks
            {
                GoToParent = parent == null ? null : () => _list.SelectCommit(parent.Commit.Hash),
                Branches = _list.Graph?.Branches,
            });
        }

        /// <summary>
        /// 최신 커밋부터 제목 · 작성자 · 해시가 검색어를 담은 다음 커밋을 고른다.
        /// </summary>
        private void FocusNextMatch()
        {
            GitGraph graph = _list.Graph;
            string needle = _state.Search?.Trim();
            if (graph == null || graph.Nodes.Count == 0 || string.IsNullOrEmpty(needle))
            {
                return;
            }
            // 목록은 최신이 위다. 그래프 노드 순서(오래된 것부터)를 거꾸로 돈다.
            int count = graph.Nodes.Count;
            for (int step = 1; step <= count; step++)
            {
                int index = ((_searchCursor < 0 ? count : _searchCursor) - step + count) % count;
                GitCommitInfo commit = graph.Nodes[index].Commit;
                if (Contains(commit.Subject) || Contains(commit.AuthorName) || commit.Hash.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                {
                    _searchCursor = index;
                    _list.SelectCommit(commit.Hash);
                    return;
                }
            }
            Services.Runner.SetStatus(GitText.Format("explorer.noMatch", needle));

            bool Contains(string text) => (text ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
