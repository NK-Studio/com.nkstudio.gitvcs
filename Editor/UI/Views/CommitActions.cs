using System.Collections.Generic;
using System.Linq;
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

        public Task CompareWithRevisionAsync(GitCommitInfo commit)
        {
            return CompareWithRevisionAsync(commit.Hash, Describe(commit), "HEAD");
        }

        /// <summary>
        /// 다른 리비전을 물어 <paramref name="target"/> 과 비교한다(물은 쪽 ↔ 대상).
        /// </summary>
        /// <param name="target">커밋 해시 또는 브랜치 이름.</param>
        /// <param name="description">물을 때 보일 대상 설명.</param>
        public async Task CompareWithRevisionAsync(string target, string description, string defaultRevision)
        {
            string title = GitText.Get("commit.compareTitle");
            string other = null;
            // 모르는 리비전이면 입력 팝업으로 돌아가 쓴 글을 고치게 한다.
            for (string revision = defaultRevision; ;)
            {
                revision = (await _services.Dialogs.PromptAsync(title, GitText.Format("commit.compareWithPrompt", description), revision, icon: ActionGlyphKind.Search))?.Trim();
                if (string.IsNullOrEmpty(revision))
                {
                    return;
                }
                other = await _services.Commands.ResolveCommitAsync(revision);
                if (other != null)
                {
                    break;
                }
                await _services.Dialogs.AlertAsync(title, GitText.Format("commit.unknownRevision", revision), ActionGlyphKind.Search);
            }
            GitCompareWindow.OpenRange(other, target);
        }

        public Task CreateBranchAsync(GitCommitInfo commit)
        {
            return CreateBranchFromAsync(commit.Hash, Describe(commit));
        }

        /// <summary>
        /// GitKraken 레이아웃이면 그래프 줄에서 이름을 받고(<see cref="IInlineBranchCreator"/>), 아니면 대화상자로 묻는다.
        /// </summary>
        /// <param name="startPoint">새 브랜치가 가리킬 커밋 해시.</param>
        /// <param name="revealGraph">Graph 탭을 보고 있지 않아도 열어서 그래프에서 받는다(사이드바의 "HEAD 에서 새 브랜치").</param>
        public Task CreateBranchFromAsync(string startPoint, string description, bool revealGraph = false)
        {
            if (_services.InlineBranches?.TryBeginCreateBranch(startPoint, revealGraph) == true)
            {
                return Task.CompletedTask;
            }
            return CreateBranchWithDialogAsync(startPoint, description);
        }

        /// <summary>
        /// GitKraken 의 그래프 안 입력: 이름을 받은 뒤 더 묻지 않고 만들고 바로 전환한다.
        /// HEAD 에서 만들면 작업 트리가 그대로라(커밋하지 않은 변경을 그대로 들고 간다) 저장·확인 없이 실행한다.
        /// </summary>
        public async Task CreateAndSwitchAsync(string name, string startPoint)
        {
            string title = GitText.Get("commit.createBranchTitle");
            GitResult valid = await _services.Commands.ValidateBranchNameAsync(name);
            if (valid.Success == false)
            {
                await _services.Dialogs.AlertAsync(title, GitText.Format("branches.invalidName", name), ActionGlyphKind.Branch);
                return;
            }
            bool atHead = startPoint == _services.Repository.HeadCommit;
            if (atHead == false && await BranchActions.PrepareWorkingTreeAsync(_services, GitText.Format("branches.actionCreateSwitch", name)) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.createBranch", name), () => _services.Commands.CreateBranchAtAsync(name, startPoint, true), refreshAssets: atHead == false);
        }

        /// <param name="startPoint">새 브랜치가 가리킬 커밋 해시.</param>
        public async Task CreateBranchWithDialogAsync(string startPoint, string description)
        {
            string title = GitText.Get("commit.createBranchTitle");
            // Fork 처럼 이름·"만든 뒤 체크아웃" 을 한 창에서 받는다. 창이 쓰는 동안 이름을 검사하고, 여기서 git 으로 한 번 더 확인한다.
            GitBranchDialogResult picked = await GitBranchDialog.ShowAsync(_services, description);
            if (picked == null)
            {
                return;
            }
            string name = picked.Name;
            bool switchTo = picked.Checkout;
            GitResult valid = await _services.Commands.ValidateBranchNameAsync(name);
            if (valid.Success == false)
            {
                await _services.Dialogs.AlertAsync(title, GitText.Format("branches.invalidName", name), ActionGlyphKind.Branch);
                return;
            }
            if (switchTo && await BranchActions.PrepareWorkingTreeAsync(_services, GitText.Format("branches.actionCreateSwitch", name)) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.createBranch", name), () => _services.Commands.CreateBranchAtAsync(name, startPoint, switchTo), refreshAssets: switchTo);
        }

        public Task CreateTagAsync(GitCommitInfo commit)
        {
            return CreateTagAtAsync(commit.Hash, Describe(commit));
        }

        /// <param name="commit">태그가 가리킬 커밋 해시.</param>
        public async Task CreateTagAtAsync(string commit, string description)
        {
            string title = GitText.Get("commit.tagTitle");
            // 이미 있는 태그 이름은 쓰는 동안 막는다(git 오류로 알게 되지 않게).
            HashSet<string> existing = new(await _services.Commands.TagNamesAsync());
            string name = (await _services.Dialogs.PromptAsync(title, GitText.Format("commit.tagPrompt", description), string.Empty,
                validate: value =>
                {
                    string trimmed = value.Trim();
                    if (existing.Contains(trimmed))
                    {
                        return GitText.Format("commit.tagExists", trimmed);
                    }
                    BranchNameProblem problem = GitBranchNames.Check(trimmed, null);
                    return problem is BranchNameProblem.None or BranchNameProblem.Empty ? null : GitText.Get("branchDialog.problem" + problem);
                }, icon: ActionGlyphKind.Tag))?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            GitResult valid = await _services.Commands.ValidateTagNameAsync(name);
            if (valid.Success == false)
            {
                await _services.Dialogs.AlertAsync(title, GitText.Format("commit.invalidTag", name), ActionGlyphKind.Tag);
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.tag", name), () => _services.Commands.CreateTagAsync(name, commit), refreshAssets: false);
        }

        public async Task SwitchToAsync(GitCommitInfo commit)
        {
            string title = GitText.Get("commit.switchTitle");
            if (await GitConfirmDialog.ShowAsync(_services, ActionGlyphKind.Branch, title, GitText.Format("commit.switchConfirm", Describe(commit)), GitText.Get("commit.switchOk")) == false)
            {
                return;
            }
            if (await BranchActions.PrepareWorkingTreeAsync(_services, GitText.Format("commit.actionSwitch", commit.ShortHash)) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.switch", commit.ShortHash), () => _services.Commands.SwitchDetachedAsync(commit.Hash), refreshAssets: true);
        }

        public Task MergeAsync(GitCommitInfo commit, GitMergeMode mode)
        {
            return MergeAsync(commit.Hash, commit.ShortHash, Describe(commit), mode);
        }

        /// <param name="revision">병합할 커밋 해시 또는 브랜치 이름. 브랜치 이름이면 병합 메시지에 그 이름이 남는다.</param>
        /// <param name="shortName">진행 표시·실행 취소 목록에 보일 짧은 이름.</param>
        public Task MergeAsync(string revision, string shortName, string description, GitMergeMode mode)
        {
            string key = mode switch
            {
                GitMergeMode.NoFastForward => "commit.mergeNoFfConfirm",
                GitMergeMode.Squash => "commit.squashConfirm",
                _ => "commit.mergeConfirm",
            };
            return MoveHeadAsync(GitText.Get("commit.mergeTitle"), GitText.Format(key, description, CurrentBranchName()),
                GitText.Format("op.merge", shortName), GitCommitCommands.Merge(revision, mode), null);
        }

        /// <summary>
        /// 현재 브랜치의 커밋을 <paramref name="revision"/> 위로 다시 쌓는다.
        /// </summary>
        public async Task RebaseOntoAsync(string revision, string shortName, string description)
        {
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("branch.rebaseTitle"), GitText.Format("branch.rebaseConfirm", CurrentBranchName(), description), GitText.Get("common.continue"),
                    icon: ActionGlyphKind.Branch) == false)
            {
                return;
            }
            if (_services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }
            string operation = GitText.Format("op.rebase", CurrentBranchName(), shortName);
            GitResult result = await _services.Runner.RunAsync(operation, () => _services.Commands.RebaseAsync(operation, revision), refreshAssets: true);
            await AlertNoticeAsync(GitText.Get("branch.rebaseTitle"), result);
        }

        /// <summary>성공했지만 알려야 할 일이 있을 때(rebase 의 autostash 충돌). 실패는 Runner 가 이미 알린다.</summary>
        private async Task AlertNoticeAsync(string title, GitResult result)
        {
            if (result.Success && string.IsNullOrEmpty(result.Error) == false)
            {
                await _services.Dialogs.AlertAsync(title, result.Error);
            }
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

        /// <summary>
        /// 이 커밋 ↔ 지금 작업 트리(커밋하지 않은 변경 포함).
        /// </summary>
        public void CompareWithWorkingTree(GitCommitInfo commit)
        {
            GitCompareWindow.OpenWorkingTree(commit.Hash);
        }

        /// <summary>
        /// Fork 의 Interactive Rebase 빠른 작업. 메시지가 필요한 것(reword·squash)은 여러 줄 입력 창에 지금 메시지를 채워 묻는다.
        /// </summary>
        public async Task RewriteAsync(GitCommitInfo commit, GitRewriteKind kind)
        {
            string unsupported = GitRewrite.Unsupported(kind, commit);
            string title = GitText.Get(kind switch
            {
                GitRewriteKind.Reword => "rewrite.rewordTitle",
                GitRewriteKind.Squash => "rewrite.squashTitle",
                GitRewriteKind.Fixup => "rewrite.fixupTitle",
                _ => "rewrite.dropTitle",
            });
            if (unsupported != null)
            {
                await _services.Dialogs.AlertAsync(title, GitText.Get(unsupported), ActionGlyphKind.Pencil);
                return;
            }

            string message = null;
            switch (kind)
            {
                case GitRewriteKind.Reword:
                {
                    string current = await _services.History.CommitMessageAsync(commit.Hash) ?? commit.Subject;
                    message = await _services.Dialogs.PromptAsync(title, GitText.Format("rewrite.rewordPrompt", Describe(commit)), current.TrimEnd(), multiline: true);
                    if (message == null || message.Trim() == current.Trim())
                    {
                        return;
                    }
                    break;
                }
                case GitRewriteKind.Squash:
                {
                    string parentMessage = await _services.History.CommitMessageAsync(commit.Parents[0]) ?? string.Empty;
                    string ownMessage = await _services.History.CommitMessageAsync(commit.Hash) ?? commit.Subject;
                    string combined = GitRewrite.CombinedMessage(GitRewriteKind.Squash, parentMessage, ownMessage).TrimEnd();
                    message = await _services.Dialogs.PromptAsync(title, GitText.Format("rewrite.squashPrompt", Describe(commit)), combined, multiline: true);
                    if (message == null)
                    {
                        return;
                    }
                    break;
                }
                case GitRewriteKind.Fixup:
                    if (await _services.Dialogs.ConfirmAsync(title, GitText.Format("rewrite.fixupConfirm", Describe(commit)), GitText.Get("common.continue"), icon: ActionGlyphKind.Pencil) == false)
                    {
                        return;
                    }
                    break;
                case GitRewriteKind.Drop:
                    if (await _services.Dialogs.ConfirmAsync(title, GitText.Format("rewrite.dropConfirm", Describe(commit), CurrentBranchName()), GitText.Get("common.continue"),
                            danger: true, icon: ActionGlyphKind.Pencil) == false)
                    {
                        return;
                    }
                    // 빼기만 작업 트리 파일을 바꾼다.
                    if (_services.Dialogs.SaveOpenWork() == false)
                    {
                        return;
                    }
                    break;
            }
            if (message != null && string.IsNullOrWhiteSpace(message))
            {
                await _services.Dialogs.AlertAsync(title, GitText.Get("rewrite.emptyMessage"), ActionGlyphKind.Pencil);
                return;
            }

            string operation = GitText.Format(kind switch
            {
                GitRewriteKind.Reword => "op.reword",
                GitRewriteKind.Squash => "op.squash",
                GitRewriteKind.Fixup => "op.fixup",
                _ => "op.drop",
            }, commit.ShortHash);
            GitResult result = await _services.Runner.RunAsync(operation, () => _services.Commands.RewriteAsync(kind, commit.Hash, message, operation), refreshAssets: kind == GitRewriteKind.Drop);
            if (kind == GitRewriteKind.Drop)
            {
                await AlertNoticeAsync(title, result);
            }
        }

        /// <summary>
        /// <c>git format-patch</c> 결과를 고른 파일에 쓴다.
        /// </summary>
        public async Task SavePatchAsync(GitCommitInfo commit)
        {
            string slug = GitDisplay.PatchFileSlug(commit.Subject);
            string path = UnityEditor.EditorUtility.SaveFilePanel(GitText.Get("fork.patchTitle"), string.Empty, $"{commit.ShortHash}-{slug}.patch", "patch");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.savePatch", commit.ShortHash), () => _services.Commands.SavePatchAsync(commit.Hash, path), refreshAssets: false);
        }

        /// <summary>
        /// Fork 의 "Copy Commit Info": SHA·작성자·날짜와 메시지 전체.
        /// </summary>
        public async Task CopyInfoAsync(GitCommitInfo commit)
        {
            string message = await _services.History.CommitMessageAsync(commit.Hash) ?? commit.Subject;
            string date = commit.Date.ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture);
            _services.Assets.CopyToClipboard(GitText.Format("fork.copyInfoFormat", commit.Hash, commit.AuthorName, commit.AuthorEmail, date, message.TrimEnd()));
        }

        private async Task MoveHeadAsync(string title, string confirm, string operation, string[] args, string undoResetMode)
        {
            // hard reset 은 커밋하지 않은 변경을 지운다: 빨간 확인, 기본은 취소.
            bool danger = args.Contains("--hard");
            if (await _services.Dialogs.ConfirmAsync(title, confirm, GitText.Get("common.continue"), danger, icon: ActionGlyphKind.Branch) == false)
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

        public string CurrentBranchName()
        {
            GitBranchInfo branch = _services.Repository.Branch;
            return branch == null || branch.Detached ? "HEAD" : branch.Name;
        }
    }
}
