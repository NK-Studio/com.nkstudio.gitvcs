using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace GitVersionControl
{
    /// <summary>
    /// Preferences > Git: 사람마다 다른 개인 설정. 지금은 GitHub 계정 로그인(<see cref="GitHubAccount"/>)만 있다.
    /// 프로젝트 설정(Project Settings > Git)과 달리 이 PC 의 모든 프로젝트에 같이 적용된다.
    /// </summary>
    internal static class GitPreferences
    {
        private const float AvatarSize = 40f;

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
            return new SettingsProvider("Preferences/Git", SettingsScope.User)
            {
                label = "Git",
                guiHandler = _ => Draw(),
                keywords = new[] { "Git", "GitHub", "Avatar", "Login", "Toolbar" },
            };
        }

        private static void Draw()
        {
            EditorGUILayout.Space();
            GitVcsSettings settings = GitVcsSettings.instance;
            GitMainToolbar.SetShown(EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.mainToolbar"), GitText.Get("settings.mainToolbarTooltip")), settings.ShowInMainToolbar));
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(GitText.Get("github.title"), EditorStyles.boldLabel);
            EditorGUILayout.LabelField(GitText.Get("github.description"), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space();

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
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect avatarRect = GUILayoutUtility.GetRect(AvatarSize, AvatarSize, GUILayout.Width(AvatarSize), GUILayout.Height(AvatarSize));
                if (s_avatar != null)
                {
                    GUI.DrawTexture(avatarRect, s_avatar, ScaleMode.ScaleToFit);
                }
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.LabelField(GitText.Format("github.signedInAs", GitHubAccount.Login), EditorStyles.boldLabel);
                    if (GUILayout.Button(GitText.Get("github.signOut"), GUILayout.Width(120)))
                    {
                        GitHubAccount.SignOut();
                        s_login.Cancel();
                    }
                }
            }
            EditorGUILayout.Space();
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
