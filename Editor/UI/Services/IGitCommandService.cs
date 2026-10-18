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

        public Task<GitResult> CommitStagedAsync(string message, bool amend) => GitUndo.CommitStagedAsync(message, amend);

        public Task<string> LastCommitMessageAsync() => GitOperations.LastCommitMessageAsync();

        public GitUndoJournal UndoJournal => GitUndo.Journal;

        public Task<GitResult> UndoAsync() => GitUndo.UndoAsync();

        public Task<GitResult> RedoAsync() => GitUndo.RedoAsync();
    }
}
