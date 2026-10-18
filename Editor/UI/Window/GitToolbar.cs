using System.IO;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 창 위쪽 작업 막대(<c>.git-actionbar</c>). GitKraken·Fork 처럼 아이콘+글자 버튼을 묶어 둔다:
    /// 저장소·브랜치(누르면 브랜치 탭) | 가운데: 실행 취소·다시 실행 | Fetch·Pull·Push | 새 브랜치 | Stash·Pop | 오른쪽: 진행 상태 | 터미널·새로고침.
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
        private readonly Label _repoName;
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
            _repoName = root.Require<Label>("repo-name");
            _branchButton = root.Require<Button>("branch-button");
            _status = root.Require<Label>("status-label");
            _undo = new ActionButton(root.Require<Button>("undo-button"), ActionGlyphKind.Undo);
            _redo = new ActionButton(root.Require<Button>("redo-button"), ActionGlyphKind.Redo);
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
            _branchButton.clicked += () => navigator.SelectTab<BranchesView>();
            _undo.Clicked += () => _ = runner.RunAsync(GitText.Get("op.undo"), commands.UndoAsync, refreshAssets: true);
            _redo.Clicked += () => _ = runner.RunAsync(GitText.Get("op.redo"), commands.RedoAsync, refreshAssets: true);
            _fetch.Clicked += () => _ = runner.RunAsync(GitText.Get("op.fetch"), commands.FetchAsync, refreshAssets: false);
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
            _bar.RegisterCallback<GeometryChangedEvent>(evt => _bar.EnableInClassList(CompactModifier, evt.newRect.width < CompactWidth));
            _bar.RegisterCallback<DetachFromPanelEvent>(_ => commands.UndoJournal.Changed -= Render);
        }

        public void Render()
        {
            IGitRepositoryState repository = _services.Repository;
            GitBranchInfo branch = repository.Branch;
            _repoName.text = string.IsNullOrEmpty(GitRepository.RepoRoot) ? "-" : Path.GetFileName(GitRepository.RepoRoot.TrimEnd('/'));
            _repoName.tooltip = GitRepository.RepoRoot;
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
        /// 지금 HEAD 에서 새 브랜치를 만든다. 이름을 묻고, 만든 뒤 바로 전환할지 한 번 더 묻는다(창을 닫으면 만들기만).
        /// </summary>
        private async System.Threading.Tasks.Task CreateBranchAsync(BranchActions branchActions)
        {
            GitBranchInfo branch = _services.Repository.Branch;
            string from = branch == null || branch.Detached ? "HEAD" : GitDisplay.Nfc(branch.Name);
            string title = GitText.Get("branches.createTitle");
            string name = GitTextPromptWindow.Ask(title, GitText.Format("actionbar.newBranchPrompt", from), string.Empty)?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            bool switchTo = _services.Dialogs.Confirm(title, GitText.Format("commit.createBranchSwitch", name), GitText.Get("commit.createAndSwitch"), GitText.Get("commit.createOnly"));
            await branchActions.CreateAsync(name, switchTo);
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

            public ActionButton(Button button, ActionGlyphKind kind)
            {
                _button = button;
                string caption = button.text;
                button.text = string.Empty;

                ActionGlyph icon = new(kind);
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
                _badge.EnableInClassList(BadgeHiddenModifier, count <= 0);
                _badge.text = count > 99 ? "99+" : count.ToString();
            }
        }
    }
}
