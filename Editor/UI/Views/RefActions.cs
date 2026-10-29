using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// 사이드바 참조 트리의 태그·스태시·워크트리·서브모듈 작업. <see cref="BranchActions"/> 처럼 "묻고 → 저장하고 → 실행" 흐름만 맡고,
    /// git 인자는 <see cref="GitCommitCommands"/> 가 만든다. 브랜치 작업은 <see cref="BranchMenu"/> 를 그대로 쓴다.
    /// </summary>
    public sealed class RefActions
    {
        private readonly GitServices _services;
        private readonly CommitActions _commitActions;

        public RefActions(GitServices services)
        {
            _services = services;
            _commitActions = new CommitActions(services);
        }

        // ── 구역 머리 ──

        public Task NewBranchAtHeadAsync()
        {
            string head = _services.Repository.HeadCommit;
            return head == null ? Task.CompletedTask : _commitActions.CreateBranchFromAsync(head, "HEAD", revealGraph: true);
        }

        public Task NewTagAtHeadAsync()
        {
            string head = _services.Repository.HeadCommit;
            return head == null ? Task.CompletedTask : _commitActions.CreateTagAtAsync(head, "HEAD");
        }

        public Task FetchAsync()
        {
            return _services.Runner.RunAsync(GitText.Get("op.fetch"), _services.Commands.FetchAsync, refreshAssets: false);
        }

        public async Task StashNowAsync()
        {
            if (_services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Get("op.stash"), _services.Commands.StashAsync, refreshAssets: true);
        }

        // ── 태그 ──

        public Task CheckoutTagAsync(GitTagRef tag)
        {
            return _commitActions.SwitchToAsync(new GitCommitInfo
            {
                Hash = tag.Commit,
                ShortHash = GitDisplay.ShortRevision(tag.Commit),
                Parents = System.Array.Empty<string>(),
                Subject = tag.Name,
            });
        }

        public Task MergeTagAsync(GitTagRef tag)
        {
            return _commitActions.MergeAsync(tag.Name, tag.Name, $"'{tag.Name}'", GitMergeMode.Default);
        }

        public async Task PushTagAsync(GitTagRef tag, string remote)
        {
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("refs.tagPushTitle"), GitText.Format("refs.tagPushConfirm", tag.Name, remote), GitText.Get("op.push"),
                    icon: ActionGlyphKind.Tag) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.pushTag", tag.Name), () => _services.Commands.PushTagAsync(remote, tag.Name), refreshAssets: false);
        }

        public async Task DeleteTagAsync(GitTagRef tag)
        {
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("refs.tagDeleteTitle"), GitText.Format("refs.tagDeleteConfirm", tag.Name), GitText.Get("common.delete"),
                    danger: true, icon: ActionGlyphKind.Tag) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.deleteTag", tag.Name), () => _services.Commands.DeleteTagAsync(tag.Name), refreshAssets: false);
        }

        public async Task DeleteRemoteTagAsync(GitTagRef tag, string remote)
        {
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("refs.tagDeleteTitle"), GitText.Format("refs.tagDeleteRemoteConfirm", tag.Name, remote), GitText.Get("common.delete"),
                    danger: true, icon: ActionGlyphKind.Tag) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.deleteRemoteTag", tag.Name), () => _services.Commands.DeleteRemoteTagAsync(remote, tag.Name), refreshAssets: false);
        }

        // ── 원격 ──

        public Task FetchRemoteAsync(string remote)
        {
            return _services.Runner.RunAsync(GitText.Format("op.fetchRemote", remote), () => GitOperations.RunNetworkAsync(new[] { "fetch", "--prune", remote }), refreshAssets: false);
        }

        /// <summary>원격에서 지워진 브랜치의 로컬 추적 참조를 정리한다(<c>git remote prune</c>).</summary>
        public Task PruneRemoteAsync(string remote)
        {
            return _services.Runner.RunAsync(GitText.Format("op.pruneRemote", remote), () => GitOperations.RunNetworkAsync(new[] { "remote", "prune", remote }), refreshAssets: false);
        }

        public async Task RenameRemoteAsync(string remote, IEnumerable<string> existing)
        {
            HashSet<string> taken = new(existing);
            taken.Remove(remote);
            string name = (await _services.Dialogs.PromptAsync(GitText.Get("remote.rename"), GitText.Format("remote.renamePrompt", remote), remote,
                validate: value =>
                {
                    string trimmed = value.Trim();
                    if (taken.Contains(trimmed))
                    {
                        return GitText.Format("remote.nameTaken", trimmed);
                    }
                    BranchNameProblem problem = GitBranchNames.Check(trimmed, null);
                    return problem is BranchNameProblem.None or BranchNameProblem.Empty && trimmed.Contains("/") == false ? null : GitText.Get("remote.invalidName");
                }, icon: ActionGlyphKind.Cloud))?.Trim();
            if (string.IsNullOrEmpty(name) || name == remote)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.renameRemote", remote, name), () => GitRepository.RunAsync("remote", "rename", remote, name), refreshAssets: false);
        }

        public async Task EditRemoteUrlAsync(string remote, string url)
        {
            string next = (await _services.Dialogs.PromptAsync(GitText.Get("remote.editUrl"), GitText.Format("remote.editUrlPrompt", remote), url ?? string.Empty,
                icon: ActionGlyphKind.Cloud))?.Trim();
            if (string.IsNullOrEmpty(next) || next == url)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.editRemoteUrl", remote), () => GitRepository.RunAsync("remote", "set-url", remote, next), refreshAssets: false);
        }

        public async Task RemoveRemoteAsync(string remote)
        {
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("remote.removeTitle"), GitText.Format("remote.removeConfirm", remote), GitText.Get("common.delete"),
                    danger: true, icon: ActionGlyphKind.Cloud) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.removeRemote", remote), () => GitRepository.RunAsync("remote", "remove", remote), refreshAssets: false);
        }

        // ── 스태시 ──

        /// <param name="drop">true 면 Pop(적용하고 목록에서 뺀다), false 면 Apply(목록에 남긴다).</param>
        public async Task ApplyStashAsync(GitStashRef stash, bool drop)
        {
            if (_services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }
            string operation = GitText.Format(drop ? "op.stashPop" : "op.stashApply", stash.Name);
            await _services.Runner.RunAsync(operation, () => _services.Commands.StashApplyAsync(stash.Name, drop), refreshAssets: true);
        }

        public async Task DropStashAsync(GitStashRef stash)
        {
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("refs.stashDropTitle"), GitText.Format("refs.stashDropConfirm", stash.Name, stash.Message), GitText.Get("common.delete"),
                    danger: true, icon: ActionGlyphKind.Stash) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.stashDrop", stash.Name), () => _services.Commands.StashDropAsync(stash.Name), refreshAssets: false);
        }

        public void ShowStash(GitStashRef stash)
        {
            GitCompareWindow.OpenCommit(stash.Hash);
        }

        public async Task BranchFromStashAsync(GitStashRef stash)
        {
            string title = GitText.Get("refs.stashBranchTitle");
            (List<GitBranchRef> branches, _) = await _services.History.BranchesAsync();
            List<string> existing = branches?.Where(b => b.IsRemote == false).Select(b => b.Name).ToList() ?? new List<string>();
            string name = (await _services.Dialogs.PromptAsync(title, GitText.Format("refs.stashBranchPrompt", stash.Name), string.Empty,
                validate: value => BranchActions.BranchNameProblemText(value, existing), icon: ActionGlyphKind.Branch))?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            if (await BranchActions.PrepareWorkingTreeAsync(_services, GitText.Format("branches.actionCreateSwitch", name)) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.stashBranch", name), () => _services.Commands.StashBranchAsync(name, stash.Name), refreshAssets: true);
        }

        // ── 워크트리·서브모듈 ──

        public void OpenFolder(string path)
        {
            GitFileManager.OpenFolder(path);
        }

        public void OpenTerminal(string path)
        {
            string error = GitTerminal.Open(path);
            if (error != null)
            {
                _ = _services.Dialogs.AlertAsync(GitText.Get("actionbar.terminalFailed"), error, ActionGlyphKind.Terminal);
            }
        }

        public async Task RemoveWorktreeAsync(GitWorktreeRef worktree)
        {
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("refs.worktreeRemoveTitle"), GitText.Format("refs.worktreeRemoveConfirm", worktree.Path), GitText.Get("common.delete"),
                    danger: true, icon: ActionGlyphKind.Worktree) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.removeWorktree", worktree.DisplayName), () => _services.Commands.RemoveWorktreeAsync(worktree.Path), refreshAssets: false);
        }

        /// <param name="path">저장소 루트 기준 경로. "." 이면 모든 서브모듈.</param>
        public async Task UpdateSubmoduleAsync(string path)
        {
            if (_services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Format("op.updateSubmodule", path), () => _services.Commands.UpdateSubmoduleAsync(path), refreshAssets: true);
        }

        public static string SubmoduleFullPath(GitSubmoduleRef submodule)
        {
            return Path.Combine(GitRepository.RepoRoot ?? GitRepository.ProjectRoot, submodule.Path).Replace('\\', '/');
        }
    }
}
