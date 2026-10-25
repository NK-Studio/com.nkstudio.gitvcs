using System.IO;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 창 위쪽 작업 막대(<c>.git-actionbar</c>). GitKraken·Fork 처럼 아이콘+글자 버튼을 묶어 둔다:
    /// 저장소·브랜치(누르면 검색해 고르는 브랜치 목록 — <see cref="GitBranchPicker"/>) | 가운데: 실행 취소·다시 실행 | Fetch·Pull·Push | 새 브랜치 | Stash·Pop | 오른쪽: 진행 상태 | 터미널·새로고침.
    /// Pull·Push·Pop 버튼에는 받을/올릴 커밋 수와 stash 수를 숫자로 겹쳐 보인다.
    /// </summary>
    public sealed class GitToolbar
    {
        private const string CompactModifier = "git-actionbar--compact";

        /// <summary>
        /// 이 폭보다 좁으면 버튼 글자를 숨긴다. 저장소·브랜치 칸(약 300) + 버튼 10개(각 54)가 한 줄에 들어가는 폭.
        /// </summary>
        private const float CompactWidth = 840f;

        private readonly GitServices _services;
        private readonly VisualElement _bar;
        private readonly Label _repoCaption;
        private readonly Button _repoButton;
        private readonly Button _branchButton;
        private readonly Label _status;
        private readonly ActionButton _undo;
        private readonly ActionButton _redo;
        private readonly ActionButton _fetch;
        private readonly ActionButton _pull;
        private readonly ActionButton _push;
        private readonly ActionButton _newBranch;
        private readonly ActionButton _stash;
        private readonly ActionButton _pop;
        private readonly ActionButton _terminal;
        private readonly ActionButton _refresh;

        private int _stashCount;
        private bool _isCountingStash;

        /// <summary>새로고침 버튼을 눌렀을 때. 창이 보고 있는 탭에 다시 읽기를 전한다.</summary>
        public event System.Action RefreshRequested;

        public GitToolbar(VisualElement root, GitServices services, ITabNavigator navigator)
        {
            _services = services;
            _bar = root.Require<VisualElement>("action-bar");
            _repoCaption = root.Require<Label>("repo-caption");
            _repoButton = root.Require<Button>("repo-button");
            _branchButton = root.Require<Button>("branch-button");
            _status = root.Require<Label>("status-label");
            // 실행 취소·다시 실행은 Material Symbols 그림(Icons/Undo.png·Redo.png)을 쓴다. 색은 USS 가 글자색으로 물들인다.
            _undo = new ActionButton(root.Require<Button>("undo-button"), "git-action__icon--undo");
            _redo = new ActionButton(root.Require<Button>("redo-button"), "git-action__icon--redo");
            _fetch = new ActionButton(root.Require<Button>("fetch-button"), ActionGlyphKind.Fetch);
            _pull = new ActionButton(root.Require<Button>("pull-button"), ActionGlyphKind.Pull);
            _push = new ActionButton(root.Require<Button>("push-button"), ActionGlyphKind.Push);
            _newBranch = new ActionButton(root.Require<Button>("new-branch-button"), ActionGlyphKind.Branch);
            _stash = new ActionButton(root.Require<Button>("stash-button"), ActionGlyphKind.Stash);
            _pop = new ActionButton(root.Require<Button>("pop-button"), ActionGlyphKind.Pop);
            _terminal = new ActionButton(root.Require<Button>("terminal-button"), ActionGlyphKind.Terminal);
            _refresh = new ActionButton(root.Require<Button>("refresh-button"), ActionGlyphKind.Refresh);

            IGitOperationRunner runner = services.Runner;
            IGitCommandService commands = services.Commands;
            // 저장소 이름도 ▾ 를 붙여 누르면 프로젝트 저장소와 서브모듈 중에서 고른다(Fork 처럼 서브모듈을 연다).
            VisualElement repoContext = _repoButton.parent;
            VisualElement repoRow = WrapWithCaret(_repoButton, out ActionGlyph repoCaret);
            void OpenRepositoryPicker()
            {
                GitRepositoryPicker.Show(services, repoContext, open => repoRow.EnableInClassList("git-actionbar__branch-row--open", open));
            }
            _repoButton.clicked += OpenRepositoryPicker;
            repoCaret.RegisterCallback<ClickEvent>(_ => OpenRepositoryPicker());
            // GitKraken 처럼 브랜치 이름을 누르면 그 아래에 검색해 고르는 목록을 펼친다. 띄울 곳이 없으면 예전처럼 사이드바 참조 트리로 간다.
            // 이름 뒤 ▾ 는 버튼 안이 아니라 옆에 둔다 — 글자 요소(Button)에 자식을 넣으면 글자 폭을 재지 않아 이름이 사라진다.
            VisualElement branchContext = _branchButton.parent;
            VisualElement branchRow = WrapWithCaret(_branchButton, out ActionGlyph caret);
            void OpenBranchPicker()
            {
                if (GitBranchPicker.Show(services, branchContext, open => branchRow.EnableInClassList("git-actionbar__branch-row--open", open)) == false)
                {
                    navigator.FocusRefs();
                }
            }
            _branchButton.clicked += OpenBranchPicker;
            // 마우스를 올리면 누르기 전에 목록을 새로 읽기 시작한다(이미 읽는 중이면 그대로).
            branchRow.RegisterCallback<PointerEnterEvent>(_ => GitBranchPicker.Prefetch());
            caret.RegisterCallback<ClickEvent>(_ => OpenBranchPicker());
            _undo.Clicked += () => _ = runner.RunAsync(GitText.Get("op.undo"), commands.UndoAsync, refreshAssets: true);
            _redo.Clicked += () => _ = runner.RunAsync(GitText.Get("op.redo"), commands.RedoAsync, refreshAssets: true);
            _fetch.Clicked += async () =>
            {
                GitResult result = await runner.RunAsync(GitText.Get("op.fetch"), commands.FetchAsync, refreshAssets: false);
                if (result.Success)
                {
                    GitAutoFetch.ClearAuthFailure();
                    Render();
                }
            };
            _pull.Clicked += () => _ = runner.RunAsync(GitText.Get("op.pull"), commands.PullAsync, refreshAssets: true);
            _push.Clicked += () =>
            {
                GitBranchInfo branch = services.Repository.Branch;
                _ = runner.RunAsync(GitText.Get("op.push"), () => commands.PushAsync(branch), refreshAssets: false);
            };
            BranchActions branchActions = new(services);
            _newBranch.Clicked += () => _ = CreateBranchAsync(branchActions);
            _stash.Clicked += Stash;
            _pop.Clicked += () => _ = runner.RunAsync(GitText.Get("op.pop"), commands.StashPopAsync, refreshAssets: true);
            _terminal.Clicked += OpenTerminal;
            _refresh.Clicked += () =>
            {
                services.Repository.RequestRefresh(immediate: true);
                RefreshRequested?.Invoke();
            };

            commands.UndoJournal.Changed += Render;
            GitAutoFetch.Completed += OnAutoFetchCompleted;
            _bar.RegisterCallback<GeometryChangedEvent>(evt => _bar.EnableInClassList(CompactModifier, evt.newRect.width < CompactWidth));
            _bar.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                commands.UndoJournal.Changed -= Render;
                GitAutoFetch.Completed -= OnAutoFetchCompleted;
            });
        }

        private void OnAutoFetchCompleted(GitAutoFetchOutcome outcome)
        {
            Render();
        }

        /// <summary>
        /// 이름 버튼을 ▾ 와 한 줄로 감싼다. ▾ 는 버튼 안이 아니라 옆에 둔다 — 글자 요소(Button)에 자식을 넣으면 글자 폭을 재지 않아 이름이 사라진다.
        /// </summary>
        private static VisualElement WrapWithCaret(Button button, out ActionGlyph caret)
        {
            VisualElement row = new();
            row.AddToClassList("git-actionbar__branch-row");
            VisualElement context = button.parent;
            context.Insert(context.IndexOf(button), row);
            row.Add(button);
            caret = new ActionGlyph(ActionGlyphKind.Caret) { pickingMode = PickingMode.Position };
            caret.AddToClassList("git-actionbar__caret");
            row.Add(caret);
            return row;
        }

        public void Render()
        {
            IGitRepositoryState repository = _services.Repository;
            GitBranchInfo branch = repository.Branch;
            _repoButton.text = string.IsNullOrEmpty(GitRepository.RepoRoot) ? "-" : Path.GetFileName(GitRepository.RepoRoot.TrimEnd('/'));
            _repoButton.tooltip = GitRepository.RepoRoot;
            // 서브모듈을 열었으면 칸 이름도 "서브모듈" 로 바꿔, 프로젝트 저장소가 아님을 바로 알게 한다.
            _repoCaption.text = GitText.Get(GitRepository.OpenedRoot == null ? "actionbar.repository" : "actionbar.submodule");
            _branchButton.text = branch == null ? "-" : branch.Detached ? "HEAD (detached)" : GitDisplay.Nfc(branch.Name);
            _branchButton.tooltip = GitDisplay.FormatBranch(branch);
            _status.text = _services.Runner.StatusText;
            _status.tooltip = _services.Runner.StatusText;

            bool idle = _services.Runner.IsBusy == false;
            bool ready = repository.IsRunning && repository.IsRepository;
            bool hasUpstream = string.IsNullOrEmpty(branch?.Upstream) == false;
            int behind = branch?.Behind ?? 0;
            int ahead = branch?.Ahead ?? 0;

            GitUndoJournal journal = _services.Commands.UndoJournal;
            _undo.SetEnabled(idle && ready && journal.NextUndo != null);
            _undo.Tooltip = journal.NextUndo != null ? GitText.Format("actionbar.undoTooltip", journal.NextUndo.Title) : GitText.Get("actionbar.nothingToUndo");
            _redo.SetEnabled(idle && ready && journal.NextRedo != null);
            _redo.Tooltip = journal.NextRedo != null ? GitText.Format("actionbar.redoTooltip", journal.NextRedo.Title) : GitText.Get("actionbar.nothingToRedo");

            _fetch.SetEnabled(idle && ready);
            // 자동 fetch 가 계정 문제로 실패했으면 경고(!)를 단다. 눌러서 수동 Fetch 하면 계정 고르기 팝업이 뜬다.
            _fetch.SetWarning(GitAutoFetch.LastFailureWasAuth);
            _fetch.Tooltip = GitAutoFetch.LastFailureWasAuth ? GitText.Get("actionbar.fetchAuthFailed") : GitText.Get("actionbar.fetchTooltip");
            _pull.SetEnabled(idle && ready && hasUpstream);
            _pull.SetBadge(behind);
            _pull.Tooltip = GitText.Format("actionbar.pullTooltip", behind);
            _push.SetEnabled(idle && ready && branch != null && branch.Detached == false && branch.HasCommits);
            _push.SetBadge(ahead);
            _push.Tooltip = GitText.Format("actionbar.pushTooltip", ahead);
            _newBranch.SetEnabled(idle && ready && branch != null && branch.HasCommits);
            _stash.SetEnabled(idle && ready && repository.Entries.Count > 0);
            _pop.SetEnabled(idle && ready && _stashCount > 0);
            _pop.SetBadge(_stashCount);
            _pop.Tooltip = GitText.Format("actionbar.popTooltip", _stashCount);
            _terminal.SetEnabled(ready);
            _refresh.SetEnabled(repository.IsRefreshing == false);
        }

        /// <summary>
        /// 저장소가 바뀌었을 때(Stash·Pop·터미널에서의 작업 뒤 등) stash 수를 다시 센다.
        /// </summary>
        public async void RefreshStashCount()
        {
            if (_isCountingStash || _services.Repository.IsRepository == false)
            {
                return;
            }
            _isCountingStash = true;
            _stashCount = await _services.Commands.StashCountAsync();
            _isCountingStash = false;
            Render();
        }

        private void Stash()
        {
            // 열린 씬·프리팹의 저장 안 된 수정은 stash 에 들어가지 않는다. 저장을 거부하면 멈춘다.
            if (_services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }
            _ = _services.Runner.RunAsync(GitText.Get("op.stash"), _services.Commands.StashAsync, refreshAssets: true);
        }

        /// <summary>
        /// 지금 HEAD 에서 새 브랜치를 만든다. GitKraken 레이아웃은 Graph 탭의 HEAD 줄에서 이름을 받아 바로 만들고 전환한다.
        /// 그 밖의 레이아웃(Unity VCS·Fork)은 이름을 묻고, 만든 뒤 바로 전환할지 한 번 더 묻는다(창을 닫으면 만들기만).
        /// </summary>
        private async System.Threading.Tasks.Task CreateBranchAsync(BranchActions branchActions)
        {
            string head = _services.Repository.HeadCommit;
            if (head != null && _services.InlineBranches?.TryBeginCreateBranch(head, revealGraph: true) == true)
            {
                return;
            }
            GitBranchInfo branch = _services.Repository.Branch;
            string from = branch == null || branch.Detached ? "HEAD" : GitDisplay.Nfc(branch.Name);
            // Fork 처럼 이름·"만든 뒤 체크아웃" 을 한 창에서 받는다(따로 전환을 묻지 않는다).
            GitBranchDialogResult result = await GitBranchDialog.ShowAsync(_services, from);
            if (result == null)
            {
                return;
            }
            await branchActions.CreateAsync(result.Name, result.Checkout);
        }

        private void OpenTerminal()
        {
            string error = GitTerminal.Open(GitRepository.RepoRoot ?? GitRepository.ProjectRoot);
            if (error != null)
            {
                _services.Dialogs.Alert(GitText.Get("actionbar.terminalFailed"), error);
            }
        }

        /// <summary>
        /// UXML 의 버튼 하나를 아이콘(위)·글자(아래)·숫자 배지로 꾸민다. 글자는 UXML 의 text 를 옮겨 쓴다.
        /// </summary>
        private sealed class ActionButton
        {
            private const string BadgeHiddenModifier = "git-action__badge--hidden";
            private const string BadgeWarningModifier = "git-action__badge--warning";

            private readonly Button _button;
            private readonly Label _badge;

            public event System.Action Clicked
            {
                add => _button.clicked += value;
                remove => _button.clicked -= value;
            }

            public string Tooltip
            {
                set => _button.tooltip = value;
            }

            public ActionButton(Button button, ActionGlyphKind kind) : this(button, new ActionGlyph(kind))
            {
            }

            /// <param name="imageModifier">그림 아이콘의 USS 클래스(background-image 를 정한다).</param>
            public ActionButton(Button button, string imageModifier) : this(button, ImageIcon(imageModifier))
            {
            }

            private static VisualElement ImageIcon(string imageModifier)
            {
                VisualElement icon = new() { pickingMode = PickingMode.Ignore };
                icon.AddToClassList(imageModifier);
                return icon;
            }

            private ActionButton(Button button, VisualElement icon)
            {
                _button = button;
                string caption = button.text;
                button.text = string.Empty;

                icon.AddToClassList("git-action__icon");
                button.Add(icon);

                Label label = new(caption);
                label.AddToClassList("git-action__caption");
                button.Add(label);

                _badge = new Label();
                _badge.AddToClassList("git-action__badge");
                _badge.AddToClassList(BadgeHiddenModifier);
                button.Add(_badge);
            }

            public void SetEnabled(bool enabled)
            {
                _button.SetEnabled(enabled);
            }

            /// <summary>
            /// 0 이면 숨긴다. 세 자리 넘는 수는 "99+" 로 줄인다(배지 폭을 넘지 않게).
            /// </summary>
            public void SetBadge(int count)
            {
                _badge.RemoveFromClassList(BadgeWarningModifier);
                _badge.EnableInClassList(BadgeHiddenModifier, count <= 0);
                _badge.text = count > 99 ? "99+" : count.ToString();
            }

            /// <summary>
            /// 숫자 대신 빨간 "!" 배지. 끄면 배지를 숨긴다(숫자 배지를 쓰는 버튼에는 쓰지 않는다).
            /// </summary>
            public void SetWarning(bool shown)
            {
                _badge.EnableInClassList(BadgeWarningModifier, shown);
                _badge.EnableInClassList(BadgeHiddenModifier, shown == false);
                _badge.text = "!";
            }
        }
    }
}
