using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 내장 diff 뷰어 창. 내용은 <see cref="GitDiffPanel"/> 이 그리고, 이 창은 테마·UI 배율·⋮ 메뉴만 맡는다.
    /// 창은 하나를 다시 쓴다 — 다른 파일을 열면 같은 창의 내용이 바뀐다.
    /// </summary>
    public sealed class GitDiffWindow : EditorWindow, IHasCustomMenu
    {
        private const string StylePath = "Packages/com.nkstudio.gitvcs/Editor/UI/Styles/GitDiff.uss";
        private const string CommonStylePath = "Packages/com.nkstudio.gitvcs/Editor/UI/Styles/GitCommon.uss";

        [SerializeField] private GitDiffTarget _target;

        private IGitDiffService _service;
        private GitDiffPanel _panel;
        private UiScaleController _scale;
        private VisualElement _content;

        public static void Open(GitDiffTarget target, IGitDiffService service)
        {
            GitDiffWindow window = GetWindow<GitDiffWindow>();
            window.titleContent = new GUIContent(GitText.Get("diff.windowTitle"), EditorGUIUtility.IconContent("d_UnityEditor.VersionControl").image);
            window._service = service;
            window._target = target;
            window.Show();
            window._panel?.SetTarget(target);
        }

        private void CreateGUI()
        {
            _service ??= new GitDiffService(new EditorDialogService());

            VisualElement scaleHost = new();
            VisualElement root = new();
            rootVisualElement.Add(scaleHost);
            scaleHost.Add(root);
            GitVcsSettings settings = GitVcsSettings.instance;
            _scale = new UiScaleController(scaleHost, root, () => settings.DiffWindowScale, value => settings.DiffWindowScale = value);

            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(CommonStylePath));
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath));
            root.AddToClassList("git-window");
            _content = root;
            GitTheme.Apply(root);
            GitTheme.Changed -= ApplyTheme;
            GitTheme.Changed += ApplyTheme;

            _panel = new GitDiffPanel(_service);
            _panel.Root.style.flexGrow = 1;
            root.Add(_panel.Root);
            _panel.SetTarget(_target);
        }

        private void ApplyTheme()
        {
            if (_content != null)
            {
                GitTheme.Apply(_content);
            }
        }

        private void OnFocus()
        {
            ApplyTheme();
        }

        private void OnDisable()
        {
            GitTheme.Changed -= ApplyTheme;
        }

        /// <summary>
        /// 창 오른쪽 위 ⋮ 메뉴: 테마, UI 크기(Ctrl+휠 을 모르는 사람을 위한 같은 기능).
        /// </summary>
        public void AddItemsToMenu(GenericMenu menu)
        {
            string themeMenu = GitText.Get("window.theme");
            foreach (GitThemeStyle style in (GitThemeStyle[])Enum.GetValues(typeof(GitThemeStyle)))
            {
                GitThemeStyle captured = style;
                menu.AddItem(new GUIContent($"{themeMenu}/{GitDisplay.MenuSafe(GitTheme.DisplayName(style))}"), GitTheme.Style == style, () => GitTheme.Style = captured);
            }
            string scaleMenu = GitText.Get("window.uiScale");
            menu.AddItem(new GUIContent($"{scaleMenu}/{GitText.Get("window.uiScaleIn")}"), false, () => _scale?.ZoomIn());
            menu.AddItem(new GUIContent($"{scaleMenu}/{GitText.Get("window.uiScaleOut")}"), false, () => _scale?.ZoomOut());
            menu.AddItem(new GUIContent($"{scaleMenu}/{GitText.Format("window.uiScaleReset", Mathf.RoundToInt(GitVcsSettings.instance.DiffWindowScale * 100f))}"), false, () => _scale?.Reset());
        }
    }
}
