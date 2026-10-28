using System;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 브랜치 탐색기 탭의 모양. EditorPrefs 에 숫자로 저장되므로 끝에만 추가한다.
    /// </summary>
    public enum ExplorerStyle
    {
        /// <summary>
        /// Unity Version Control 처럼 확대·이동하는 가로 브랜치 그래프, 오른쪽 커밋 상세(<see cref="BranchExplorerView"/>).
        /// </summary>
        BranchGraph,

        /// <summary>
        /// Fork 처럼 위는 커밋 목록(그래프·작성자·SHA·날짜), 아래는 Commit · Changes · File Tree 칸(<see cref="CommitHistoryView"/>).
        /// </summary>
        CommitList,
    }

    /// <summary>
    /// 브랜치 탐색기 모양 고르기. 사람마다 좋아하는 모양이 달라 프로젝트가 아니라 이 PC 의 개인 설정(EditorPrefs)으로 둔다.
    /// GitKraken 배치는 Graph 탭이 이미 세로 커밋 목록이라 탐색기는 가로 브랜치 그래프로 고정한다.
    /// Fork · Unity VCS 배치는 고를 수 있고, 고르지 않았으면 커밋 목록(Fork 형태)이다.
    /// 고르는 곳: Preferences > Git, Project Settings > Git(배치 아래), Git 창 ⋮ 메뉴, 브랜치 탐색기 도구 막대.
    /// 바꾸면 열린 Git 창이 UI 를 다시 만든다(탭이 다른 클래스라서).
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitExplorerStyle
    {
        private const string PrefsKey = "GitVersionControl.ExplorerStyle";

        public static event Action Changed;

        /// <summary>
        /// 지금 배치에서 모양을 고를 수 있는가(GitKraken 이 아니면).
        /// </summary>
        public static bool IsChoosable => IsChoosableFor(GitLayout.Style);

        public static bool IsChoosableFor(GitLayoutStyle layout)
        {
            return layout != GitLayoutStyle.GitKraken;
        }

        /// <summary>
        /// 실제로 쓰는 모양. GitKraken 배치면 고른 값과 상관없이 브랜치 그래프.
        /// </summary>
        public static ExplorerStyle Effective => EffectiveFor(GitLayout.Style, Style);

        public static ExplorerStyle EffectiveFor(GitLayoutStyle layout, ExplorerStyle chosen)
        {
            return IsChoosableFor(layout) ? chosen : ExplorerStyle.BranchGraph;
        }

        /// <summary>
        /// 고른 모양(고르지 않았으면 커밋 목록). GitKraken 배치에서는 쓰지 않지만 값은 남겨 둔다.
        /// </summary>
        public static ExplorerStyle Style
        {
            get
            {
                int value = EditorPrefs.GetInt(PrefsKey, (int)ExplorerStyle.CommitList);
                return Enum.IsDefined(typeof(ExplorerStyle), value) ? (ExplorerStyle)value : ExplorerStyle.CommitList;
            }
            set
            {
                if (Style == value)
                {
                    return;
                }
                EditorPrefs.SetInt(PrefsKey, (int)value);
                Changed?.Invoke();
            }
        }

        public static string DisplayName(ExplorerStyle style)
        {
            return GitText.Get("explorerStyle." + style);
        }

        /// <summary>
        /// Preferences · Project Settings 의 고르기 칸. GitKraken 배치면 끄고 고정이라고 알린다.
        /// </summary>
        public static void DrawPopup()
        {
            ExplorerStyle[] styles = (ExplorerStyle[])Enum.GetValues(typeof(ExplorerStyle));
            GUIContent[] names = Array.ConvertAll(styles, style => new GUIContent(DisplayName(style)));
            using (new EditorGUI.DisabledScope(IsChoosable == false))
            {
                int current = Array.IndexOf(styles, Effective);
                int picked = EditorGUILayout.Popup(new GUIContent(GitText.Get("settings.explorerStyle"), GitText.Get("settings.explorerStyleTooltip")), current, names);
                if (picked >= 0 && picked != current)
                {
                    Style = styles[picked];
                }
            }
            if (IsChoosable == false)
            {
                EditorGUILayout.LabelField(" ", GitText.Get("settings.explorerStyleFixed"), EditorStyles.miniLabel);
            }
        }

        /// <summary>
        /// Git 창 ⋮ 메뉴 항목. GitKraken 배치면 그래프 하나만 체크된 채 꺼 둔다.
        /// </summary>
        public static void AddMenuItems(GenericMenu menu)
        {
            string prefix = GitText.Get("settings.explorerStyle");
            foreach (ExplorerStyle style in (ExplorerStyle[])Enum.GetValues(typeof(ExplorerStyle)))
            {
                GUIContent item = new($"{prefix}/{GitDisplay.MenuSafe(DisplayName(style))}");
                if (IsChoosable)
                {
                    ExplorerStyle captured = style;
                    menu.AddItem(item, Style == style, () => Style = captured);
                }
                else
                {
                    menu.AddDisabledItem(item, style == ExplorerStyle.BranchGraph);
                }
            }
        }
    }
}
