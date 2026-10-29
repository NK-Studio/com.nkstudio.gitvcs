using System.Collections.Generic;
using System.Linq;
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
                await _services.Dialogs.AlertAsync(GitText.Get("branches.createTitle"), GitText.Format("branches.invalidName", name), ActionGlyphKind.Branch);
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
            // 지운 뒤에는 추적하던 원격 브랜치를 알 수 없으니 먼저 읽어 둔다.
            string upstream = string.IsNullOrEmpty(branch.Upstream) ? await _services.Commands.UpstreamOfAsync(branch.Name) : branch.Upstream;
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("branches.deleteTitle"), GitText.Format("branches.deleteConfirm", branch.Name), GitText.Get("common.delete"),
                    danger: true, icon: ActionGlyphKind.Branch) == false)
            {
                return;
            }

            GitResult result = await _services.Runner.RunAsync(GitText.Format("op.deleteBranch", branch.Name), () => _services.Commands.DeleteBranchAsync(branch.Name, force: false),
                refreshAssets: false, showErrorDialog: false);
            if (result.Success)
            {
                await OfferDeleteUpstreamAsync(upstream);
                return;
            }
            // -d 는 다른 브랜치에 병합되지 않은 커밋이 있으면 거부한다. 그 커밋을 버려도 되는지 한 번 더 묻는다.
            if (result.Message.Contains("not fully merged"))
            {
                if (await _services.Dialogs.ConfirmAsync(GitText.Get("branches.deleteTitle"), GitText.Format("branches.notMerged", branch.Name), GitText.Get("branches.forceDelete"),
                        danger: true, icon: ActionGlyphKind.Branch))
                {
                    GitResult forced = await _services.Runner.RunAsync(GitText.Format("op.forceDeleteBranch", branch.Name), () => _services.Commands.DeleteBranchAsync(branch.Name, force: true), refreshAssets: false);
                    if (forced.Success)
                    {
                        await OfferDeleteUpstreamAsync(upstream);
                    }
                }
                return;
            }
            await GitErrorDialog.ShowAsync(_services, GitText.Get("branches.deleteFailed"), result.Message);
        }

        /// <summary>
        /// 로컬 브랜치를 지운 뒤, 추적하던 원격 브랜치도 지울지 묻는다(GitKraken 의 "원격도 삭제"). 원격 삭제는 다른 사람에게도 보이므로 따로 확인한다.
        /// </summary>
        private async Task OfferDeleteUpstreamAsync(string upstream)
        {
            if (string.IsNullOrEmpty(upstream))
            {
                return;
            }
            (List<GitBranchRef> branches, _) = await _services.History.BranchesAsync();
            if (branches == null || branches.Any(b => b.IsRemote && b.Name == upstream) == false)
            {
                return;
            }
            (string remote, string name) = GitCommitCommands.SplitRemoteBranch(upstream);
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("branch.deleteRemoteTitle"), GitText.Format("branches.deleteUpstreamToo", upstream), GitText.Get("common.delete"),
                    danger: true, cancel: GitText.Get("branches.keepRemote"), icon: ActionGlyphKind.Cloud) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.deleteRemoteBranch", upstream), () => _services.Commands.DeleteRemoteBranchAsync(remote, name), refreshAssets: false);
        }

        public async Task RenameAsync(GitBranchRef branch)
        {
            string title = GitText.Get("branch.renameTitle");
            (List<GitBranchRef> branches, _) = await _services.History.BranchesAsync();
            List<string> existing = branches?.Where(b => b.IsRemote == false && b.Name != branch.Name).Select(b => b.Name).ToList() ?? new List<string>();
            string name = (await _services.Dialogs.PromptAsync(title, GitText.Format("branch.renamePrompt", branch.Name), branch.Name,
                validate: value => BranchNameProblemText(value, existing), icon: ActionGlyphKind.Branch))?.Trim();
            if (string.IsNullOrEmpty(name) || name == branch.Name)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.renameBranch", branch.Name, name), () => _services.Commands.RenameBranchAsync(branch.Name, name), refreshAssets: false);
        }

        /// <summary>
        /// 로컬 브랜치를 원격에 올린다. upstream 이 있으면 그 원격, 없으면 고른 원격의 같은 이름으로 올리며 추적을 건다.
        /// </summary>
        public async Task PushAsync(GitBranchRef branch)
        {
            string title = GitText.Get("branch.pushTitle");
            string upstream = string.IsNullOrEmpty(branch.Upstream) ? await _services.Commands.UpstreamOfAsync(branch.Name) : branch.Upstream;
            bool setUpstream = string.IsNullOrEmpty(upstream);
            string remote = setUpstream ? await new SyncFlows(_services).ChooseRemoteAsync(title) : GitCommitCommands.SplitRemoteBranch(upstream).remote;
            if (string.IsNullOrEmpty(remote))
            {
                return;
            }
            if (await _services.Dialogs.ConfirmAsync(title, GitText.Format(setUpstream ? "branch.pushNewConfirm" : "branch.pushConfirm", branch.Name, remote), GitText.Get("op.push"),
                    icon: ActionGlyphKind.Push) == false)
            {
                return;
            }
            string opTitle = GitText.Format("op.pushBranch", branch.Name);
            GitResult result = await _services.Runner.RunAsync(opTitle, () => _services.Commands.PushBranchAsync(remote, branch.Name, setUpstream), refreshAssets: false,
                showErrorDialog: false);
            await new SyncFlows(_services).AfterPushAsync(opTitle, result, setUpstream ? branch.Name : null);
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
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("branch.deleteRemoteTitle"), GitText.Format("branch.deleteRemoteConfirm", name, remote), GitText.Get("common.delete"),
                    danger: true, icon: ActionGlyphKind.Cloud) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.deleteRemoteBranch", remoteBranch.Name), () => _services.Commands.DeleteRemoteBranchAsync(remote, name), refreshAssets: false);
        }

        /// <summary>
        /// 현재 브랜치에 원격의 새 커밋을 받는다. 위쪽 작업 막대의 Pull 과 같다(갈라졌으면 병합·리베이스를 묻는다).
        /// </summary>
        public Task PullAsync()
        {
            return new SyncFlows(_services).PullAsync();
        }

        /// <summary>
        /// 체크아웃하지 않은 로컬 브랜치를 추적하는 원격 브랜치까지 앞으로 감는다. 작업 트리는 그대로다.
        /// </summary>
        public async Task FastForwardAsync(GitBranchRef branch)
        {
            string upstream = string.IsNullOrEmpty(branch.Upstream) ? await _services.Commands.UpstreamOfAsync(branch.Name) : branch.Upstream;
            if (string.IsNullOrEmpty(upstream))
            {
                await _services.Dialogs.AlertAsync(GitText.Get("fork.upstreamTitle"), GitText.Format("fork.noUpstream", branch.Name), ActionGlyphKind.Cloud);
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.fastForward", branch.Name), () => _services.Commands.FastForwardBranchAsync(branch.Name, upstream), refreshAssets: false);
        }

        /// <summary>
        /// 추적할 원격 브랜치를 고른다. 원격에 있는 브랜치는 바로 추적하고, 원격에 아직 없는 이름이면 올리면서 추적을 건다(<c>push -u</c>).
        /// </summary>
        public async Task SetUpstreamAsync(GitBranchRef branch)
        {
            string current = string.IsNullOrEmpty(branch.Upstream) ? await _services.Commands.UpstreamOfAsync(branch.Name) : branch.Upstream;
            string title = GitText.Get("fork.upstreamTitle");
            List<string> remotes = await _services.Commands.RemotesAsync();
            if (remotes.Count == 0)
            {
                await new SyncFlows(_services).OfferAddRemoteAsync(title);
                return;
            }
            (List<GitBranchRef> branches, _) = await _services.History.BranchesAsync();
            HashSet<string> remoteBranches = new(branches?.Where(b => b.IsRemote).Select(b => b.Name) ?? Enumerable.Empty<string>());
            string suggestion = current ?? (remotes.Contains("origin") ? "origin" : remotes[0]) + "/" + branch.Name;
            string upstream = (await _services.Dialogs.PromptAsync(title, GitText.Format("fork.upstreamPrompt", branch.Name), suggestion,
                validate: value =>
                {
                    (string remote, string name) = GitCommitCommands.SplitRemoteBranch(value.Trim());
                    if (string.IsNullOrEmpty(remote) || string.IsNullOrEmpty(name) || remotes.Contains(remote) == false)
                    {
                        return GitText.Format("fork.upstreamUnknownRemote", string.Join(", ", remotes));
                    }
                    return null;
                }, icon: ActionGlyphKind.Cloud))?.Trim();
            if (string.IsNullOrEmpty(upstream) || upstream == current)
            {
                return;
            }
            if (remoteBranches.Contains(upstream))
            {
                await _services.Runner.RunAsync(GitText.Format("op.setUpstream", branch.Name, upstream), () => _services.Commands.SetUpstreamAsync(branch.Name, upstream), refreshAssets: false);
                return;
            }
            // 원격에 아직 없다: 그 이름으로 올리면서 추적을 건다.
            (string targetRemote, string targetName) = GitCommitCommands.SplitRemoteBranch(upstream);
            if (await _services.Dialogs.ConfirmAsync(title, GitText.Format("fork.upstreamPushConfirm", upstream), GitText.Get("op.push"), icon: ActionGlyphKind.Push) == false)
            {
                return;
            }
            string opTitle = GitText.Format("op.pushBranch", branch.Name);
            GitResult result = await _services.Runner.RunAsync(opTitle, () => _services.Commands.PushBranchToAsync(targetRemote, branch.Name, targetName), refreshAssets: false,
                showErrorDialog: false);
            await new SyncFlows(_services).AfterPushAsync(opTitle, result, null);
        }

        /// <summary>브랜치 이름의 문제를 입력 팝업에 보일 한 줄로. 문제가 없으면 null.</summary>
        public static string BranchNameProblemText(string value, IEnumerable<string> existing)
        {
            BranchNameProblem problem = GitBranchNames.Check((value ?? string.Empty).Trim(), existing);
            return problem is BranchNameProblem.None or BranchNameProblem.Empty ? null : GitText.Get("branchDialog.problem" + problem);
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
