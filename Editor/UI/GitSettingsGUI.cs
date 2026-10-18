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

            EditorGUILayout.Space();
            DrawRepositoryStatus();
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
