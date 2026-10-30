using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 파일 히스토리·blame 같은 보조 창의 공통 바탕: Git 창과 같은 스타일 시트·테마·UI 배율을 붙인 내용 루트(<see cref="Content"/>).
    /// 배율은 Git 창과 같은 설정을 쓴다 — Git 창에서 연 창이 그 창과 다른 글자 크기로 뜨지 않게.
    /// </summary>
    public abstract class GitToolWindowBase : EditorWindow, IHasCustomMenu
    {
        private const string StyleFolder = "Packages/com.nkstudio.gitvcs/Editor/UI/Styles/";
        private static readonly string[] Sheets = { "GitCommon", "GitRows", "GitViews", "GitDiff", "GitStaging" };

        private UiScaleController _scale;

        protected VisualElement Content { get; private set; }

        /// <summary>
        /// 창을 이보다 작게 줄이지 못하게 한다. 너무 좁으면 diff 막대·목록 머리가 겹쳐 깨진다.
        /// diff 막대는 좁아지면 덜 중요한 버튼부터 숨기는데(GitDiffPanel), 520 은 그렇게 줄인 막대가 들어가는 폭이다.
        /// </summary>
        protected virtual Vector2 MinimumSize => new(520, 360);

        protected void CreateContent()
        {
            GitIcons.ApplyTo(this);
            minSize = MinimumSize;
            VisualElement scaleHost = new();
            Content = new VisualElement();
            Content.AddToClassList("git-window");
            foreach (string sheet in Sheets)
            {
                Content.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleFolder + sheet + ".uss"));
            }
            rootVisualElement.Add(scaleHost);
            scaleHost.Add(Content);
            GitVcsSettings settings = GitVcsSettings.instance;
            _scale = new UiScaleController(scaleHost, Content, () => settings.GitWindowScale, value => settings.GitWindowScale = value);
            GitContextMenu.Attach(Content, Content);
            // 이 창에서 시작한 확인·입력 팝업은 이 창 위에 뜬다(Git 창으로 넘어가지 않게).
            GitPopupHosts.Register(this, Content);
            // Git 창과 같은 툴팁(기본 툴팁 대신 이 창 위 말풍선).
            HoverCallout.ReplaceTooltips(rootVisualElement, () => Content);
            GitTheme.Apply(Content);
            GitTheme.Changed -= ApplyTheme;
            GitTheme.Changed += ApplyTheme;
        }

        protected virtual void OnDisable()
        {
            GitPopupHosts.Unregister(this);
            GitTheme.Changed -= ApplyTheme;
        }

        private void OnFocus()
        {
            ApplyTheme();
            // Git 창에서 배율을 바꾼 뒤 돌아오면 그 배율로 맞춘다.
            _scale?.Apply();
        }

        private void ApplyTheme()
        {
            if (Content != null)
            {
                GitTheme.Apply(Content);
            }
        }

        protected static GUIContent TitleWithIcon(string title)
        {
            return new GUIContent(title, GitIcons.Window);
        }

        /// <summary>
        /// 창 오른쪽 위 ⋮ 메뉴: 테마, UI 크기(Ctrl+휠 을 모르는 사람을 위한 같은 기능).
        /// </summary>
        public virtual void AddItemsToMenu(GenericMenu menu)
        {
            string themeMenu = GitText.Get("window.theme");
            foreach (GitThemeMode mode in GitTheme.MenuOrder)
            {
                GitThemeMode captured = mode;
                menu.AddItem(new GUIContent($"{themeMenu}/{GitDisplay.MenuSafe(GitTheme.DisplayName(mode))}"), GitTheme.Mode == mode, () => GitTheme.Mode = captured);
            }
            string scaleMenu = GitText.Get("window.uiScale");
            menu.AddItem(new GUIContent($"{scaleMenu}/{GitText.Get("window.uiScaleIn")}"), false, () => _scale?.ZoomIn());
            menu.AddItem(new GUIContent($"{scaleMenu}/{GitText.Get("window.uiScaleOut")}"), false, () => _scale?.ZoomOut());
            menu.AddItem(new GUIContent($"{scaleMenu}/{GitText.Format("window.uiScaleReset", Mathf.RoundToInt(GitVcsSettings.instance.GitWindowScale * 100f))}"), false, () => _scale?.Reset());
        }
    }
}
