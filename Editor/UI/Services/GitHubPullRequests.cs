using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace GitVersionControl
{
    /// <summary>
    /// GitHub Pull Request 읽기(<c>GET /repos/{owner}/{repo}/pulls</c>)와 만들기(<c>POST</c>). 로그인한 계정(<see cref="GitHubAccount"/>)의 토큰을 쓴다.
    /// 요청·응답 모양은 <see cref="GitPullRequestForm"/> · <see cref="GitPullRequestList"/>.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitHubPullRequests
    {
        private const string UserAgent = "com.nkstudio.gitvcs";

        /// <summary>
        /// 너무 많은 PR 이 열린 저장소에서 끝없이 읽지 않게 막는다(최근에 고친 순 300개).
        /// </summary>
        private const int MaxPages = 3;

        private const int PageSize = 100;

        /// <summary>
        /// 열려 있는 PR 목록(최근에 고친 순). 실패하면 그때까지 읽은 것과 오류 문구.
        /// </summary>
        public static async Task<(List<GitPullRequestInfo> pulls, string error)> ListOpenAsync(GitHubRemoteRepo repository)
        {
            List<GitPullRequestInfo> all = new();
            for (int page = 1; page <= MaxPages; page++)
            {
                string url = $"https://api.github.com/repos/{repository.Owner}/{repository.Repository}/pulls?state=open&sort=updated&direction=desc&per_page={PageSize}&page={page}";
                using UnityWebRequest request = UnityWebRequest.Get(url);
                request.SetRequestHeader("User-Agent", UserAgent);
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("Authorization", "Bearer " + GitHubAccount.Token);
                await GitHubAccount.SendAsync(request);
                if (request.result != UnityWebRequest.Result.Success)
                {
                    return (all, GitPullRequestForm.ParseError(request.downloadHandler?.text) ?? request.error);
                }
                List<GitPullRequestInfo> items = GitPullRequestList.Parse(request.downloadHandler.text);
                all.AddRange(items);
                if (items.Count < PageSize)
                {
                    break;
                }
            }
            return (all, null);
        }

        /// <returns>성공하면 (번호, 주소, null), 실패하면 (0, null, 오류 문구).</returns>
        public static async Task<(int number, string url, string error)> CreateAsync(GitHubRemoteRepo to, string head, string baseBranch, string title, string body, bool draft)
        {
            string json = GitPullRequestForm.RequestJson(title, body, head, baseBranch, draft);
            using UnityWebRequest request = new($"https://api.github.com/repos/{to.Owner}/{to.Repository}/pulls", UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            request.SetRequestHeader("User-Agent", UserAgent);
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + GitHubAccount.Token);
            await GitHubAccount.SendAsync(request);
            string text = request.downloadHandler?.text;
            if (request.result != UnityWebRequest.Result.Success)
            {
                return (0, null, GitPullRequestForm.ParseError(text) ?? request.error);
            }
            (int number, string url) = GitPullRequestForm.ParseCreated(text);
            return (number, url, null);
        }
    }
}
