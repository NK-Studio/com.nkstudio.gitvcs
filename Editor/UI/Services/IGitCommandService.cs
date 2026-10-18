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
    }

    public sealed class GitCommandService : IGitCommandService
    {
        public Task<GitResult> CommitAsync(IReadOnlyCollection<GitStatusEntry> entries, string message) => GitOperations.CommitAsync(entries, message);

        public Task<GitResult> DiscardAsync(IReadOnlyCollection<GitStatusEntry> entries) => GitOperations.DiscardAsync(entries);

        public Task<GitResult> FetchAsync() => GitOperations.FetchAsync();

        public Task<GitResult> PullAsync() => GitOperations.PullAsync();

        public Task<GitResult> PushAsync(GitBranchInfo branch) => GitOperations.PushAsync(branch);

        public Task<GitResult> SwitchAsync(GitBranchRef branch) => GitOperations.SwitchAsync(branch);

        public Task<GitResult> CreateBranchAsync(string name, bool switchTo) => GitOperations.CreateBranchAsync(name, switchTo);

        public Task<GitResult> DeleteBranchAsync(string name, bool force) => GitOperations.DeleteBranchAsync(name, force);

        public Task<GitResult> ValidateBranchNameAsync(string name) => GitOperations.ValidateBranchNameAsync(name);
    }
}
