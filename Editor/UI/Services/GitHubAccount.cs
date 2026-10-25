using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GitVersionControl
{
    /// <summary>
    /// Preferences > Git 에서 로그인한 GitHub 계정. 아바타가 비공개 저장소의 커밋 작성자를 찾고, 내 커밋에 내 사진을 붙일 때 쓴다.
    ///
    /// 개인 설정이라 프로젝트(UserSettings)가 아니라 EditorPrefs 에 둔다 — 이 PC 의 모든 Unity 프로젝트가 같은 로그인을 쓴다.
    /// 트레이드오프: EditorPrefs 는 OS 사용자 영역(Windows 레지스트리·macOS plist)에 평문으로 남는다. 범위가 이 사용자 계정으로 한정되고
    /// 로그아웃하면 지우므로 받아들였다. 운영체제 비밀 저장소는 플랫폼마다 따로 만들어야 해서 쓰지 않았다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitHubAccount
    {
        /// <summary>
        /// GitHub OAuth App 의 Client ID(공개 값 — 비밀이 아니다). NK-Studio 이름으로 등록한 앱이며 "Enable Device Flow" 가 켜져 있어야 한다.
        /// 로그인은 Device Flow(브라우저에 코드 입력)만 쓴다 — 브라우저가 돌아오는 웹 방식은 Client secret 을 패키지에 공개로 넣어야 해서 쓰지 않는다.
        /// 비어 있으면 로그인 버튼이 꺼지고 등록 방법을 안내한다.
        /// </summary>
        public const string ClientId = "Ov23liwBBq0RMqbmU44U";

        /// <summary>
        /// 비공개 저장소의 커밋을 읽으려면 repo 가 필요하다(OAuth App 에는 읽기 전용 비공개 저장소 권한이 따로 없다).
        /// </summary>
        public const string Scope = "repo";

        private const string KeyPrefix = "GitVersionControl.GitHub.";
        private const string TokenKey = KeyPrefix + "Token";
        private const string LoginKey = KeyPrefix + "Login";
        private const string AvatarUrlKey = KeyPrefix + "AvatarUrl";
        private const string UserAgent = "com.nkstudio.gitvcs";

        /// <summary>
        /// 로그인·로그아웃했을 때.
        /// </summary>
        public static event Action Changed;

        public static bool IsConfigured => string.IsNullOrEmpty(ClientId) == false;

        public static bool IsSignedIn => string.IsNullOrEmpty(Token) == false;

        public static string Token => EditorPrefs.GetString(TokenKey, string.Empty);

        public static string Login => EditorPrefs.GetString(LoginKey, string.Empty);

        public static string AvatarUrl => EditorPrefs.GetString(AvatarUrlKey, string.Empty);

        /// <summary>
        /// 받은 토큰으로 계정 정보(<c>GET /user</c>)를 읽어 저장한다. 토큰이 통하지 않으면 저장하지 않고 오류 메시지를 돌려준다.
        /// </summary>
        public static async Task<string> SignInAsync(string token)
        {
            using UnityWebRequest request = UnityWebRequest.Get("https://api.github.com/user");
            request.SetRequestHeader("User-Agent", UserAgent);
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.SetRequestHeader("Authorization", "Bearer " + token);
            await SendAsync(request);
            if (request.result != UnityWebRequest.Result.Success)
            {
                return request.error;
            }
            User user = JsonUtility.FromJson<User>(request.downloadHandler.text);
            EditorPrefs.SetString(TokenKey, token);
            EditorPrefs.SetString(LoginKey, user?.login ?? string.Empty);
            EditorPrefs.SetString(AvatarUrlKey, user?.avatar_url ?? string.Empty);
            Changed?.Invoke();
            return null;
        }

        /// <summary>
        /// 이 PC 에서 토큰을 지운다. GitHub 쪽 승인까지 거두려면 GitHub 설정 > Applications 에서 앱을 취소해야 한다.
        /// </summary>
        public static void SignOut()
        {
            EditorPrefs.DeleteKey(TokenKey);
            EditorPrefs.DeleteKey(LoginKey);
            EditorPrefs.DeleteKey(AvatarUrlKey);
            Changed?.Invoke();
        }

        /// <summary>
        /// UnityWebRequest 의 완료 콜백은 메인 스레드에서 오므로, await 뒤도 메인 스레드다.
        /// </summary>
        public static Task SendAsync(UnityWebRequest request)
        {
            TaskCompletionSource<bool> completion = new();
            request.timeout = 15;
            request.SendWebRequest().completed += _ => completion.TrySetResult(true);
            return completion.Task;
        }

        [Serializable]
        private sealed class User
        {
            public string login;
            public string avatar_url;
        }
    }
}
