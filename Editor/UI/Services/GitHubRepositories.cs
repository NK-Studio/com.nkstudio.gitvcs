using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace GitVersionControl
{
    /// <summary>
    /// 로그인한 GitHub 계정(<see cref="GitHubAccount"/>)이 볼 수 있는 저장소 목록과 새 저장소 만들기. 원격 추가 서랍의 GitHub·새 저장소 탭이 쓴다.
    /// 한 번 읽으면 에디터를 끌 때까지(또는 로그인이 바뀔 때까지) 기억해 서랍을 다시 열 때 기다리지 않는다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitHubRepositories
    {
        private const string UserAgent = "com.nkstudio.gitvcs";
        private const int PageSize = 100;

        /// <summary>
        /// 너무 많은 저장소를 가진 계정(조직 포함)에서 끝없이 읽지 않게 막는다. 최근에 고친 순이라 앞쪽이 쓸 만한 것들이다.
        /// </summary>
        private const int MaxPages = 10;

        private static string _cachedLogin;
        private static List<GitHubRepository> _cached;

        /// <summary>
        /// 저장소 목록과 오류(성공이면 null). 로그인하지 않았으면 빈 목록과 null.
        /// </summary>
        public static async Task<(List<GitHubRepository> repositories, string error)> ListAsync(bool reload = false)
        {
            if (GitHubAccount.IsSignedIn == false)
            {
                return (new List<GitHubRepository>(), null);
            }
            string login = GitHubAccount.Login;
            if (reload == false && _cached != null && _cachedLogin == login)
            {
                return (_cached, null);
            }
            List<GitHubRepository> all = new();
            for (int page = 1; page <= MaxPages; page++)
            {
                string url = $"https://api.github.com/user/repos?per_page={PageSize}&page={page}&sort=updated&affiliation=owner,collaborator,organization_member";
                using UnityWebRequest request = UnityWebRequest.Get(url);
                SetHeaders(request);
                await GitHubAccount.SendAsync(request);
                if (request.result != UnityWebRequest.Result.Success)
                {
                    return (all, request.error);
                }
                List<GitHubRepository> items = GitRemoteForm.ParseRepositories(request.downloadHandler.text);
                all.AddRange(items);
                if (items.Count < PageSize)
                {
                    break;
                }
            }
            _cachedLogin = login;
            _cached = all;
            return (all, null);
        }

        /// <summary>
        /// 이 계정이 속한 조직 이름(<c>GET /user/orgs</c>). 새 저장소의 소유자로 고를 수 있게 한다.
        /// 조직이 공개하지 않은 소속은 토큰 범위(repo)로 보이지 않을 수 있어, 실패해도 오류로 막지 않고 빈 목록을 준다.
        /// </summary>
        public static async Task<List<string>> OrganizationsAsync()
        {
            if (GitHubAccount.IsSignedIn == false)
            {
                return new List<string>();
            }
            using UnityWebRequest request = UnityWebRequest.Get("https://api.github.com/user/orgs?per_page=100");
            SetHeaders(request);
            await GitHubAccount.SendAsync(request);
            return request.result == UnityWebRequest.Result.Success ? GitRemoteForm.ParseOrganizations(request.downloadHandler.text) : new List<string>();
        }

        /// <summary>
        /// 새 저장소를 만든다: 내 계정이면 <c>POST /user/repos</c>, 조직이면 <c>POST /orgs/{owner}/repos</c>. 빈 저장소(README 없이)로 만들어 처음 Push 가 그대로 올라가게 한다.
        /// 만든 저장소는 기억한 목록에도 넣어, 서랍을 다시 열 때 GitHub 탭에 바로 보인다.
        /// </summary>
        /// <returns>만든 저장소, 또는 실패 문구(GitHub 가 준 까닭).</returns>
        public static async Task<(GitHubRepository repository, string error)> CreateAsync(string owner, string name, bool isPrivate)
        {
            string url = string.Equals(owner, GitHubAccount.Login, System.StringComparison.OrdinalIgnoreCase)
                ? "https://api.github.com/user/repos"
                : $"https://api.github.com/orgs/{owner}/repos";
            using UnityWebRequest request = new(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(GitRemoteForm.CreateRepositoryJson(name, isPrivate))),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            SetHeaders(request);
            request.SetRequestHeader("Content-Type", "application/json");
            await GitHubAccount.SendAsync(request);
            if (request.result != UnityWebRequest.Result.Success)
            {
                return (null, GitPullRequestForm.ParseError(request.downloadHandler?.text) ?? request.error);
            }
            GitHubRepository repository = GitRemoteForm.ParseRepository(request.downloadHandler.text);
            if (repository == null)
            {
                return (null, GitText.Get("remote.createBadResponse"));
            }
            if (_cached != null && _cachedLogin == GitHubAccount.Login)
            {
                _cached.Insert(0, repository);
            }
            return (repository, null);
        }

        /// <summary>
        /// 저장소의 기본 브랜치를 정한다(<c>PATCH /repos/{fullName}</c>). 첫 Push 가 이미 기본 브랜치가 되지만, 여러 브랜치를 올린 뒤 확실히 해 둔다.
        /// </summary>
        /// <returns>성공하면 null, 실패하면 오류 문구.</returns>
        public static async Task<string> SetDefaultBranchAsync(string fullName, string branch)
        {
            using UnityWebRequest request = new($"https://api.github.com/repos/{fullName}", "PATCH")
            {
                uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(GitRemoteForm.DefaultBranchJson(branch))),
                downloadHandler = new DownloadHandlerBuffer(),
            };
            SetHeaders(request);
            request.SetRequestHeader("Content-Type", "application/json");
            await GitHubAccount.SendAsync(request);
            return request.result == UnityWebRequest.Result.Success ? null : GitPullRequestForm.ParseError(request.downloadHandler?.text) ?? request.error;
        }

        private static void SetHeaders(UnityWebRequest request)
        {
            request.SetRequestHeader("User-Agent", UserAgent);
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.SetRequestHeader("Authorization", "Bearer " + GitHubAccount.Token);
        }
    }
}
