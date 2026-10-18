using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// Project Settings > Version Control 에서 모드가 "Git" 일 때 모드 드롭다운 아래에 그려지는 UI.
    /// Unity 의 ISettingsInspectorExtension 이 IMGUI 만 받으므로 이 화면만 IMGUI 로 그린다.
    /// </summary>
    internal static class GitSettingsGUI
    {
        private static string s_versionText;
        private static bool s_isBusy;

        public static void Draw()
        {
            GitVcsSettings settings = GitVcsSettings.instance;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Git", EditorStyles.boldLabel);

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

            settings.ShowOverlays = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.overlays")), settings.ShowOverlays);
            settings.RefreshOnFocus = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.refreshOnFocus"), GitText.Get("settings.refreshOnFocusTooltip")), settings.RefreshOnFocus);
            settings.ShowAvatars = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.avatars"), GitText.Get("settings.avatarsTooltip")), settings.ShowAvatars);
            using (new EditorGUI.DisabledScope(settings.ShowAvatars == false))
            using (new EditorGUI.IndentLevelScope())
            {
                settings.AvatarsUseGitCredentials = EditorGUILayout.Toggle(new GUIContent(GitText.Get("settings.avatarsCredentials"), GitText.Get("settings.avatarsCredentialsTooltip")), settings.AvatarsUseGitCredentials);
                settings.MyGitHubLogin = EditorGUILayout.DelayedTextField(new GUIContent(GitText.Get("settings.myGitHubLogin"), GitText.Get("settings.myGitHubLoginTooltip")), settings.MyGitHubLogin);
            }

            EditorGUILayout.Space();
            DrawDiff(settings);

            EditorGUILayout.Space();
            DrawRepositoryStatus();
        }

        /// <summary>
        /// Diff 엔진과 엔진별 옵션. 내장 뷰어는 git diff 옵션을, 외부 도구는 프로그램 경로·인자를 보인다.
        /// </summary>
        private static void DrawDiff(GitVcsSettings settings)
        {
            EditorGUILayout.LabelField(GitText.Get("settings.diffHeader"), EditorStyles.boldLabel);

            DiffEngine engine = EnumPopup(GitText.Get("settings.diffEngine"), GitText.Get("settings.diffEngineTooltip"), settings.DiffEngine, "diffEngine.");
            settings.DiffEngine = engine;

            using (new EditorGUI.IndentLevelScope())
            {
                switch (engine)
                {
                    case DiffEngine.BuiltIn:
                        settings.DiffAlgorithm = EnumPopup(GitText.Get("settings.diffAlgorithm"), GitText.Get("settings.diffAlgorithmTooltip"), settings.DiffAlgorithm, null);
                        settings.DiffWhitespace = EnumPopup(GitText.Get("settings.diffWhitespace"), null, settings.DiffWhitespace, "diffWhitespace.");
                        settings.DiffContextLines = EditorGUILayout.DelayedIntField(new GUIContent(GitText.Get("settings.diffContext"), GitText.Get("settings.diffContextTooltip")), settings.DiffContextLines);
                        settings.DiffLayout = EnumPopup(GitText.Get("settings.diffLayout"), null, settings.DiffLayout, "diffLayout.");
                        break;
                    case DiffEngine.GitDifftool:
                        settings.DifftoolName = EditorGUILayout.DelayedTextField(new GUIContent(GitText.Get("settings.difftoolName"), GitText.Get("settings.difftoolNameTooltip")), settings.DifftoolName);
                        DrawDifftoolConfig();
                        break;
                    default:
                        DrawExternalTool(settings, engine);
                        break;
                }
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

        private static string s_difftoolConfig;
        private static bool s_isReadingDifftoolConfig;

        /// <summary>
        /// git config 의 diff.tool 을 보여 준다. IMGUI 는 매 프레임 그리므로 한 번만 읽어 둔다.
        /// </summary>
        private static void DrawDifftoolConfig()
        {
            if (s_difftoolConfig == null)
            {
                ReadDifftoolConfigAsync();
                return;
            }
            if (s_difftoolConfig.Length > 0)
            {
                EditorGUILayout.LabelField(" ", GitText.Format("settings.difftoolConfigured", s_difftoolConfig), EditorStyles.miniLabel);
            }
            else if (string.IsNullOrEmpty(GitVcsSettings.instance.DifftoolName))
            {
                EditorGUILayout.HelpBox(GitText.Get("settings.difftoolNotConfigured"), MessageType.Warning);
            }
        }

        private static async void ReadDifftoolConfigAsync()
        {
            if (s_isReadingDifftoolConfig)
            {
                return;
            }
            s_isReadingDifftoolConfig = true;
            GitResult result = await GitRepository.RunAsync("config", "--get", "diff.tool");
            s_difftoolConfig = result.Success ? result.Output.Trim() : string.Empty;
            s_isReadingDifftoolConfig = false;
            SettingsService.RepaintAllSettingsWindow();
        }

        private static void DrawExternalTool(GitVcsSettings settings, DiffEngine engine)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string path = EditorGUILayout.DelayedTextField(new GUIContent(GitText.Get("settings.diffToolPath"), GitText.Get("settings.diffToolPathTooltip")), settings.GetDiffToolPath(engine));
                settings.SetDiffToolPath(engine, path);
                if (GUILayout.Button(GitText.Get("settings.browse"), GUILayout.Width(60)))
                {
                    string picked = EditorUtility.OpenFilePanel(GitText.Get("settings.browseTitle"), string.Empty, Application.platform == RuntimePlatform.WindowsEditor ? "exe" : string.Empty);
                    if (string.IsNullOrEmpty(picked) == false)
                    {
                        settings.SetDiffToolPath(engine, picked);
                    }
                }
            }

            string executable = settings.GetDiffToolPath(engine);
            if (engine == DiffEngine.Custom)
            {
                settings.CustomDiffArguments = EditorGUILayout.DelayedTextField(new GUIContent(GitText.Get("settings.diffArguments"), GitText.Get("settings.diffArgumentsTooltip")), settings.CustomDiffArguments);
            }
            else if (string.IsNullOrEmpty(executable))
            {
                executable = FindToolCached(engine);
                if (executable != null)
                {
                    EditorGUILayout.LabelField(" ", GitText.Format("settings.diffToolFound", executable), EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox(GitText.Get("settings.diffToolMissing"), MessageType.Warning);
                }
            }

            string template = engine == DiffEngine.Custom ? settings.CustomDiffArguments : ExternalDiffTool.ArgumentTemplate(engine, Application.platform);
            EditorGUILayout.LabelField(" ", GitText.Format("settings.diffPreview", System.IO.Path.GetFileName(executable ?? "?"), template), EditorStyles.miniLabel);
        }

        private static readonly System.Collections.Generic.Dictionary<DiffEngine, string> s_foundTools = new();

        /// <summary>
        /// 설치 위치 찾기는 디스크를 훑으므로 엔진마다 한 번만 한다(IMGUI 는 매 프레임 그린다).
        /// </summary>
        private static string FindToolCached(DiffEngine engine)
        {
            if (s_foundTools.TryGetValue(engine, out string path) == false)
            {
                path = ExternalDiffTool.FindExecutable(engine);
                s_foundTools[engine] = path;
            }
            return path;
        }

        /// <summary>
        /// 창 모양. 어둡게·밝게는 에디터 스킨을 따르므로 스타일만 고른다.
        /// </summary>
        private static void DrawTheme()
        {
            GitThemeStyle[] styles = (GitThemeStyle[])System.Enum.GetValues(typeof(GitThemeStyle));
            string[] names = styles.Select(GitTheme.DisplayName).ToArray();
            int current = System.Array.IndexOf(styles, GitTheme.Style);
            int picked = EditorGUILayout.Popup(new GUIContent(GitText.Get("window.theme"), GitText.Get("settings.themeTooltip")), current, names.Select(n => new GUIContent(n)).ToArray());
            if (picked >= 0 && picked != current)
            {
                GitTheme.Style = styles[picked];
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

        private static void DrawRepositoryStatus()
        {
            EditorGUILayout.LabelField(GitText.Get("settings.repository"), EditorStyles.boldLabel);

            if (string.IsNullOrEmpty(GitRepository.LastError) == false)
            {
                EditorGUILayout.HelpBox(GitRepository.LastError, MessageType.Error);
            }

            if (GitRepository.IsRepository == false)
            {
                if (GitRepository.IsRefreshing)
                {
                    EditorGUILayout.LabelField(GitText.Get("settings.checking"));
                    return;
                }
                EditorGUILayout.HelpBox(GitText.Get("settings.notRepository"), MessageType.Info);
                using (new EditorGUI.DisabledScope(s_isBusy))
                {
                    if (GUILayout.Button("git init"))
                    {
                        InitAsync();
                    }
                }
                return;
            }

            GitBranchInfo branch = GitRepository.Branch;
            EditorGUILayout.LabelField(GitText.Get("settings.root"), GitRepository.RepoRoot);
            if (string.IsNullOrEmpty(GitRepository.ProjectPrefix) == false)
            {
                EditorGUILayout.LabelField(GitText.Get("settings.projectLocation"), GitRepository.ProjectPrefix);
            }
            if (branch != null)
            {
                EditorGUILayout.LabelField(GitText.Get("settings.branch"), GitDisplay.FormatBranch(branch));
            }
            EditorGUILayout.LabelField(GitText.Get("settings.changes"), GitText.Format("settings.changesCount", GitRepository.Entries.Count));

            string author = string.IsNullOrEmpty(GitRepository.UserName) ? GitText.Get("settings.unset") : $"{GitRepository.UserName} <{GitRepository.UserEmail}>";
            EditorGUILayout.LabelField(GitText.Get("settings.author"), author);
            if (string.IsNullOrEmpty(GitRepository.UserName) || string.IsNullOrEmpty(GitRepository.UserEmail))
            {
                EditorGUILayout.HelpBox(GitText.Get("settings.noAuthor"), MessageType.Warning);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(GitText.Get("common.openGitWindow")))
                {
                    GitWindow.Open();
                }
                using (new EditorGUI.DisabledScope(GitRepository.IsRefreshing))
                {
                    if (GUILayout.Button(GitText.Get("common.refresh"), GUILayout.Width(90)))
                    {
                        GitRepository.RequestRefresh(immediate: true);
                    }
                }
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

        private static async void InitAsync()
        {
            bool hasGitignore = File.Exists(Path.Combine(GitRepository.ProjectRoot, ".gitignore"));
            string message = GitText.Format("settings.initMessage", GitRepository.ProjectRoot)
                             + (hasGitignore ? string.Empty : GitText.Get("settings.initNoGitignore"));
            if (EditorUtility.DisplayDialog("git init", message, GitText.Get("common.create"), GitText.Get("common.cancel")) == false)
            {
                return;
            }

            s_isBusy = true;
            GitResult result = await GitCommand.RunAsync(GitRepository.ProjectRoot, new[] { "init" });
            s_isBusy = false;
            if (result.Success == false)
            {
                EditorUtility.DisplayDialog(GitText.Get("settings.initFailed"), result.Message, GitText.Get("common.ok"));
            }
            GitRepository.RequestRefresh(immediate: true);
        }
    }
}
