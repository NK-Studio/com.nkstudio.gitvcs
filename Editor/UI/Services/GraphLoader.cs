using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// Branch Explorer·GitKraken 그래프가 함께 쓰는 "커밋 + 브랜치 읽고 그래프 만들기".
    /// 커밋·브랜치가 수천 개인 저장소에서도 창이 멈추지 않게 git 두 명령을 동시에 돌리고, 그래프 배치는 작업 스레드에서 한다.
    /// </summary>
    public static class GraphLoader
    {
        /// <summary>
        /// 메인 스레드에서 부른다. 끝나면 메인 스레드로 돌아온다(에디터의 동기화 컨텍스트).
        /// </summary>
        /// <param name="headHash">없으면(커밋 없는 저장소) 커밋은 읽지 않는다.</param>
        public static async Task<(GitGraph graph, string error)> LoadAsync(IGitHistoryService history, string headHash, DateTime? since, int maxCommits)
        {
            Task<(List<GitCommitInfo> commits, string error)> logTask = headHash == null
                ? Task.FromResult((new List<GitCommitInfo>(), (string)null))
                : history.GraphLogAsync(since, maxCommits);
            Task<(List<GitBranchRef> branches, string error)> branchTask = history.BranchesAsync();
            (List<GitCommitInfo> commits, string logError) = await logTask;
            (List<GitBranchRef> branches, string branchError) = await branchTask;

            // GitText 는 메인 스레드 전용이라 여기서 읽어 넘긴다.
            string anonymousLaneName = GitText.Get("graph.mergedBranch");
            GitGraph graph = await Task.Run(() => GitGraph.Build(commits, branches, headHash, anonymousLaneName));
            return (graph, logError ?? branchError);
        }
    }
}
