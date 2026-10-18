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
