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

        public GitUndoJournal UndoJournal => GitUndo.Journal;

        public Task<GitResult> UndoAsync() => GitUndo.UndoAsync();

        public Task<GitResult> RedoAsync() => GitUndo.RedoAsync();
    }
}
