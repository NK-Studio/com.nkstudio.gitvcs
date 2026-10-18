using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>읽기 전용 히스토리 조회. 뷰는 <see cref="GitHistory"/> 대신 이 인터페이스로 묻는다.</summary>
    public interface IGitHistoryService
    {
        Task<(List<GitCommitInfo> commits, string error)> LogAsync(string revisionRange, bool allRefs, int skip, int maxCount);
        Task<(List<GitCommitInfo> commits, string error)> GraphLogAsync(DateTime? since, int maxCount);
        Task<List<GitFileChange>> CommitFilesAsync(string hash);
        Task<string> CommitMessageAsync(string hash);
        Task<List<GitFileChange>> ChangedSinceMergeBaseAsync(string from, string to);
        Task<(List<GitBranchRef> branches, string error)> BranchesAsync();
    }

    public sealed class GitHistoryService : IGitHistoryService
    {
        public Task<(List<GitCommitInfo> commits, string error)> LogAsync(string revisionRange, bool allRefs, int skip, int maxCount)
            => GitHistory.LogAsync(revisionRange, allRefs, skip, maxCount);

        public Task<(List<GitCommitInfo> commits, string error)> GraphLogAsync(DateTime? since, int maxCount)
            => GitHistory.GraphLogAsync(since, maxCount);

        public Task<List<GitFileChange>> CommitFilesAsync(string hash) => GitHistory.CommitFilesAsync(hash);

        public Task<string> CommitMessageAsync(string hash) => GitHistory.CommitMessageAsync(hash);

        public Task<List<GitFileChange>> ChangedSinceMergeBaseAsync(string from, string to) => GitHistory.ChangedSinceMergeBaseAsync(from, to);

        public Task<(List<GitBranchRef> branches, string error)> BranchesAsync() => GitHistory.BranchesAsync();
    }
}
