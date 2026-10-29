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

        /// <summary>새로 뜰 때의 크기. 이미지 diff 의 두 판과 코드 diff 의 양쪽이 읽을 만하게.</summary>
        private static readonly Vector2 PreferredSize = new(1200, 800);

        public static void Open(GitDiffTarget target, IGitDiffService service)
        {
            bool isNew = HasOpenInstances<GitDiffWindow>() == false;
            GitDiffWindow window = GetWindow<GitDiffWindow>();
            window.titleContent = new GUIContent(GitText.Get("diff.windowTitle"), GitIcons.Window);
            window._service = service;
            window._target = target;
            window.Show();
            if (isNew)
            {
                window.position = GitWindowPlacement.CenteredOnMain(PreferredSize);
            }
            window._panel?.SetTarget(target);
        }

        private void CreateGUI()
        {
            GitIcons.ApplyTo(this);
            // 너무 좁으면 diff 막대가 겹쳐 깨진다. 막대가 버튼을 숨겨 가며 들어가는 폭까지만 줄인다(보조 창과 같다).
            minSize = new Vector2(520, 300);
            _service ??= new GitDiffService(new OverlayDialogService());

            VisualElement scaleHost = new();
            VisualElement root = new();
            rootVisualElement.Add(scaleHost);
            scaleHost.Add(root);
            // 배율은 Git 창·보조 창(히스토리·blame·비교)과 같은 설정을 쓴다 — 창마다 글자 크기가 달라 보이지 않게.
            GitVcsSettings settings = GitVcsSettings.instance;
            _scale = new UiScaleController(scaleHost, root, () => settings.GitWindowScale, value => settings.GitWindowScale = value);

            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(CommonStylePath));
            root.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath));
            root.AddToClassList("git-window");
            _content = root;
            GitContextMenu.Attach(root, root);
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
            // 다른 창에서 배율을 바꾼 뒤 돌아오면 그 배율로 맞춘다.
            _scale?.Apply();
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
