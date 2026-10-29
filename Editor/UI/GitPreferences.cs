using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GitVersionControl
{
    /// <summary>
    /// Preferences > Git: 패키지의 설정은 모두 여기 있다. 언어·테마·배치·브랜치 탐색기 모양, git 실행 파일, 에디터 동작·표시, 자동 Fetch·.meta 짝,
    /// Diff(<see cref="GitSettingsGUI.DrawPersonal"/>), GitHub 계정 로그인(<see cref="GitHubAccount"/>). 이 PC 의 모든 프로젝트에 같이 적용된다.
    /// Project Settings 에는 Git 쪽이 없다 — 저장소에 올라가 팀이 나눠 쓸 설정이 없어서다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static class GitPreferences
    {
        public const string SettingsPath = "Preferences/Git";

        private const float AvatarSize = 40f;

        /// <summary>라벨 칸 폭. 기본값(약 150)이면 "도구 막대에 Git 버튼 표시" 같은 긴 라벨이 체크박스에 붙는다.</summary>
        private const float LabelWidth = 220f;

        /// <summary>묶음(일반·GitHub 계정) 사이 간격.</summary>
        private const float SectionGap = 16f;

        private static GUIStyle s_page;
        private static GUIStyle s_card;

        /// <summary>화면 가장자리에서 띄우는 여백(다른 Preferences 쪽과 같은 들여쓰기).</summary>
        private static GUIStyle Page => s_page ??= new GUIStyle { padding = new RectOffset(10, 12, 10, 12) };

        /// <summary>계정 칸: 테두리 상자 안에 아바타·이름·버튼을 넉넉한 여백으로 담는다.</summary>
        private static GUIStyle Card => s_card ??= new GUIStyle(EditorStyles.helpBox) { padding = new RectOffset(10, 10, 10, 10), margin = new RectOffset(0, 0, 4, 4) };

        private static readonly GitHubDeviceLogin s_login = GitHubDeviceLogin.Shared;
        private static Texture2D s_avatar;

        /// <summary>
        /// "코드 복사" 를 누른 시각. 잠깐 "복사됨" 을 보인다.
        /// </summary>
        private static double s_copiedAt = -1;
        private static string s_avatarUrl;

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            s_login.StateChanged -= SettingsService.RepaintAllSettingsWindow;
            s_login.StateChanged += SettingsService.RepaintAllSettingsWindow;
            return new SettingsProvider(SettingsPath, SettingsScope.User)
            {
                label = "Git",
                guiHandler = _ => Draw(),
                keywords = new[] { "Git", "GitHub", "Avatar", "Login", "Toolbar", "Explorer", "Fork", "Language", "Theme", "Layout", "Diff", "Menu", "Fetch", "Version Control" },
            };
        }

        public static void Open()
        {
            SettingsService.OpenUserPreferences(SettingsPath);
        }

        private static void Draw()
        {
            float labelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = LabelWidth;
            try
            {
                using (new EditorGUILayout.VerticalScope(Page))
                {
                    DrawGeneral();
                    GUILayout.Space(SectionGap);
                    GitSettingsGUI.DrawPersonalDiff();
                    GUILayout.Space(SectionGap);
                    DrawAccount();
                }
            }
            finally
            {
                EditorGUIUtility.labelWidth = labelWidth;
            }
        }

        private static void DrawGeneral()
        {
            EditorGUILayout.LabelField(GitText.Get("prefs.general"), EditorStyles.boldLabel);
            GUILayout.Space(2);
            GitSettingsGUI.DrawPersonal();
        }

        private static void DrawAccount()
        {
            EditorGUILayout.LabelField(GitText.Get("github.title"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(GitText.Get("github.description"), EditorStyles.wordWrappedMiniLabel);
            GUILayout.Space(6);

            if (GitHubAccount.IsConfigured == false)
            {
                EditorGUILayout.HelpBox(GitText.Get("github.notConfigured"), MessageType.Info);
                return;
            }
            if (GitHubAccount.IsSignedIn)
            {
                DrawSignedIn();
                return;
            }
            if (s_login.IsRunning)
            {
                DrawWaiting();
                return;
            }

            if (GUILayout.Button(GitText.Get("github.signIn"), GUILayout.Width(200), GUILayout.Height(26)))
            {
                s_login.Start();
            }
            if (s_login.Current == GitHubDeviceLogin.Step.Failed)
            {
                EditorGUILayout.HelpBox(GitText.Format("github.failed", s_login.Error), MessageType.Warning);
            }
        }

        private static void DrawSignedIn()
        {
            LoadAvatar(GitHubAccount.AvatarUrl);
            using (new EditorGUILayout.HorizontalScope(Card))
            {
                Rect avatarRect = GUILayoutUtility.GetRect(AvatarSize, AvatarSize, GUILayout.Width(AvatarSize), GUILayout.Height(AvatarSize));
                if (s_avatar != null)
                {
                    // GitHub 처럼 둥글게(모서리 반지름 = 반지름).
                    GUI.DrawTexture(avatarRect, s_avatar, ScaleMode.ScaleAndCrop, true, 0, Color.white, 0, AvatarSize * 0.5f);
                }
                GUILayout.Space(10);
                // 이름과 로그아웃 버튼을 아바타 높이 안에서 세로 가운데로.
                using (new EditorGUILayout.VerticalScope(GUILayout.Height(AvatarSize)))
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(GitText.Format("github.signedInAs", GitHubAccount.Login), EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                }
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.VerticalScope(GUILayout.Height(AvatarSize)))
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(GitText.Get("github.signOut"), GUILayout.Width(120), GUILayout.Height(24)))
                    {
                        GitHubAccount.SignOut();
                        s_login.Cancel();
                    }
                    GUILayout.FlexibleSpace();
                }
            }
            GUILayout.Space(6);
            EditorGUILayout.LabelField(GitText.Get("github.orgHint"), EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawWaiting()
        {
            if (s_login.Current == GitHubDeviceLogin.Step.RequestingCode)
            {
                EditorGUILayout.LabelField(GitText.Get("github.requesting"));
            }
            else
            {
                EditorGUILayout.LabelField(GitText.Get(s_login.HasOpenedPage ? "github.enterCode" : "github.codeReady"), EditorStyles.wordWrappedLabel);
                GUIStyle codeStyle = new(EditorStyles.boldLabel) { fontSize = 24 };
                EditorGUILayout.SelectableLabel(s_login.UserCode, codeStyle, GUILayout.Height(34));
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool isCopied = EditorApplication.timeSinceStartup - s_copiedAt < 1.5;
                    if (GUILayout.Button(GitText.Get(isCopied ? "github.copied" : "github.copyCode"), GUILayout.Width(120)))
                    {
                        s_login.CopyCode();
                        s_copiedAt = EditorApplication.timeSinceStartup;
                        // "복사됨" 을 원래 글자로 되돌리려면 1.5초 뒤 다시 그려야 한다.
                        RepaintAfterCopied();
                    }
                    if (GUILayout.Button(GitText.Get(s_login.HasOpenedPage ? "github.openPage" : "github.next"), GUILayout.Width(200)))
                    {
                        s_login.OpenVerificationPage();
                    }
                }
            }
            if (GUILayout.Button(GitText.Get("common.cancel"), GUILayout.Width(120)))
            {
                s_login.Cancel();
            }
        }

        private static async void RepaintAfterCopied()
        {
            await System.Threading.Tasks.Task.Delay(1600);
            SettingsService.RepaintAllSettingsWindow();
        }

        /// <summary>
        /// 계정 사진은 Preferences 를 열 때 한 번 받는다(계정이 바뀌면 다시).
        /// </summary>
        private static async void LoadAvatar(string url)
        {
            if (string.IsNullOrEmpty(url) || url == s_avatarUrl)
            {
                return;
            }
            s_avatarUrl = url;
            s_avatar = null;
            using UnityWebRequest request = UnityWebRequest.Get(url);
            await GitHubAccount.SendAsync(request);
            if (request.result != UnityWebRequest.Result.Success || s_avatarUrl != url)
            {
                return;
            }
            Texture2D texture = new(2, 2) { hideFlags = HideFlags.HideAndDontSave };
            if (texture.LoadImage(request.downloadHandler.data))
            {
                s_avatar = texture;
                SettingsService.RepaintAllSettingsWindow();
            }
        }
    }
}
