using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 에디터를 열면 바로 Git 연동을 켠다. Project Settings > Version Control > Mode 는 Unity 기본값인 "Visible Meta Files" 를 그대로 쓴다.
    ///
    /// 예전에는 Mode 드롭다운에 "Git" 항목(VersionControlObject)을 더했지만, 그러면 ProjectSettings/VersionControlSettings.asset 에
    /// "Git" 이 저장돼 이 패키지가 없는 팀원의 에디터가 모르는 모드를 읽게 된다. 이제 모드와 상관없이 켜고,
    /// 예전 버전이 "Git" 으로 바꿔 둔 프로젝트는 열 때 "Visible Meta Files" 로 되돌린다.
    /// 설정은 Project Settings > Git(<see cref="GitSettingsGUI"/>), Project 창 상태 배지는 <see cref="EditorApplication.projectWindowItemOnGUI"/> 로 그린다.
    /// </summary>
    [InitializeOnLoad]
    internal static class GitIntegration
    {
        /// <summary>
        /// 예전 버전이 Mode 드롭다운에 더했던 항목 이름.
        /// </summary>
        public const string LegacyModeName = "Git";

        public const string VisibleMetaFilesMode = "Visible Meta Files";

        public const string ProjectSettingsPath = "Project/Git";

        static GitIntegration()
        {
            EditorApplication.projectWindowItemOnGUI -= OnProjectWindowItemGUI;
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowItemGUI;
            GitRepository.Changed -= OnRepositoryChanged;
            GitRepository.Changed += OnRepositoryChanged;
            // 에디터를 처음 열 때는 설정·에셋이 다 읽히기 전이라 한 프레임 뒤에 시작한다.
            EditorApplication.delayCall += () =>
            {
                RestoreVisibleMetaFiles();
                GitRepository.Start();
            };
        }

        private static void RestoreVisibleMetaFiles()
        {
            if (VersionControlSettings.mode == LegacyModeName)
            {
                VersionControlSettings.mode = VisibleMetaFilesMode;
            }
        }

        [SettingsProvider]
        private static SettingsProvider CreateSettingsProvider()
        {
            return new SettingsProvider(ProjectSettingsPath, SettingsScope.Project)
            {
                label = "Git",
                guiHandler = _ => GitSettingsGUI.Draw(),
                keywords = new[] { "Git", "Version Control", "Diff", "Merge", "Fetch", "Toolbar" },
            };
        }

        /// <summary>
        /// Project 창 한 줄(또는 격자 한 칸). 목록이면 줄 맨 앞 16px, 격자면 위쪽 정사각형이 아이콘 자리다.
        /// </summary>
        private static void OnProjectWindowItemGUI(string guid, Rect rect)
        {
            if (GitVcsSettings.instance.ShowOverlays == false || Event.current.type != EventType.Repaint)
            {
                return;
            }
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }
            bool isGrid = rect.height > 20f;
            Rect iconRect = isGrid ? new Rect(rect.x, rect.y, rect.width, rect.width) : new Rect(rect.x, rect.y, 16f, 16f);
            GitIconOverlay.Draw(assetPath, iconRect);
        }

        private static void OnRepositoryChanged()
        {
            EditorApplication.RepaintProjectWindow();
            // 설정 창은 IMGUI 라 상태가 바뀌어도 스스로 다시 그리지 않는다.
            SettingsService.RepaintAllSettingsWindow();
        }
    }
}
