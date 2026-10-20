using System.Collections.Generic;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// 브랜치 전환·생성·삭제의 "묻고 → 저장하고 → 실행하고 → 실패를 설명하는" 흐름. 뷰는 버튼을 이 클래스에 연결만 한다.
    ///
    /// 전환은 작업 트리 파일을 바꾸므로 먼저 씬·에셋을 저장하고, 끝나면 AssetDatabase 를 새로고침한다.
    /// 커밋하지 않은 변경은 git 이 충돌하지 않는 것만 들고 가고, 충돌하면 전환 자체를 거부한다(데이터 손실 없음).
    /// </summary>
    public sealed class BranchActions
    {
        private readonly GitServices _services;

        public BranchActions(GitServices services)
        {
            _services = services;
        }

        /// <summary>origin/feature/x → feature/x</summary>
        public static string LocalNameOf(GitBranchRef remote)
        {
            int slash = remote.Name.IndexOf('/');
            return slash < 0 ? remote.Name : remote.Name.Substring(slash + 1);
        }

        /// <summary>원격 브랜치를 골랐는데 같은 이름의 로컬 브랜치가 이미 있으면, 새로 만들지 않고 그 로컬 브랜치로 간다.</summary>
        public static GitBranchRef ResolveSwitchTarget(GitBranchRef branch, ICollection<string> localNames)
        {
            if (branch.IsRemote && localNames.Contains(LocalNameOf(branch)))
            {
                return new GitBranchRef { Name = LocalNameOf(branch), IsRemote = false };
            }
            return branch;
        }

        public async Task SwitchAsync(GitBranchRef branch, ICollection<string> localNames)
        {
            GitBranchRef target = ResolveSwitchTarget(branch, localNames);
            if (await PrepareWorkingTreeAsync(_services, GitText.Format("branches.actionSwitch", target.Name)) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.switch", target.Name), () => _services.Commands.SwitchAsync(target), refreshAssets: true);
        }

        /// <returns>만들었으면 true.</returns>
        public async Task<bool> CreateAsync(string name, bool switchTo)
        {
            GitResult valid = await _services.Commands.ValidateBranchNameAsync(name);
            if (valid.Success == false)
            {
                _services.Dialogs.Alert(GitText.Get("branches.createTitle"), GitText.Format("branches.invalidName", name));
                return false;
            }
            if (switchTo && await PrepareWorkingTreeAsync(_services, GitText.Format("branches.actionCreateSwitch", name)) == false)
            {
                return false;
            }
            GitResult result = await _services.Runner.RunAsync(GitText.Format("op.createBranch", name), () => _services.Commands.CreateBranchAsync(name, switchTo), refreshAssets: false);
            return result.Success;
        }

        public async Task DeleteAsync(GitBranchRef branch)
        {
            if (_services.Dialogs.Confirm(GitText.Get("branches.deleteTitle"), GitText.Format("branches.deleteConfirm", branch.Name), GitText.Get("common.delete")) == false)
            {
                return;
            }

            GitResult result = await _services.Runner.RunAsync(GitText.Format("op.deleteBranch", branch.Name), () => _services.Commands.DeleteBranchAsync(branch.Name, force: false),
                refreshAssets: false, showErrorDialog: false);
            if (result.Success)
            {
                return;
            }
            // -d 는 다른 브랜치에 병합되지 않은 커밋이 있으면 거부한다. 그 커밋을 버려도 되는지 한 번 더 묻는다.
            if (result.Message.Contains("not fully merged"))
            {
                if (_services.Dialogs.Confirm(GitText.Get("branches.deleteTitle"), GitText.Format("branches.notMerged", branch.Name), GitText.Get("branches.forceDelete")))
                {
                    await _services.Runner.RunAsync(GitText.Format("op.forceDeleteBranch", branch.Name), () => _services.Commands.DeleteBranchAsync(branch.Name, force: true), refreshAssets: false);
                }
                return;
            }
            _services.Dialogs.Alert(GitText.Get("branches.deleteFailed"), result.Message);
        }

        public async Task RenameAsync(GitBranchRef branch)
        {
            string title = GitText.Get("branch.renameTitle");
            string name = GitTextPromptWindow.Ask(title, GitText.Format("branch.renamePrompt", branch.Name), branch.Name)?.Trim();
            if (string.IsNullOrEmpty(name) || name == branch.Name)
            {
                return;
            }
            GitResult valid = await _services.Commands.ValidateBranchNameAsync(name);
            if (valid.Success == false)
            {
                _services.Dialogs.Alert(title, GitText.Format("branches.invalidName", name));
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.renameBranch", branch.Name, name), () => _services.Commands.RenameBranchAsync(branch.Name, name), refreshAssets: false);
        }

        /// <summary>
        /// 로컬 브랜치를 origin 에 올린다. upstream 이 없으면 같은 이름으로 올리며 추적을 건다.
        /// </summary>
        public async Task PushAsync(GitBranchRef branch)
        {
            bool setUpstream = string.IsNullOrEmpty(branch.Upstream) && string.IsNullOrEmpty(await _services.Commands.UpstreamOfAsync(branch.Name));
            if (_services.Dialogs.Confirm(GitText.Get("branch.pushTitle"), GitText.Format(setUpstream ? "branch.pushNewConfirm" : "branch.pushConfirm", branch.Name), GitText.Get("op.push")) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.pushBranch", branch.Name), () => _services.Commands.PushBranchAsync("origin", branch.Name, setUpstream), refreshAssets: false);
        }

        /// <summary>
        /// 원격 저장소에서 브랜치를 지운다(<c>git push origin --delete</c>). 다른 사람에게도 바로 보이는 일이라 한 번 더 묻는다.
        /// </summary>
        public async Task DeleteRemoteAsync(GitBranchRef remoteBranch)
        {
            (string remote, string name) = GitCommitCommands.SplitRemoteBranch(remoteBranch.Name);
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            if (_services.Dialogs.Confirm(GitText.Get("branch.deleteRemoteTitle"), GitText.Format("branch.deleteRemoteConfirm", name, remote), GitText.Get("common.delete")) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.deleteRemoteBranch", remoteBranch.Name), () => _services.Commands.DeleteRemoteBranchAsync(remote, name), refreshAssets: false);
        }

        /// <summary>
        /// 현재 브랜치에 원격의 새 커밋을 받는다(fast-forward 만). 위쪽 작업 막대의 Pull 과 같다.
        /// </summary>
        public async Task PullAsync()
        {
            if (_services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Get("op.pull"), _services.Commands.PullAsync, refreshAssets: true);
        }

        /// <summary>
        /// 체크아웃하지 않은 로컬 브랜치를 추적하는 원격 브랜치까지 앞으로 감는다. 작업 트리는 그대로다.
        /// </summary>
        public async Task FastForwardAsync(GitBranchRef branch)
        {
            string upstream = string.IsNullOrEmpty(branch.Upstream) ? await _services.Commands.UpstreamOfAsync(branch.Name) : branch.Upstream;
            if (string.IsNullOrEmpty(upstream))
            {
                _services.Dialogs.Alert(GitText.Get("fork.upstreamTitle"), GitText.Format("fork.noUpstream", branch.Name));
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.fastForward", branch.Name), () => _services.Commands.FastForwardBranchAsync(branch.Name, upstream), refreshAssets: false);
        }

        public async Task SetUpstreamAsync(GitBranchRef branch)
        {
            string current = string.IsNullOrEmpty(branch.Upstream) ? await _services.Commands.UpstreamOfAsync(branch.Name) : branch.Upstream;
            string title = GitText.Get("fork.upstreamTitle");
            string upstream = GitTextPromptWindow.Ask(title, GitText.Format("fork.upstreamPrompt", branch.Name), current ?? "origin/" + branch.Name)?.Trim();
            if (string.IsNullOrEmpty(upstream) || upstream == current)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.setUpstream", branch.Name, upstream), () => _services.Commands.SetUpstreamAsync(branch.Name, upstream), refreshAssets: false);
        }

        /// <summary>
        /// 작업 트리가 바뀌기 전에 열린 씬·에셋을 저장하고, 커밋하지 않은 변경이 있으면 Git 창 위 팝업(<see cref="GitConfirmDialog"/>)으로 알린다. 취소하면 false.
        /// GitKraken 배치는 GitKraken 처럼 묻지 않고 바로 진행한다 — 충돌하지 않는 변경은 따라가고, 충돌하면 git 이 전환을 거부해 아무것도 바뀌지 않는다.
        /// </summary>
        public static async Task<bool> PrepareWorkingTreeAsync(GitServices services, string action)
        {
            if (services.Dialogs.SaveOpenWork() == false)
            {
                return false;
            }
            int pending = services.Repository.Entries.Count;
            if (pending == 0 || GitLayout.Style == GitLayoutStyle.GitKraken)
            {
                return true;
            }
            return await GitConfirmDialog.ShowAsync(services, ActionGlyphKind.Branch, GitText.Get("branches.switchTitle"),
                GitText.Format("branches.pendingWarning", pending, action), GitText.Get("common.continue"));
        }
    }
}
