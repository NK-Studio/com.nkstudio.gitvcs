using System.Collections.Generic;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>작업 트리·브랜치·원격을 바꾸는 명령. 뷰는 <see cref="GitOperations"/> 대신 이 인터페이스로 실행한다.</summary>
    public interface IGitCommandService
    {
        Task<GitResult> CommitAsync(IReadOnlyCollection<GitStatusEntry> entries, string message);
        Task<GitResult> DiscardAsync(IReadOnlyCollection<GitStatusEntry> entries);
        Task<GitResult> FetchAsync();
        Task<GitResult> PullAsync();
        Task<GitResult> PushAsync(GitBranchInfo branch);
        Task<GitResult> SwitchAsync(GitBranchRef branch);
        Task<GitResult> CreateBranchAsync(string name, bool switchTo);
        Task<GitResult> DeleteBranchAsync(string name, bool force);
        Task<GitResult> ValidateBranchNameAsync(string name);

        /// <summary>
        /// 커밋 하나를 대상으로 하는 작업(Branch Explorer 커밋 메뉴). 인자는 <see cref="GitCommitCommands"/> 가 만든다.
        /// </summary>
        Task<GitResult> CreateBranchAtAsync(string name, string commit, bool switchTo);
        Task<GitResult> SwitchDetachedAsync(string commit);
        Task<GitResult> CreateTagAsync(string name, string commit);
        Task<GitResult> ValidateTagNameAsync(string name);

        /// <summary>
        /// HEAD 를 옮기는 작업(병합·체리픽·revert·reset). 실행 취소 목록에 기록된다.
        /// </summary>
        Task<GitResult> MoveHeadAsync(string title, string[] args, string undoResetMode);

        /// <summary>현재 브랜치를 rebase 한다. 충돌하면 설정에 따라 취소해 원래대로 돌리거나 멈춰 둔다(<see cref="GitUndo.RebaseAsync"/>).</summary>
        Task<GitResult> RebaseAsync(string title, string revision);

        /// <summary>
        /// 충돌로 멈춘 rebase·병합·cherry-pick·revert 를 이어가기·건너뛰기·취소한다(<see cref="GitInProgress"/>).
        /// </summary>
        Task<GitResult> ContinueOperationAsync();
        Task<GitResult> SkipOperationAsync();
        Task<GitResult> AbortOperationAsync();

        /// <summary>충돌 파일 하나를 해결됨으로 표시한다. 한쪽을 고르면 그 내용으로 바꾼 뒤 표시한다.</summary>
        Task<GitResult> ResolveConflictAsync(GitStatusEntry entry, GitConflictSide side);

        Task<GitResult> RenameBranchAsync(string oldName, string newName);
        Task<GitResult> PushBranchAsync(string remote, string branch, bool setUpstream);
        Task<GitResult> DeleteRemoteBranchAsync(string remote, string branch);
        Task<GitResult> FastForwardBranchAsync(string branch, string upstream);
        Task<GitResult> SetUpstreamAsync(string branch, string upstream);

        /// <summary>
        /// 브랜치가 추적하는 원격 브랜치("origin/main"). 없으면 null.
        /// </summary>
        Task<string> UpstreamOfAsync(string branch);

        Task<GitResult> SavePatchAsync(string commit, string path);

        /// <summary>
        /// 사이드바 참조 트리의 태그·스태시·워크트리·서브모듈 작업. 인자는 <see cref="GitCommitCommands"/> 가 만든다.
        /// </summary>
        Task<GitResult> DeleteTagAsync(string name);
        Task<GitResult> PushTagAsync(string remote, string name);

        /// <summary>
        /// 원격을 더하고(Push 주소가 따로 있으면 함께 적고) 그 원격만 fetch 한다. 더하기가 실패하면 fetch 하지 않는다.
        /// </summary>
        Task<GitResult> AddRemoteAsync(string name, string pullUrl, string pushUrl);

        Task<GitResult> AddWorktreeAsync(string path, string reference, string newBranch);

        /// <summary>
        /// 서브모듈을 받아(clone) .gitmodules 와 인덱스에 올린다. 네트워크를 쓴다.
        /// </summary>
        Task<GitResult> AddSubmoduleAsync(string url, string path);
        Task<GitResult> DeleteRemoteTagAsync(string remote, string name);
        Task<GitResult> StashApplyAsync(string stash, bool drop);
        Task<GitResult> StashDropAsync(string stash);
        Task<GitResult> StashBranchAsync(string branch, string stash);
        Task<GitResult> RemoveWorktreeAsync(string path);
        Task<GitResult> UpdateSubmoduleAsync(string path);

        /// <summary>
        /// 현재 브랜치 히스토리의 커밋 하나를 고친다(<see cref="GitRewrite"/>). 실행 취소 목록에 기록된다.
        /// </summary>
        /// <param name="newMessage">메시지 바꾸기·squash 의 새 메시지. null 이면 규칙대로 만든다.</param>
        Task<GitResult> RewriteAsync(GitRewriteKind kind, string commit, string newMessage, string undoTitle);

        /// <summary>
        /// 리비전(브랜치·태그·해시·HEAD~2)의 커밋 해시. 없으면 null.
        /// </summary>
        Task<string> ResolveCommitAsync(string revision);
        Task<GitResult> StashAsync();
        Task<GitResult> StashPopAsync();
        Task<int> StashCountAsync();

        /// <summary>
        /// 되돌리기·다시 하기 목록. 커밋·전환·브랜치 만들기/지우기·Stash·Pull 이 여기 기록된다.
        /// </summary>
        Task<GitResult> StageAsync(IReadOnlyCollection<GitStatusEntry> entries);
        Task<GitResult> UnstageAsync(IReadOnlyCollection<GitStatusEntry> entries);
        Task<GitResult> StageAllAsync();
        Task<GitResult> UnstageAllAsync();
        Task<GitResult> DiscardWorkTreeAsync(IReadOnlyCollection<GitStatusEntry> entries);

        /// <summary>
        /// diff 뷰어에서 고른 줄·hunk 만 인덱스에 올리거나(<c>git apply --cached</c>) 내린다(<paramref name="reverse"/>, <c>--reverse</c>).
        /// 패치는 <see cref="GitPartialPatch"/> 가 만든다. 작업 트리 파일은 건드리지 않는다.
        /// </summary>
        /// <param name="zeroContext">문맥 줄 없이(-U0) 만든 diff 면 true — git apply 는 기본으로 문맥 없는 패치를 거부한다(<c>--unidiff-zero</c>).</param>
        /// <param name="intentToAddPath">아직 git 이 모르는(untracked) 파일이면 그 경로. 먼저 <c>git add -N</c> 으로 인덱스에 빈 항목을 만들어야 패치가 붙는다. 아니면 null.</param>
        Task<GitResult> ApplyPatchToIndexAsync(string patch, bool reverse, bool zeroContext, string intentToAddPath);

        /// <summary>
        /// diff 의 "Discard Hunk": 패치를 작업 사본에 거꾸로 적용해 그 변경을 되돌린다(<c>git apply --reverse</c>, 인덱스는 그대로). 되돌릴 수 없다.
        /// </summary>
        Task<GitResult> DiscardPatchFromWorkTreeAsync(string patch, bool zeroContext);
        Task<GitResult> CommitStagedAsync(string message, bool amend);
        Task<string> LastCommitMessageAsync();

        GitUndoJournal UndoJournal { get; }
        Task<GitResult> UndoAsync();
        Task<GitResult> RedoAsync();
    }

    public sealed class GitCommandService : IGitCommandService
    {
        public Task<GitResult> CommitAsync(IReadOnlyCollection<GitStatusEntry> entries, string message) => GitUndo.CommitAsync(entries, message);

        public Task<GitResult> DiscardAsync(IReadOnlyCollection<GitStatusEntry> entries) => GitOperations.DiscardAsync(entries);

        public Task<GitResult> FetchAsync() => GitOperations.FetchAsync();

        public Task<GitResult> PullAsync() => GitUndo.PullAsync();

        public Task<GitResult> PushAsync(GitBranchInfo branch) => GitOperations.PushAsync(branch);

        public Task<GitResult> SwitchAsync(GitBranchRef branch) => GitUndo.SwitchAsync(branch);

        public Task<GitResult> CreateBranchAsync(string name, bool switchTo) => GitUndo.CreateBranchAsync(name, switchTo);

        public Task<GitResult> DeleteBranchAsync(string name, bool force) => GitUndo.DeleteBranchAsync(name, force);

        public Task<GitResult> ValidateBranchNameAsync(string name) => GitOperations.ValidateBranchNameAsync(name);

        public Task<GitResult> CreateBranchAtAsync(string name, string commit, bool switchTo) => GitUndo.CreateBranchAsync(name, switchTo, commit);

        public Task<GitResult> SwitchDetachedAsync(string commit) => GitUndo.SwitchDetachedAsync(commit);

        public Task<GitResult> CreateTagAsync(string name, string commit) => GitRepository.RunAsync(GitCommitCommands.Tag(name, commit));

        public Task<GitResult> ValidateTagNameAsync(string name) => GitRepository.RunAsync(GitCommitCommands.ValidateTagName(name));

        public Task<GitResult> MoveHeadAsync(string title, string[] args, string undoResetMode) => GitUndo.MoveHeadAsync(title, args, undoResetMode);
        public Task<GitResult> RebaseAsync(string title, string revision) => GitUndo.RebaseAsync(title, revision);

        public Task<GitResult> ContinueOperationAsync() => GitInProgress.ContinueAsync();

        public Task<GitResult> SkipOperationAsync() => GitInProgress.SkipAsync();

        public Task<GitResult> AbortOperationAsync() => GitInProgress.AbortAsync();

        public Task<GitResult> ResolveConflictAsync(GitStatusEntry entry, GitConflictSide side) => GitInProgress.ResolveAsync(entry, side);

        public Task<GitResult> RenameBranchAsync(string oldName, string newName) => GitRepository.RunAsync(GitCommitCommands.RenameBranch(oldName, newName));

        public Task<GitResult> PushBranchAsync(string remote, string branch, bool setUpstream) => GitOperations.RunNetworkAsync(GitCommitCommands.PushBranch(remote, branch, setUpstream));

        public Task<GitResult> DeleteRemoteBranchAsync(string remote, string branch) => GitOperations.RunNetworkAsync(GitCommitCommands.DeleteRemoteBranch(remote, branch));

        public Task<GitResult> FastForwardBranchAsync(string branch, string upstream) => GitRepository.RunAsync(GitCommitCommands.FastForwardBranch(branch, upstream));

        public Task<GitResult> SetUpstreamAsync(string branch, string upstream) => GitRepository.RunAsync(GitCommitCommands.SetUpstream(branch, upstream));

        public async Task<string> UpstreamOfAsync(string branch)
        {
            GitResult result = await GitRepository.RunAsync(GitCommitCommands.UpstreamOf(branch));
            return result.Success && string.IsNullOrWhiteSpace(result.Output) == false ? result.Output.Trim() : null;
        }

        public Task<GitResult> SavePatchAsync(string commit, string path) => GitCommand.RunToFileAsync(GitRepository.RepoRoot, GitCommitCommands.FormatPatch(commit), path);

        public Task<GitResult> DeleteTagAsync(string name) => GitRepository.RunAsync(GitCommitCommands.DeleteTag(name));

        public Task<GitResult> PushTagAsync(string remote, string name) => GitOperations.RunNetworkAsync(GitCommitCommands.PushTag(remote, name));

        public async Task<GitResult> AddRemoteAsync(string name, string pullUrl, string pushUrl)
        {
            GitResult added = await GitRepository.RunAsync(GitCommitCommands.AddRemote(name, pullUrl));
            if (added.Success == false)
            {
                return added;
            }
            if (string.IsNullOrWhiteSpace(pushUrl) == false && pushUrl != pullUrl)
            {
                GitResult pushSet = await GitRepository.RunAsync(GitCommitCommands.SetPushUrl(name, pushUrl));
                if (pushSet.Success == false)
                {
                    return pushSet;
                }
            }
            return await GitOperations.RunNetworkAsync(GitCommitCommands.FetchRemote(name));
        }

        public Task<GitResult> AddWorktreeAsync(string path, string reference, string newBranch) => GitRepository.RunAsync(GitCommitCommands.AddWorktree(path, reference, newBranch));

        public Task<GitResult> AddSubmoduleAsync(string url, string path) => GitOperations.RunNetworkAsync(GitCommitCommands.AddSubmodule(url, path));

        public Task<GitResult> DeleteRemoteTagAsync(string remote, string name) => GitOperations.RunNetworkAsync(GitCommitCommands.DeleteRemoteTag(remote, name));

        public Task<GitResult> StashApplyAsync(string stash, bool drop) => GitRepository.RunAsync(GitCommitCommands.StashApply(stash, drop));

        public Task<GitResult> StashDropAsync(string stash) => GitRepository.RunAsync(GitCommitCommands.StashDrop(stash));

        public Task<GitResult> StashBranchAsync(string branch, string stash) => GitRepository.RunAsync(GitCommitCommands.StashBranch(branch, stash));

        public Task<GitResult> RemoveWorktreeAsync(string path) => GitRepository.RunAsync(GitCommitCommands.RemoveWorktree(path));

        public Task<GitResult> UpdateSubmoduleAsync(string path) => GitOperations.RunNetworkAsync(GitCommitCommands.UpdateSubmodule(path));

        public Task<GitResult> RewriteAsync(GitRewriteKind kind, string commit, string newMessage, string undoTitle) => GitRewrite.RunAsync(kind, commit, newMessage, undoTitle);

        public async Task<string> ResolveCommitAsync(string revision)
        {
            GitResult result = await GitRepository.RunAsync(GitCommitCommands.ResolveCommit(revision));
            return result.Success ? result.Output.Trim() : null;
        }

        public Task<GitResult> StashAsync() => GitUndo.StashAsync();

        public Task<GitResult> StashPopAsync() => GitOperations.StashPopAsync();

        public Task<int> StashCountAsync() => GitOperations.StashCountAsync();

        public Task<GitResult> StageAsync(IReadOnlyCollection<GitStatusEntry> entries) => GitOperations.StageAsync(entries);

        public Task<GitResult> UnstageAsync(IReadOnlyCollection<GitStatusEntry> entries) => GitOperations.UnstageAsync(entries);

        public Task<GitResult> StageAllAsync() => GitOperations.StageAllAsync();

        public Task<GitResult> UnstageAllAsync() => GitOperations.UnstageAllAsync();

        public Task<GitResult> DiscardWorkTreeAsync(IReadOnlyCollection<GitStatusEntry> entries) => GitOperations.DiscardWorkTreeAsync(entries);

        public Task<GitResult> ApplyPatchToIndexAsync(string patch, bool reverse, bool zeroContext, string intentToAddPath) => GitOperations.ApplyPatchToIndexAsync(patch, reverse, zeroContext, intentToAddPath);

        public Task<GitResult> DiscardPatchFromWorkTreeAsync(string patch, bool zeroContext) => GitOperations.DiscardPatchFromWorkTreeAsync(patch, zeroContext);

        public Task<GitResult> CommitStagedAsync(string message, bool amend) => GitUndo.CommitStagedAsync(message, amend);

        public Task<string> LastCommitMessageAsync() => GitOperations.LastCommitMessageAsync();

        public GitUndoJournal UndoJournal => GitUndo.Journal;

        public Task<GitResult> UndoAsync() => GitUndo.UndoAsync();

        public Task<GitResult> RedoAsync() => GitUndo.RedoAsync();
    }
}
