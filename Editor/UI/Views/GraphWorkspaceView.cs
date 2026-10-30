using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitKraken 형태의 작업 탭.
    /// 가운데: 세로 커밋 그래프(<see cref="CommitGraphList"/>). 오른쪽 파일을 고르면 그 자리에 diff 가 열리고 × 로 그래프로 돌아온다.
    /// 오른쪽: 맨 위 "작업 중" 줄을 고르면 Unstaged·Staged·커밋(푸시) 패널, 커밋 줄을 고르면 그 커밋의 메시지·바뀐 파일.
    /// </summary>
    public sealed class GraphWorkspaceView : GitView, ICommitRevealer, IFileHistoryPresenter
    {
        /// <summary>
        /// 한 번에 읽을 커밋 수. 세로 목록은 가상화라 그리기 부담은 적고, git log·그래프 배치(작업 스레드)가 커밋 수에 비례한다.
        /// 커밋 ~4500·브랜치 3000 저장소에서 읽기~배치 ~0.3초, 스크롤 한 장 ~4ms(2026-10-18 실측).
        /// </summary>
        private const int MaxCommits = 5000;

        private readonly ITabNavigator _navigator;
        private readonly Label _summary;
        private readonly VisualElement _errorNotice;
        private readonly Label _errorText;
        private readonly VisualElement _graphHost;
        private readonly VisualElement _diffHost;
        private readonly VisualElement _wipPanel;
        private readonly VisualElement _detailHost;
        private readonly Label _wipTitle;
        private readonly Button _discardAll;
        private readonly CommitGraphList _graph;
        private readonly GitDiffPanel _diff;
        private readonly StagingLists _staging;
        private readonly CommitBox _commit;
        private readonly GraphCommitDetail _detail;
        private readonly CommitMenu _commitMenu;
        private readonly CommitActions _actions;
        private readonly BranchActions _branchActions;

        private string _loadedKey;
        private bool _isLoading;
        private bool _isDiffShown;

        /// <summary>
        /// 그래프·작업 패널(가운데 split)과 그것을 통째로 덮는 파일 히스토리(GitKraken 의 File History).
        /// </summary>
        private readonly VisualElement _split;
        private readonly VisualElement _historyHost;
        private readonly FileHistoryPanel _history;
        private bool _isHistoryShown;

        /// <summary>
        /// 그래프를 다 읽기 전에 보여 달라고 한 커밋. 읽은 뒤 고른다.
        /// </summary>
        private string _pendingReveal;

        /// <summary>
        /// 그래프를 다 읽기 전에 브랜치를 만들려고 한 커밋. 읽은 뒤 그 줄에 이름 입력란을 띄운다.
        /// </summary>
        private string _pendingBranchStart;

        /// <summary>
        /// 가운데에 파일 diff 를 열면 true(파일을 고를 때마다), × 로 닫고 그래프로 돌아오면 false.
        /// 창은 GitKraken 처럼 diff 를 여는 동안 왼쪽 사이드바를 아이콘 줄로 접는다.
        /// </summary>
        public event Action<bool> DiffShownChanged;

        public override string Title => GitText.Get("tab.graph");

        public override bool HasLoaded => _loadedKey != null && _isLoading == false;

        public override System.Collections.Generic.IEnumerable<(VisualElement Host, SkeletonShape Shape)> SkeletonHosts
        {
            get
            {
                yield return (Root.Require<VisualElement>("graph-host"), SkeletonShape.Commits);
                yield return (Root.Require<VisualElement>("staging-host"), SkeletonShape.Files);
            }
        }
        public override string Badge => Services.Repository.Entries.Count > 0 ? Services.Repository.Entries.Count.ToString() : null;

        private string CurrentKey => $"{Services.Repository.HeadCommit}|{Services.Repository.UpstreamCommit}|{Services.Repository.Branch?.Name}|{Services.Repository.LastFetchTime}|{Services.Repository.RefsFingerprint}";

        public GraphWorkspaceView(GitServices services, StagingState state, ITabNavigator navigator) : base(services, "GraphWorkspaceView")
        {
            _navigator = navigator;
            _summary = Root.Require<Label>("summary");
            _errorNotice = Root.Require<VisualElement>("error-notice");
            _errorText = Root.Require<Label>("error-text");
            _graphHost = Root.Require<VisualElement>("graph-host");
            _diffHost = Root.Require<VisualElement>("diff-host");
            _wipPanel = Root.Require<VisualElement>("wip-panel");
            _detailHost = Root.Require<VisualElement>("detail-host");
            _wipTitle = Root.Require<Label>("wip-title");
            _discardAll = Root.Require<Button>("discard-all");
            _discardAll.tooltip = GitText.Get("graph.discardAll");
            _discardAll.clicked += DiscardAll;

            _errorNotice.SetVisible(false);
            _graph = new CommitGraphList(_graphHost, services.Avatars);
            _diff = new GitDiffPanel(services.Diff);
            _diff.Root.style.flexGrow = 1;
            _diff.ShowCloseButton(true);
            _diff.ShowReloadButton(false);
            // GitKraken 처럼 diff 의 거터 + 버튼·hunk 버튼으로 줄 단위 Stage·Unstage.
            _diff.EnablePartialStaging(services.Runner, services.Commands, services.Dialogs);
            // "이 파일 편집" 의 저장 확인은 운영체제 대화상자 대신 Git 창 위 팝업으로.
            _diff.PopupHostProvider = () => services.PopupHost;
            _diffHost.Add(_diff.Root);
            _staging = new StagingLists(Root.Require<VisualElement>("staging-host"), services);
            _commit = new CommitBox(Root.Require<VisualElement>("commit-host"), services, state, showPush: true);
            _detail = new GraphCommitDetail(_detailHost, services);
            _detail.FileSelected += target =>
            {
                _staging.ClearSelection();
                ShowDiff(target);
            };
            _detail.ParentRequested += hash => _graph.SelectCommit(hash);
            _commitMenu = new CommitMenu(services);
            _actions = new CommitActions(services);
            _branchActions = new BranchActions(services);

            // GitKraken 처럼 작업 중 줄의 입력란과 커밋 입력란의 제목은 같은 글이다. 어느 쪽에 써도 다른 쪽에 보인다.
            _graph.SetWipSubject(_commit.Subject);
            _commit.SubjectChanged += () => _graph.SetWipSubject(_commit.Subject);
            _graph.WipSubjectEdited += _commit.SetSubject;
            _graph.WipSelected += ShowWip;
            _graph.CommitSelected += ShowCommit;
            _graph.ContextMenuRequested += PopulateCommitMenu;
            _graph.RefActivated += SwitchToRef;
            _staging.FileSelected += ShowDiff;
            // 누르자마자 옮겨 보인 목록(짐작)에 맞춰 커밋 상자의 stage 개수도 바로 바꾼다.
            _staging.EntriesShown += () => _commit.SetStagedCount(_staging.StagedCount);
            _diff.CloseRequested += CloseDiff;

            // 파일 히스토리는 따로 창을 띄우지 않고 가운데를 덮어 크게 보인다. diff 막대의 "히스토리" 도 여기로 온다.
            _split = Root.Require<VisualElement>("split");
            _historyHost = Root.Require<VisualElement>("history-host");
            _history = new FileHistoryPanel(services);
            _history.Root.style.flexGrow = 1;
            _history.CloseRequested += CloseFileHistory;
            _historyHost.Add(_history.Root);
            _historyHost.SetVisible(false);
            _diff.FileHistoryPresenter = TryShowFileHistory;
            _history.Diff.FileHistoryPresenter = TryShowFileHistory;

            ShowDiff(null);
            ShowWip();
        }

        /// <summary>
        /// 파일 히스토리를 가운데에 크게 연다(GitKraken 의 File History). 다른 탭에서 불렀으면 Graph 탭으로 옮겨 연다.
        /// 여는 동안은 diff 처럼 왼쪽 사이드바를 아이콘 줄로 접는다.
        /// </summary>
        public bool TryShowFileHistory(string repoPath)
        {
            if (string.IsNullOrEmpty(repoPath))
            {
                return false;
            }
            if (IsActive == false)
            {
                _navigator?.SelectTab<GraphWorkspaceView>();
            }
            bool wasCollapsed = _isDiffShown || _isHistoryShown;
            _isHistoryShown = true;
            _split.SetVisible(false);
            _historyHost.SetVisible(true);
            _history.Show(repoPath);
            if (wasCollapsed == false)
            {
                DiffShownChanged?.Invoke(true);
            }
            return true;
        }

        /// <summary>
        /// 파일 히스토리를 닫고 그래프로 돌아온다(× 또는 Esc). GitKraken 처럼 히스토리를 열기 전에 가운데에 diff 가 열려 있었어도
        /// 그 diff 로 돌아가지 않고 그래프로 돌아가며, 접어 둔 왼쪽 사이드바를 다시 편다.
        /// </summary>
        public void CloseFileHistory()
        {
            if (_isHistoryShown == false)
            {
                return;
            }
            _isHistoryShown = false;
            _historyHost.SetVisible(false);
            _split.SetVisible(true);
            _staging.ClearSelection();
            _detail.ClearFileSelection();
            ShowDiff(null);
            // 히스토리만 열었던 경우(diff 없이)에도 사이드바를 편다. 이미 펴져 있으면 창이 아무것도 하지 않는다.
            DiffShownChanged?.Invoke(false);
        }

        protected override void OnActivated()
        {
            RefreshWorkingTree();
            ReloadIfNeeded();
        }

        /// <summary>마지막으로 본 진행 중 작업. 병합·cherry-pick 이 멈추는 순간을 알아 충돌 목록을 보인다.</summary>
        private GitOperationKind _seenOperation;

        public override void OnRepositoryChanged()
        {
            // 병합·리베이스가 충돌로 멈추면 오른쪽을 커밋 상세에서 작업 변경(충돌 파일)으로 바꾼다.
            GitOperationKind operation = Services.Repository.InProgressOperation;
            if (operation != _seenOperation)
            {
                _seenOperation = operation;
                if (operation != GitOperationKind.None)
                {
                    ShowWorkingChanges();
                }
            }
            // 보이지 않으면 작업 패널 목록·그래프를 다시 만들지 않는다 — 탭을 열 때(OnActivated) 만든다. 탭 옆 개수는 바로 바꾼다.
            if (IsActive == false)
            {
                NotifyBadgeChanged();
                return;
            }
            // 그래프 git 읽기를 먼저 백그라운드로 시작해 두고 작업 목록을 다시 그린다. 반대 순서면 목록 그리기(파일이 많으면 수백 ms)가
            // 끝날 때까지 git 이 시작조차 하지 않아 커밋 뒤 그래프가 그만큼 늦게 바뀐다.
            ReloadIfNeeded();
            RefreshWorkingTree();
        }

        public override void OnBusyChanged(bool isBusy)
        {
            _staging.OnBusyChanged();
            _commit.Render();
            UpdateDiscardAll();
        }

        public override void Reload()
        {
            if (_diff.Target != null)
            {
                _diff.Reload();
            }
            if (_isLoading == false)
            {
                Load();
            }
        }

        public override void Dispose()
        {
            _staging.Dispose();
            _detail.Dispose();
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
            UpdateDiscardAll();
            if (_graph.Graph != null)
            {
                _graph.SetGraph(_graph.Graph, WipCounts.From(entries));
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
            _summary.text = GitText.Get("common.loading");
            string key = CurrentKey;

            (GitGraph graph, string error) = await GraphLoader.LoadAsync(Services.History, Services.Repository.HeadCommit, null, MaxCommits);
            _isLoading = false;
            _loadedKey = key;

            _errorNotice.SetVisible(string.IsNullOrEmpty(error) == false);
            _errorText.text = error ?? string.Empty;

            _graph.SetGraph(graph, WipCounts.From(Services.Repository.Entries));
            _summary.text = GitText.Format("explorer.summary", graph.Nodes.Count, graph.Lanes.Count)
                            + (graph.Nodes.Count >= MaxCommits ? GitText.Format("explorer.capped", MaxCommits) : string.Empty);

            if (_pendingReveal != null)
            {
                _graph.SelectCommit(_pendingReveal);
                _pendingReveal = null;
            }
            if (_pendingBranchStart != null)
            {
                string start = _pendingBranchStart;
                _pendingBranchStart = null;
                if (BeginCreateBranch(start) == false)
                {
                    _ = _actions.CreateBranchWithDialogAsync(start, start.Length > 7 ? start.Substring(0, 7) : start);
                }
            }

            // 기다리는 동안 저장소가 또 바뀌었으면 한 번 더 읽는다.
            ReloadIfNeeded();
        }

        public bool RevealCommit(string hash)
        {
            if (_isLoading || _graph.Graph == null)
            {
                _pendingReveal = hash;
                return true;
            }
            return _graph.SelectCommit(hash);
        }

        /// <summary>
        /// GitKraken 처럼 그 커밋 줄의 브랜치/태그 칸에 이름 입력란을 띄운다. Enter 면 만들고 바로 전환한다.
        /// 가운데에 diff 가 열려 있으면 그래프로 돌아온다. 그래프를 읽는 중이면 다 읽은 뒤 띄운다.
        /// </summary>
        /// <returns>그래프에 없는 커밋이면 false(부른 쪽이 대화상자로 묻는다).</returns>
        public bool BeginCreateBranch(string startPoint)
        {
            _staging.ClearSelection();
            _detail.ClearFileSelection();
            ShowDiff(null);
            if (_isLoading || _graph.Graph == null)
            {
                _pendingBranchStart = startPoint;
                return true;
            }
            List<string> existing = new();
            _graph.BranchNameValidator = value => BranchActions.BranchNameProblemText(value, existing);
            // 같은 이름 검사용 로컬 브랜치 목록은 뒤에서 읽어 채운다(입력란은 바로 띄운다).
            _ = FillLocalBranchNamesAsync(existing);
            return _graph.BeginBranchInput(startPoint, name => _ = _actions.CreateAndSwitchAsync(name, startPoint));
        }

        private async Task FillLocalBranchNamesAsync(List<string> target)
        {
            (List<GitBranchRef> branches, _) = await Services.History.BranchesAsync();
            target.AddRange(branches?.Where(b => b.IsRemote == false).Select(b => b.Name) ?? Enumerable.Empty<string>());
        }

        /// <summary>
        /// 버릴 변경이 없거나 다른 git 작업 중이면 휴지통을 끈다.
        /// </summary>
        private void UpdateDiscardAll()
        {
            _discardAll.SetEnabled(Services.Repository.Entries.Count > 0 && Services.Runner.IsBusy == false);
        }

        /// <summary>
        /// 작업 중인 변경을 모두 버린다(GitKraken 의 휴지통). Stage 한 것까지 마지막 커밋 상태로 되돌리고 새 파일은 지운다.
        /// 되돌릴 수 없으므로 개수와 앞쪽 파일을 보여 주고 한 번 더 묻는다.
        /// </summary>
        private async void DiscardAll()
        {
            List<GitStatusEntry> entries = Services.Repository.Entries.ToList();
            if (entries.Count == 0 || Services.Runner.IsBusy)
            {
                return;
            }
            string list = string.Join("\n", entries.Take(10).Select(e => e.ProjectPath ?? e.RepoPath))
                          + (entries.Count > 10 ? GitText.Format("pending.discardMore", entries.Count - 10) : string.Empty);
            string message = GitText.Format("graph.discardAllMessage", Services.Repository.Branch?.Name ?? "-", entries.Count, list);
            if (await Services.Dialogs.ConfirmAsync(GitText.Get("graph.discardAll"), message, GitText.Get("op.discard"), danger: true, icon: ActionGlyphKind.Undo) == false)
            {
                return;
            }
            await Services.Runner.RunAsync(GitText.Get("op.discard"), () => Services.Commands.DiscardAsync(entries), refreshAssets: true);
        }

        /// <summary>오른쪽을 작업 변경(Unstaged·Staged·커밋)으로 바꾼다.</summary>
        public void ShowWorkingChanges()
        {
            CloseFileHistory();
            ShowWip();
        }

        public override bool ShowsChanges => true;

        /// <summary>충돌 안내의 "충돌 보기": 작업 변경으로 바꾸고 첫 충돌 파일의 diff 를 연다.</summary>
        public override void ShowConflicts()
        {
            ShowWorkingChanges();
            _staging.SelectFirstConflict();
        }

        public override bool RevealChange(string repoPath)
        {
            ShowWorkingChanges();
            return _staging.SelectEntry(repoPath);
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
                _detail.Show(node.Commit);
            }
        }

        /// <summary>
        /// 가운데의 diff 를 닫고 그래프로 돌아온다(× 와 같다). 고른 파일 표시도 지운다.
        /// </summary>
        public void CloseDiff()
        {
            if (_isHistoryShown)
            {
                // 히스토리를 닫으면 그 아래 diff 도 같이 닫힌다.
                CloseFileHistory();
                return;
            }
            _staging.ClearSelection();
            _detail.ClearFileSelection();
            ShowDiff(null);
        }

        /// <summary>
        /// 가운데를 diff 로 바꾼다. null 이면 그래프로 돌아간다.
        /// </summary>
        private void ShowDiff(GitDiffTarget target)
        {
            // diff 에서 파일을 고치는 중이면 닫기 전에 저장 안 된 편집을 묻는다(Git 창 위 팝업). 저장·버리기 뒤에 닫고, 취소하면 diff 를 그대로 둔다.
            if (target == null && _isDiffShown && _diff.IsEditing)
            {
                _diff.LeaveEdit(() => ShowDiff(null));
                return;
            }
            bool wasShown = _isDiffShown;
            _isDiffShown = target != null;
            _diffHost.SetVisible(target != null);
            _graphHost.SetVisible(target == null);
            if (target != null)
            {
                _diff.SetTarget(target);
                DiffShownChanged?.Invoke(true);
            }
            else if (wasShown && _isHistoryShown == false)
            {
                // 히스토리가 덮고 있는 동안은 사이드바를 접어 둔 채로 둔다(히스토리를 닫을 때 편다).
                DiffShownChanged?.Invoke(false);
            }
        }

        /// <summary>
        /// 이름표를 두 번 눌렀을 때(GitKraken 과 같다): 브랜치는 체크아웃(원격은 같은 이름의 로컬이 있으면 그쪽, 없으면 추적 브랜치를 만든다),
        /// 태그는 그 커밋으로 전환(분리된 HEAD). 이미 현재 브랜치거나 다른 git 작업 중이면 하지 않는다.
        /// </summary>
        private void SwitchToRef(GitGraphNode node, CommitRef commitRef)
        {
            if (Services.Runner.IsBusy || commitRef.Kind == CommitRefKind.Current)
            {
                return;
            }
            if (commitRef.Kind == CommitRefKind.Tag)
            {
                _ = _actions.SwitchToAsync(node.Commit);
                return;
            }
            IReadOnlyList<GitBranchRef> branches = _graph.Graph?.Branches ?? new List<GitBranchRef>();
            GitBranchRef branch = branches.FirstOrDefault(b => b.Name == commitRef.Name)
                                  ?? new GitBranchRef { Name = commitRef.Name, IsRemote = commitRef.Kind == CommitRefKind.Remote };
            if (branch.IsCurrent)
            {
                return;
            }
            HashSet<string> localNames = new(branches.Where(b => b.IsRemote == false).Select(b => b.Name));
            _ = _branchActions.SwitchAsync(branch, localNames);
        }

        /// <summary>
        /// Branch Explorer·Changesets 와 같은 커밋 메뉴(<see cref="CommitMenu"/>). 그래프가 읽어 둔 브랜치 목록을 넘겨 이 커밋의 브랜치 항목을 만든다.
        /// </summary>
        private void PopulateCommitMenu(DropdownMenu menu, GitGraphNode node)
        {
            GitCommitInfo commit = node.Commit;
            string parent = commit.Parents.Length > 0 && _graph.Graph != null && _graph.Graph.NodeByHash.ContainsKey(commit.Parents[0]) ? commit.Parents[0] : null;
            _commitMenu.Populate(menu, commit, new CommitMenu.Hooks
            {
                GoToParent = parent == null ? null : () => _graph.SelectCommit(parent),
                ShowInExplorer = () => _navigator.SelectTab<ExplorerView>()?.Reveal(commit.Hash),
                Branches = _graph.Graph?.Branches,
            });
        }
    }
}
