using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// Preferences > Git 의 설정 칸(<see cref="GitPreferences"/>). Version Control 설정 화면에 붙던 시절의 IMGUI 를 그대로 쓴다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static class GitSettingsGUI
    {
        private static string s_versionText;
        private static bool s_isBusy;

        /// <summary>
        /// Preferences > Git 의 개인 설정: 화면(언어·테마·배치), git 실행 파일, 에디터 동작, 표시 방식, 자동 Fetch·rebase·.meta 짝 다루기. 값은 EditorPrefs 에 있어 모든 프로젝트에 같이 적용된다.
        /// </summary>
        public static void DrawPersonal()
        {
            GitVcsSettings settings = GitVcsSettings.instance;

            DrawLanguage();
            DrawTheme();
            DrawLayout();

            using (new EditorGUILayout.HorizontalScope())
            {
                string gitPath = EditorGUILayout.DelayedTextField(new GUIContent(GitText.Get("settings.gitExecutable"), GitText.Get("settings.gitExecutableTooltip")), settings.GitExecutable);
                if (gitPath != settings.GitExecutable)
                {
                    settings.GitExecutable = gitPath;
                    s_versionText = null;
                    GitLocator.ClearCache();
                    GitRepository.RequestRefresh(immediate: true);
                }
                if (GUILayout.Button(GitText.Get("settings.browse"), GUILayout.Width(60)))
                {
                    string picked = EditorUtility.OpenFilePanel(GitText.Get("settings.browseTitle"), string.Empty, Application.platform == RuntimePlatform.WindowsEditor ? "exe" : string.Empty);
                    if (string.IsNullOrEmpty(picked) == false)
                    {
                        settings.GitExecutable = picked;
                        s_versionText = null;
                        GitLocator.ClearCache();
                        GitRepository.RequestRefresh(immediate: true);
                    }
                }
                using (new EditorGUI.DisabledScope(s_isBusy))
                {
                    if (GUILayout.Button(GitText.Get("settings.check"), GUILayout.Width(60)))
                    {
                        // git 을 새로 설치했을 수 있으니 다시 찾는다.
                        GitLocator.ClearCache();
                        CheckVersionAsync();
                    }
                }
            }
            // macOS 는 로그인 셸 PATH 를 읽느라 처음 찾기가 수 초 걸릴 수 있어 백그라운드에서 찾고 끝나면 다시 그린다.
            if (GitLocator.TryGetCached(settings.GitExecutable, out GitLocator.Location location, SettingsService.RepaintAllSettingsWindow) == false)
            {
                EditorGUILayout.LabelField(" ", GitText.Get("settings.searching"), EditorStyles.miniLabel);
            }
            else if (location.Found)
            {
                EditorGUILayout.LabelField(" ", GitText.Format("settings.using", location.Path, location.Source), EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox(location.Hint, MessageType.Warning);
            }
            if (string.IsNullOrEmpty(s_versionText) == false)
            {
                EditorGUILayout.LabelField(" ", s_versionText, EditorStyles.miniLabel);
            }

            bool overlays = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.overlays")), settings.ShowOverlays);
            if (overlays != settings.ShowOverlays)
            {
                settings.ShowOverlays = overlays;
                EditorApplication.RepaintProjectWindow();
            }
            settings.RefreshOnFocus = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.refreshOnFocus"), GitText.Get("settings.refreshOnFocusTooltip")), settings.RefreshOnFocus);
            bool watch = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.watchFileSystem"), GitText.Get("settings.watchFileSystemTooltip")), settings.WatchFileSystem);
            if (watch != settings.WatchFileSystem)
            {
                settings.WatchFileSystem = watch;
                GitRepository.UpdateFileWatcher();
            }
            settings.ShowAvatars = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.avatars"), GitText.Get("settings.avatarsTooltip")), settings.ShowAvatars);
            GitMainToolbar.SetShown(EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.mainToolbar"), GitText.Get("settings.mainToolbarTooltip")), settings.ShowInMainToolbar));
            settings.MenuStyle = EnumPopup(GitText.Get("settings.menuStyle"), GitText.Get("settings.menuStyleTooltip"), settings.MenuStyle, "menuStyle.");
            settings.ErrorDetailMode = EnumPopup(GitText.Get("settings.errorDetail"), GitText.Get("settings.errorDetailTooltip"), settings.ErrorDetailMode, "errorDetail.");
            settings.AutoFetch = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.autoFetch"), GitText.Get("settings.autoFetchTooltip")), settings.AutoFetch);
            using (new EditorGUI.DisabledScope(settings.AutoFetch == false))
            using (new EditorGUI.IndentLevelScope())
            {
                settings.AutoFetchMinutes = EditorGUILayout.DelayedIntField(
                    new GUIContent(GitText.Get("settings.autoFetchMinutes"), GitText.Format("settings.autoFetchMinutesTooltip", GitAutoFetch.MinIntervalMinutes, GitAutoFetch.MaxIntervalMinutes)),
                    settings.AutoFetchMinutes);
            }
            settings.StopOnRebaseConflict = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.stopOnRebaseConflict"), GitText.Get("settings.stopOnRebaseConflictTooltip")), settings.StopOnRebaseConflict);
            settings.GroupMetaPairs = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.groupMetaPairs"), GitText.Get("settings.groupMetaPairsTooltip")), settings.GroupMetaPairs);
            using (new EditorGUI.DisabledScope(settings.GroupMetaPairs))
            {
                // 묶어 보기를 켜면 짝은 늘 함께 옮기므로 켜진 채로 잠근다.
                bool movesPairs = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.stageMetaPairs"), GitText.Get("settings.stageMetaPairsTooltip")), settings.MovesMetaPairs);
                if (settings.GroupMetaPairs == false)
                {
                    settings.StageMetaPairs = movesPairs;
                }
            }
        }

        private static string s_sslTestText;
        private static MessageType s_sslTestType;
        private static bool s_sslBusy;
        private static bool s_sslConfirmNoVerify;

        /// <summary>
        /// Preferences > Git 의 SSL 묶음. 회사망처럼 SSL 을 가로채는 곳에서 Push·Pull·Fetch 가 인증서 오류로 실패할 때 쓴다(<see cref="GitSsl"/>).
        /// 이 패키지가 부르는 git 에만 붙고 .gitconfig 는 바꾸지 않는다. Diff 묶음처럼 굵은 제목 아래 들여 쓰고, 안내·결과는 입력 칸 열에 맞춘다.
        /// </summary>
        public static void DrawPersonalSsl()
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            bool windows = Application.platform == RuntimePlatform.WindowsEditor;
            bool mac = Application.platform == RuntimePlatform.OSXEditor;

            EditorGUILayout.LabelField(new GUIContent(GitText.Get("settings.sslHeader"), GitText.Get("settings.sslDescription")), EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUI.IndentLevelScope())
            {
                using (new EditorGUI.DisabledScope(settings.SslVerify == false))
                {
                    if (windows)
                    {
                        settings.SslUseSystemStore = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.sslSystemStore"), GitText.Get("settings.sslSystemStoreTooltip")), settings.SslUseSystemStore);
                        using (new EditorGUI.DisabledScope(settings.SslUseSystemStore == false))
                        using (new EditorGUI.IndentLevelScope())
                        {
                            settings.SslSkipRevocationCheck = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.sslSkipRevocation"), GitText.Get("settings.sslSkipRevocationTooltip")), settings.SslSkipRevocationCheck);
                        }
                    }
                    // schannel 은 운영체제 저장소만 보므로 CA 파일을 쓰지 않는다.
                    using (new EditorGUI.DisabledScope(windows && settings.SslUseSystemStore))
                    {
                        DrawCaFile(settings, mac);
                    }
                }
                DrawNoVerify(settings);
                DrawConnectionTest();
            }
            if (EditorGUI.EndChangeCheck())
            {
                GitSsl.Refresh();
            }
        }

        /// <summary>회사 루트 인증서 파일 한 줄: 경로 · [찾기] · (macOS) [키체인].</summary>
        private static void DrawCaFile(GitVcsSettings settings, bool mac)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                settings.SslCaFile = EditorGUILayout.DelayedTextField(new GUIContent(GitText.Get("settings.sslCaFile"), GitText.Get("settings.sslCaFileTooltip")), settings.SslCaFile);
                if (GUILayout.Button(GitText.Get("settings.browse"), GUILayout.Width(60)))
                {
                    string picked = EditorUtility.OpenFilePanelWithFilters(GitText.Get("settings.sslCaFile"), string.Empty, new[] { GitText.Get("settings.sslCaFileFilter"), "pem,crt,cer", "All", "*" });
                    if (string.IsNullOrEmpty(picked) == false)
                    {
                        settings.SslCaFile = picked;
                        GUI.changed = true;
                    }
                }
                if (mac)
                {
                    using (new EditorGUI.DisabledScope(s_sslBusy))
                    {
                        if (GUILayout.Button(new GUIContent(GitText.Get("settings.sslExportKeychain"), GitText.Get("settings.sslExportKeychainTooltip")), GUILayout.Width(60)))
                        {
                            ExportKeychainAsync();
                        }
                    }
                }
            }
            if (string.IsNullOrEmpty(settings.SslCaFile) == false && System.IO.File.Exists(settings.SslCaFile) == false)
            {
                Note(GitText.Format("settings.sslCaFileMissing", settings.SslCaFile), MessageType.Warning);
            }
        }

        /// <summary>인증서 확인 끄기. 끄는 쪽만 이 자리에서 한 번 묻는다 — 중간자 공격을 막지 못하게 된다(OS 대화상자는 쓰지 않는다).</summary>
        private static void DrawNoVerify(GitVcsSettings settings)
        {
            bool noVerify = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.sslNoVerify"), GitText.Get("settings.sslNoVerifyTooltip")), settings.SslVerify == false || s_sslConfirmNoVerify);
            if (noVerify == false)
            {
                settings.SslVerify = true;
                s_sslConfirmNoVerify = false;
                return;
            }
            if (settings.SslVerify == false)
            {
                Note(GitText.Get("settings.sslNoVerifyWarning"), MessageType.Warning);
                return;
            }
            s_sslConfirmNoVerify = true;
            using (new EditorGUILayout.HorizontalScope())
            using (new IndentReset())
            {
                GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                using (new EditorGUILayout.VerticalScope())
                {
                    EditorGUILayout.HelpBox(GitText.Get("settings.sslNoVerifyConfirm"), MessageType.Warning);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.FlexibleSpace();
                        if (GUILayout.Button(GitText.Get("common.cancel"), GUILayout.Width(80)))
                        {
                            s_sslConfirmNoVerify = false;
                        }
                        if (GUILayout.Button(GitText.Get("settings.sslNoVerifyOk"), GUILayout.Width(100)))
                        {
                            settings.SslVerify = false;
                            s_sslConfirmNoVerify = false;
                        }
                    }
                }
            }
        }

        /// <summary>"연결 테스트 [확인]" 한 줄과, 그 아래 입력 칸 열에 결과.</summary>
        private static void DrawConnectionTest()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent(GitText.Get("settings.sslTest"), GitText.Get("settings.sslTestTooltip")));
                using (new EditorGUI.DisabledScope(s_sslBusy))
                {
                    if (GUILayout.Button(GitText.Get("settings.check"), GUILayout.Width(60)))
                    {
                        TestConnectionAsync();
                    }
                }
                GUILayout.FlexibleSpace();
            }
            if (string.IsNullOrEmpty(s_sslTestText) == false)
            {
                Note(s_sslTestText, s_sslTestType);
            }
        }

        /// <summary>
        /// 입력 칸 열에서 시작하는 짧은 안내. 성공·진행은 작은 글자 한 줄(git 경로 아래 "사용 중:" 과 같은 모양), 경고·오류는 그 열 폭의 HelpBox.
        /// </summary>
        private static void Note(string text, MessageType type)
        {
            if (type == MessageType.Warning || type == MessageType.Error)
            {
                using (new EditorGUILayout.HorizontalScope())
                using (new IndentReset())
                {
                    GUILayout.Space(EditorGUIUtility.labelWidth + 2);
                    EditorGUILayout.HelpBox(text, type);
                }
                return;
            }
            EditorGUILayout.LabelField(" ", text, EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>
        /// 들여쓰기를 잠깐 0 으로. HelpBox 는 들여쓰기를 한 번 더 먹어서, 입력 칸 열(라벨 폭 뒤)에 맞춘 자리에서 그만큼 오른쪽으로 밀린다.
        /// </summary>
        private sealed class IndentReset : System.IDisposable
        {
            private readonly int _saved;

            public IndentReset()
            {
                _saved = EditorGUI.indentLevel;
                EditorGUI.indentLevel = 0;
            }

            public void Dispose() => EditorGUI.indentLevel = _saved;
        }

        private static async void ExportKeychainAsync()
        {
            s_sslBusy = true;
            (string path, string error) = await System.Threading.Tasks.Task.Run(GitSsl.ExportMacKeychainCertificates);
            s_sslBusy = false;
            if (path != null)
            {
                GitVcsSettings.instance.SslCaFile = path;
                GitSsl.Refresh();
                s_sslTestText = GitText.Format("settings.sslExported", path);
                s_sslTestType = MessageType.Info;
            }
            else
            {
                s_sslTestText = GitText.Format("settings.sslExportFailed", error);
                s_sslTestType = MessageType.Warning;
            }
            SettingsService.RepaintAllSettingsWindow();
        }

        /// <summary>
        /// 지금 설정으로 원격(origin, 없으면 첫 원격)에 붙어 본다(<c>git ls-remote</c>). 받기만 하고 아무것도 바꾸지 않는다.
        /// </summary>
        private static async void TestConnectionAsync()
        {
            s_sslBusy = true;
            s_sslTestText = GitText.Get("settings.sslTesting");
            s_sslTestType = MessageType.None;
            GitSsl.Refresh();
            try
            {
                GitResult remotes = await GitRepository.RunAsync("remote");
                string[] names = remotes.Success ? remotes.Output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray() : System.Array.Empty<string>();
                if (names.Length == 0)
                {
                    s_sslTestText = GitText.Get("settings.sslNoRemote");
                    s_sslTestType = MessageType.Warning;
                    return;
                }
                string remote = names.Contains("origin") ? "origin" : names[0];
                GitResult result = await GitRepository.RunAsync("ls-remote", remote, "HEAD");
                if (result.Success)
                {
                    s_sslTestText = GitText.Format("settings.sslTestOk", remote);
                    s_sslTestType = MessageType.Info;
                    return;
                }
                GitErrorAdvice advice = GitErrorAdvice.Explain(result.Message);
                string firstLine = result.Message.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? string.Empty;
                s_sslTestText = advice.Cause + "\n" + advice.Fix + "\n\n" + firstLine;
                s_sslTestType = advice.Kind == GitErrorKind.Ssl ? MessageType.Error : MessageType.Warning;
            }
            finally
            {
                s_sslBusy = false;
                SettingsService.RepaintAllSettingsWindow();
            }
        }

        /// <summary>Preferences > Git 의 Diff 묶음.</summary>
        public static void DrawPersonalDiff() => DrawDiff(GitVcsSettings.instance);

        /// <summary>
        /// 내장 diff 뷰어의 git diff 옵션.
        /// </summary>
        private static void DrawDiff(GitVcsSettings settings)
        {
            EditorGUILayout.LabelField(GitText.Get("settings.diffHeader"), EditorStyles.boldLabel);
            using (new EditorGUI.IndentLevelScope())
            {
                settings.DiffAlgorithm = EnumPopup(GitText.Get("settings.diffAlgorithm"), GitText.Get("settings.diffAlgorithmTooltip"), settings.DiffAlgorithm, null);
                settings.DiffWhitespace = EnumPopup(GitText.Get("settings.diffWhitespace"), null, settings.DiffWhitespace, "diffWhitespace.");
                settings.DiffContextLines = EditorGUILayout.DelayedIntField(new GUIContent(GitText.Get("settings.diffContext"), GitText.Get("settings.diffContextTooltip")), settings.DiffContextLines);
                settings.DiffLayout = EnumPopup(GitText.Get("settings.diffLayout"), null, settings.DiffLayout, "diffLayout.");
            }
        }

        private static T EnumPopup<T>(string label, string tooltip, T current, string textKeyPrefix) where T : System.Enum
        {
            T[] values = (T[])System.Enum.GetValues(typeof(T));
            GUIContent[] names = values.Select(value => new GUIContent(textKeyPrefix == null ? value.ToString().ToLowerInvariant() : GitText.Get(textKeyPrefix + value))).ToArray();
            int index = System.Array.IndexOf(values, current);
            int picked = EditorGUILayout.Popup(new GUIContent(label, tooltip), index, names);
            return picked >= 0 ? values[picked] : current;
        }

        /// <summary>
        /// 창 밝기(다크·라이트·시스템).
        /// </summary>
        private static void DrawTheme()
        {
            GitThemeMode[] modes = GitTheme.MenuOrder;
            string[] names = modes.Select(GitTheme.DisplayName).ToArray();
            int current = System.Array.IndexOf(modes, GitTheme.Mode);
            int picked = EditorGUILayout.Popup(new GUIContent(GitText.Get("window.theme"), GitText.Get("settings.themeTooltip")), current, names.Select(n => new GUIContent(n)).ToArray());
            if (picked >= 0 && picked != current)
            {
                GitTheme.Mode = modes[picked];
            }
        }

        /// <summary>
        /// 변경 탭 배치. Fork·GitKraken 은 git 인덱스(Stage·Unstage)로 커밋한다.
        /// </summary>
        private static void DrawLayout()
        {
            GitLayoutStyle[] styles = (GitLayoutStyle[])System.Enum.GetValues(typeof(GitLayoutStyle));
            GUIContent[] names = styles.Select(style => new GUIContent(GitLayout.DisplayName(style))).ToArray();
            int current = System.Array.IndexOf(styles, GitLayout.Style);
            int picked = EditorGUILayout.Popup(new GUIContent(GitText.Get("window.layout"), GitText.Get("settings.layoutTooltip")), current, names);
            if (picked >= 0 && picked != current)
            {
                GitLayout.Style = styles[picked];
            }
            // 배치에 따라 탐색기 모양이 정해지거나 고를 수 있어 바로 아래에 둔다(값은 개인 설정).
            GitExplorerStyle.DrawPopup();
        }

        /// <summary>언어 선택. 이름은 각 언어 자기 말로 보여서 어떤 언어로 보고 있든 고를 수 있게 한다.</summary>
        private static void DrawLanguage()
        {
            GitLanguage[] languages = GitText.AllLanguages.ToArray();
            string[] names = languages.Select(GitText.NativeName).ToArray();
            int current = System.Array.IndexOf(languages, GitText.Language);
            int picked = EditorGUILayout.Popup(GitText.Get("common.language"), current, names);
            if (picked != current && picked >= 0)
            {
                GitText.SetLanguage(languages[picked]);
            }
        }

        private static async void CheckVersionAsync()
        {
            s_isBusy = true;
            GitResult result = await GitCommand.RunAsync(GitRepository.ProjectRoot, new[] { "--version" });
            s_versionText = result.Success ? result.Output.Trim() : result.Message;
            s_isBusy = false;
            SettingsService.RepaintAllSettingsWindow();
        }
    }
}
