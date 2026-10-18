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
