using System;
using System.Collections.Generic;

namespace GitVersionControl
{
    public enum GitMainToolbarCommand
    {
        OpenWindow,
        HideFromToolbar,
        OpenPreferences,
        OpenHelp,
    }

    /// <summary>
    /// 도구 막대 메뉴의 한 줄. <see cref="Path"/> 가 null 이면 구분선이다(경로의 '/' 는 하위 메뉴).
    /// </summary>
    public readonly struct GitMainToolbarMenuItem
    {
        public readonly string Path;
        public readonly GitMainToolbarCommand Command;

        public GitMainToolbarMenuItem(string path, GitMainToolbarCommand command)
        {
            Path = path;
            Command = command;
        }

        public bool IsSeparator => Path == null;

        public static GitMainToolbarMenuItem Separator => new(null, default);
    }

    /// <summary>
    /// 에디터 메인 도구 막대 Git 버튼의 순수 규칙: 버튼 글자·툴팁, 메뉴 구성.
    /// Unity Version Control 의 "Unity VCS ▾" 메뉴와 같은 순서(창 열기 | 숨기기 | 설정 | 더 알아보기)를 따른다.
    /// </summary>
    public static class GitMainToolbarMenu
    {
        public const string ButtonLabel = "Git";
        public const string HelpUrl = "https://github.com/NK-Studio/com.nkstudio.gitvcs";
        public const string PreferencesPath = GitPreferences.SettingsPath;

        /// <summary>
        /// 버튼에 보일 브랜치 이름의 최대 글자 수. 도구 막대 왼쪽 칸에 Account·Cloud 버튼과 함께 들어가는 폭으로 잡았다.
        /// </summary>
        public const int MaxBranchLength = 24;

        /// <summary>
        /// 버튼 글자. 브랜치를 모르면 "Git", 알면 "Git · main" — 긴 이름은 앞부분만 남기고 "…" 로 줄인다.
        /// </summary>
        public static string ButtonText(string branchName)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                return ButtonLabel;
            }
            return $"{ButtonLabel} · {Shorten(branchName.Trim())}";
        }

        public static string Tooltip(string branchName, Func<string, string> localize)
        {
            if (string.IsNullOrWhiteSpace(branchName))
            {
                return localize("mainToolbar.tooltip");
            }
            return string.Format(localize("mainToolbar.tooltipBranch"), branchName.Trim());
        }

        /// <summary>
        /// 메뉴 줄 목록. 글자는 <paramref name="localize"/>(키 → 문구)로 만든다 — 테스트는 키를 그대로 돌려주는 함수를 넘긴다.
        /// </summary>
        public static IReadOnlyList<GitMainToolbarMenuItem> Build(Func<string, string> localize)
        {
            // 설정은 Preferences > Git 한 곳뿐이라 하위 메뉴 없이 한 줄로 둔다.
            return new[]
            {
                new GitMainToolbarMenuItem(localize("mainToolbar.openWindow"), GitMainToolbarCommand.OpenWindow),
                GitMainToolbarMenuItem.Separator,
                new GitMainToolbarMenuItem(localize("mainToolbar.hide"), GitMainToolbarCommand.HideFromToolbar),
                GitMainToolbarMenuItem.Separator,
                new GitMainToolbarMenuItem(localize("mainToolbar.preferences"), GitMainToolbarCommand.OpenPreferences),
                GitMainToolbarMenuItem.Separator,
                new GitMainToolbarMenuItem(localize("mainToolbar.help"), GitMainToolbarCommand.OpenHelp),
            };
        }

        private static string Shorten(string branchName)
        {
            if (branchName.Length <= MaxBranchLength)
            {
                return branchName;
            }
            return branchName.Substring(0, MaxBranchLength - 1) + "…";
        }
    }
}
