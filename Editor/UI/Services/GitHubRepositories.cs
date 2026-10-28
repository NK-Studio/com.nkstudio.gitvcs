using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace GitVersionControl
{
    /// <summary>
    /// 로그인한 GitHub 계정(<see cref="GitHubAccount"/>)이 볼 수 있는 저장소 목록. 원격 추가 서랍의 GitHub 탭이 쓴다.
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
                request.SetRequestHeader("User-Agent", UserAgent);
                request.SetRequestHeader("Accept", "application/vnd.github+json");
                request.SetRequestHeader("Authorization", "Bearer " + GitHubAccount.Token);
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
    }
}
