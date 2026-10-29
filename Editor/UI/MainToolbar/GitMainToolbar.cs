using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 에디터 메인 도구 막대의 Git 드롭다운 버튼(Unity Version Control 의 "Unity VCS ▾" 자리).
    /// 붙이는 방법은 Unity 버전마다 다르다: 6000.3 이상은 공개 MainToolbar API(<c>GitMainToolbar.Modern.cs</c>),
    /// 그 아래는 내부 도구 막대의 UI Toolkit 루트에 리플렉션으로 끼운다(<c>GitMainToolbar.Legacy.cs</c>). 이 파일은 둘이 같이 쓰는 부분이다.
    /// </summary>
    public static partial class GitMainToolbar
    {
        private static string s_lastText;
        private static string s_lastTooltip;

        /// <summary>
        /// 버튼을 보일지 바꾸고 도구 막대를 다시 그린다. 숨길 때는 다시 켜는 곳을 콘솔에 한 번 알려 준다.
        /// </summary>
        public static void SetShown(bool isShown)
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            if (settings.ShowInMainToolbar == isShown)
            {
                return;
            }
            settings.ShowInMainToolbar = isShown;
            if (isShown == false)
            {
                Debug.Log(GitText.Get("mainToolbar.hidden"));
            }
            Repaint(force: true);
        }

        public static void Execute(GitMainToolbarCommand command)
        {
            switch (command)
            {
                case GitMainToolbarCommand.OpenWindow:
                    GitWindow.Open();
                    break;
                case GitMainToolbarCommand.HideFromToolbar:
                    SetShown(false);
                    break;
                case GitMainToolbarCommand.OpenPreferences:
                    SettingsService.OpenUserPreferences(GitMainToolbarMenu.PreferencesPath);
                    break;
                case GitMainToolbarCommand.OpenHelp:
                    Application.OpenURL(GitMainToolbarMenu.HelpUrl);
                    break;
            }
        }

        private static void ShowMenu(Rect buttonRect)
        {
            GenericMenu menu = new();
            foreach (GitMainToolbarMenuItem item in GitMainToolbarMenu.Build(GitText.Get))
            {
                if (item.IsSeparator)
                {
                    menu.AddSeparator(string.Empty);
                    continue;
                }
                GitMainToolbarCommand command = item.Command;
                menu.AddItem(new GUIContent(item.Path), false, () => Execute(command));
            }
            menu.DropDown(buttonRect);
        }

        private static void SubscribeRepaint()
        {
            GitRepository.Changed -= OnRepositoryChanged;
            GitRepository.Changed += OnRepositoryChanged;
            GitText.LanguageChanged -= OnLanguageChanged;
            GitText.LanguageChanged += OnLanguageChanged;
        }

        private static void OnRepositoryChanged()
        {
            Repaint(force: false);
        }

        private static void OnLanguageChanged()
        {
            Repaint(force: true);
        }

        private static string CurrentBranchName => GitRepository.Branch?.Name;

        private static string CurrentText => GitMainToolbarMenu.ButtonText(CurrentBranchName);

        private static string CurrentTooltip => GitMainToolbarMenu.Tooltip(CurrentBranchName, GitText.Get);

        private static Texture2D Icon => GitIcons.Toolbar;

        /// <summary>
        /// 저장소는 상태가 바뀔 때마다(파일 저장 등) Changed 를 쏘므로, 글자가 그대로면 도구 막대를 건드리지 않는다.
        /// 설정·언어가 바뀌면 <paramref name="force"/> 로 무조건 다시 그린다.
        /// </summary>
        private static void Repaint(bool force)
        {
            string text = CurrentText;
            string tooltip = CurrentTooltip;
            if (force == false && text == s_lastText && tooltip == s_lastTooltip)
            {
                return;
            }
            s_lastText = text;
            s_lastTooltip = tooltip;
            ApplyToToolbar();
        }
    }
}
