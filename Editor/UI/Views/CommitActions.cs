using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// Branch Explorer 커밋 메뉴의 흐름: 묻고(이름·확인) → 씬을 저장하고 → 실행한다. 인자는 <see cref="GitCommitCommands"/> 가 만든다.
    ///
    /// Unity Version Control 의 체인지 세트 메뉴에서 git 에 맞는 것만 옮겼다:
    /// 비교, 브랜치 만들기, 레이블(=태그), 워크스페이스 전환(=커밋으로 전환), 병합·체리픽, 고급 병합(no-ff·squash·subtractive=revert),
    /// 부모로 이동, 고급 옵션(=reset). "다음으로 병합"(작업 공간 없이 병합)·코드 검토·리포지토리 탐색은 git 에 해당 개념이 없어 뺐다.
    /// 병합·체리픽·revert 가 충돌로 멈추면 git 이 실패를 돌려주고 충돌 상태가 남는다 — 오류 창의 git 메시지가 해결 방법을 알려 준다.
    /// </summary>
    public sealed class CommitActions
    {
        private readonly GitServices _services;

        public CommitActions(GitServices services)
        {
            _services = services;
        }

        /// <summary>
        /// "abc1234 '메시지 첫 줄'". 확인 창·진행 표시에 쓴다.
        /// </summary>
        public static string Describe(GitCommitInfo commit)
        {
            return $"{commit.ShortHash} '{GitDisplay.FirstLine(commit.Subject)}'";
        }

        public void CompareWithParent(GitCommitInfo commit)
        {
            GitCompareWindow.OpenCommit(commit.Hash);
        }

        public async Task CompareWithRevisionAsync(GitCommitInfo commit)
        {
            string revision = GitTextPromptWindow.Ask(GitText.Get("commit.compareTitle"), GitText.Format("commit.compareWithPrompt", Describe(commit)), "HEAD");
            if (string.IsNullOrWhiteSpace(revision))
            {
                return;
            }
            string other = await _services.Commands.ResolveCommitAsync(revision.Trim());
            if (other == null)
            {
                _services.Dialogs.Alert(GitText.Get("commit.compareTitle"), GitText.Format("commit.unknownRevision", revision.Trim()));
                return;
            }
            GitCompareWindow.OpenRange(other, commit.Hash);
        }

        public async Task CreateBranchAsync(GitCommitInfo commit)
        {
            string title = GitText.Get("commit.createBranchTitle");
            string name = GitTextPromptWindow.Ask(title, GitText.Format("commit.createBranchPrompt", Describe(commit)), string.Empty)?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            GitResult valid = await _services.Commands.ValidateBranchNameAsync(name);
            if (valid.Success == false)
            {
                _services.Dialogs.Alert(title, GitText.Format("branches.invalidName", name));
                return;
            }
            // 대화상자를 닫아도 "만들기만" 이 되도록 전환을 확인 쪽에 둔다.
            bool switchTo = _services.Dialogs.Confirm(title, GitText.Format("commit.createBranchSwitch", name), GitText.Get("commit.createAndSwitch"), GitText.Get("commit.createOnly"));
            if (switchTo && BranchActions.PrepareWorkingTree(_services, GitText.Format("branches.actionCreateSwitch", name)) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.createBranch", name), () => _services.Commands.CreateBranchAtAsync(name, commit.Hash, switchTo), refreshAssets: switchTo);
        }

        public async Task CreateTagAsync(GitCommitInfo commit)
        {
            string title = GitText.Get("commit.tagTitle");
            string name = GitTextPromptWindow.Ask(title, GitText.Format("commit.tagPrompt", Describe(commit)), string.Empty)?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            GitResult valid = await _services.Commands.ValidateTagNameAsync(name);
            if (valid.Success == false)
            {
                _services.Dialogs.Alert(title, GitText.Format("commit.invalidTag", name));
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.tag", name), () => _services.Commands.CreateTagAsync(name, commit.Hash), refreshAssets: false);
        }

        public async Task SwitchToAsync(GitCommitInfo commit)
        {
            string title = GitText.Get("commit.switchTitle");
            if (_services.Dialogs.Confirm(title, GitText.Format("commit.switchConfirm", Describe(commit)), GitText.Get("commit.switchOk")) == false)
            {
                return;
            }
            if (BranchActions.PrepareWorkingTree(_services, GitText.Format("commit.actionSwitch", commit.ShortHash)) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.switch", commit.ShortHash), () => _services.Commands.SwitchDetachedAsync(commit.Hash), refreshAssets: true);
        }

        public Task MergeAsync(GitCommitInfo commit, GitMergeMode mode)
        {
            string key = mode switch
            {
                GitMergeMode.NoFastForward => "commit.mergeNoFfConfirm",
                GitMergeMode.Squash => "commit.squashConfirm",
                _ => "commit.mergeConfirm",
            };
            return MoveHeadAsync(GitText.Get("commit.mergeTitle"), GitText.Format(key, Describe(commit), CurrentBranchName()),
                GitText.Format("op.merge", commit.ShortHash), GitCommitCommands.Merge(commit.Hash, mode), null);
        }

        public Task CherryPickAsync(GitCommitInfo commit)
        {
            return MoveHeadAsync(GitText.Get("commit.cherryPickTitle"), GitText.Format("commit.cherryPickConfirm", Describe(commit), CurrentBranchName()),
                GitText.Format("op.cherryPick", commit.ShortHash), GitCommitCommands.CherryPick(commit.Hash, commit.IsMerge), null);
        }

        public Task RevertAsync(GitCommitInfo commit)
        {
            return MoveHeadAsync(GitText.Get("commit.revertTitle"), GitText.Format("commit.revertConfirm", Describe(commit), CurrentBranchName()),
                GitText.Format("op.revert", commit.ShortHash), GitCommitCommands.Revert(commit.Hash, commit.IsMerge), null);
        }

        public Task ResetAsync(GitCommitInfo commit, GitResetMode mode)
        {
            string key = mode switch
            {
                GitResetMode.Soft => "commit.resetSoftConfirm",
                GitResetMode.Mixed => "commit.resetMixedConfirm",
                _ => "commit.resetHardConfirm",
            };
            return MoveHeadAsync(GitText.Get("commit.resetTitle"), GitText.Format(key, CurrentBranchName(), Describe(commit)),
                GitText.Format("op.reset", commit.ShortHash, mode.ToString().ToLowerInvariant()), GitCommitCommands.Reset(commit.Hash, mode),
                GitCommitCommands.UndoResetMode(mode));
        }

        private async Task MoveHeadAsync(string title, string confirm, string operation, string[] args, string undoResetMode)
        {
            if (_services.Dialogs.Confirm(title, confirm, GitText.Get("common.continue")) == false)
            {
                return;
            }
            // 전환과 달리 "변경을 들고 간다" 는 경고는 맞지 않는다: 겹치는 로컬 수정이 있으면 git 이 시작 전에 거부하고,
            // hard reset 의 손실은 확인 문구가 이미 알린다. 그래서 열린 씬·에셋 저장만 한다.
            if (_services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }
            await _services.Runner.RunAsync(operation, () => _services.Commands.MoveHeadAsync(operation, args, undoResetMode), refreshAssets: true);
        }

        private string CurrentBranchName()
        {
            GitBranchInfo branch = _services.Repository.Branch;
            return branch == null || branch.Detached ? "HEAD" : branch.Name;
        }
    }
}
