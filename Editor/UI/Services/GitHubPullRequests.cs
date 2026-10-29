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

        /// <summary>
        /// 필터 조건으로 GitHub 를 검색해 맞는 열린 PR 의 번호를 받는다(<c>GET /search/issues</c>). <c>@me</c> 는 GitHub 가 푼다.
        /// 이 패키지가 받은 PR 정보로 풀 수 없는 조건(review:·status:·created: …)에만 쓴다.
        /// </summary>
        public static async Task<(HashSet<int> numbers, string error)> SearchNumbersAsync(GitHubRemoteRepo repository, string query)
        {
            string q = $"repo:{repository.Owner}/{repository.Repository} is:pr is:open {query}".Trim();
            string url = "https://api.github.com/search/issues?per_page=100&q=" + UnityWebRequest.EscapeURL(q);
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.SetRequestHeader("User-Agent", UserAgent);
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.SetRequestHeader("Authorization", "Bearer " + GitHubAccount.Token);
            await GitHubAccount.SendAsync(request);
            if (request.result != UnityWebRequest.Result.Success)
            {
                return (new HashSet<int>(), GitPullRequestForm.ParseError(request.downloadHandler?.text) ?? request.error);
            }
            return (GitPullRequestList.ParseSearchNumbers(request.downloadHandler.text), null);
        }

        /// <summary>PR 하나의 자세한 정보. GitHub 가 병합 가능 여부를 아직 계산 중이면 <see cref="GitPullRequestDetail.Mergeable"/> 이 null.</summary>
        public static async Task<(GitPullRequestDetail detail, string error)> GetAsync(GitHubRemoteRepo repository, int number)
        {
            using UnityWebRequest request = Get($"https://api.github.com/repos/{repository.Owner}/{repository.Repository}/pulls/{number}");
            await GitHubAccount.SendAsync(request);
            if (request.result != UnityWebRequest.Result.Success)
            {
                return (null, GitPullRequestForm.ParseError(request.downloadHandler?.text) ?? request.error);
            }
            return (GitPullRequestDetails.Parse(request.downloadHandler.text), null);
        }

        /// <summary>PR 이 바꾼 파일(최대 300개 — GitHub 가 한 번에 주는 만큼).</summary>
        public static async Task<(List<GitPullRequestFile> files, string error)> FilesAsync(GitHubRemoteRepo repository, int number)
        {
            List<GitPullRequestFile> all = new();
            for (int page = 1; page <= MaxPages; page++)
            {
                using UnityWebRequest request = Get($"https://api.github.com/repos/{repository.Owner}/{repository.Repository}/pulls/{number}/files?per_page={PageSize}&page={page}");
                await GitHubAccount.SendAsync(request);
                if (request.result != UnityWebRequest.Result.Success)
                {
                    return (all, GitPullRequestForm.ParseError(request.downloadHandler?.text) ?? request.error);
                }
                List<GitPullRequestFile> items = GitPullRequestDetails.ParseFiles(request.downloadHandler.text);
                all.AddRange(items);
                if (items.Count < PageSize)
                {
                    break;
                }
            }
            return (all, null);
        }

        /// <summary>
        /// PR 을 병합한다(<c>PUT …/pulls/{number}/merge</c>). <paramref name="headSha"/> 를 주면 그 사이 새 커밋이 올라왔을 때 GitHub 가 거절한다.
        /// </summary>
        /// <returns>성공하면 null, 실패하면 오류 문구.</returns>
        public static async Task<string> MergeAsync(GitHubRemoteRepo repository, int number, GitPullRequestMergeMethod method, string headSha)
        {
            string json = GitPullRequestDetails.MergeRequestJson(method, headSha);
            using UnityWebRequest request = new($"https://api.github.com/repos/{repository.Owner}/{repository.Repository}/pulls/{number}/merge", UnityWebRequest.kHttpVerbPUT)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            SetHeaders(request);
            request.SetRequestHeader("Content-Type", "application/json");
            await GitHubAccount.SendAsync(request);
            return request.result == UnityWebRequest.Result.Success ? null : GitPullRequestForm.ParseError(request.downloadHandler?.text) ?? request.error;
        }

        private static UnityWebRequest Get(string url)
        {
            UnityWebRequest request = UnityWebRequest.Get(url);
            SetHeaders(request);
            return request;
        }

        private static void SetHeaders(UnityWebRequest request)
        {
            request.SetRequestHeader("User-Agent", UserAgent);
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.SetRequestHeader("Authorization", "Bearer " + GitHubAccount.Token);
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
