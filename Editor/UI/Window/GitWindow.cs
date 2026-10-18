using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Git 창(UI Toolkit). Unity Version Control 창처럼 위쪽 도구 막대, 왼쪽 탭, 오른쪽 내용으로 구성한다.
    ///
    /// 이 클래스는 합성 루트다: 서비스를 만들고(<see cref="GitServices.CreateDefault"/>), 탭을 만들어 끼우고,
    /// 저장소·작업 상태 이벤트를 탭에 전달한다. 탭 내용은 각 <see cref="GitView"/> 가, 도구 막대는 <see cref="GitToolbar"/> 가 맡는다.
    /// </summary>
    public sealed class GitWindow : EditorWindow, ITabNavigator, IHasCustomMenu
    {
        private const string NotReadyModifier = "git-window--not-ready";

        [SerializeField] private GitWindowState _state = new();

        private GitServices _services;
        private GitToolbar _toolbar;
        private readonly List<GitView> _views = new();
        private readonly List<SidebarTab> _tabs = new();
        private VisualElement _window;
        private VisualElement _notReady;
        private Label _notReadyMessage;
        private UiScaleController _scale;
        private VisualElement _layout;

        [MenuItem("Window/Version Control/Git")]
        public static void Open()
        {
            GitWindow window = GetWindow<GitWindow>();
            window.titleContent = new GUIContent("Git", EditorGUIUtility.IconContent("d_UnityEditor.VersionControl").image);
            window.Show();
        }

        private void CreateGUI()
        {
            GitText.LanguageChanged -= Rebuild;
            GitText.LanguageChanged += Rebuild;
            GitTheme.Changed -= ApplyTheme;
            GitTheme.Changed += ApplyTheme;
            Build();
        }

        private void Build()
        {
            _services = GitServices.CreateDefault();

            TemplateContainer layout = GitUiAssets.CloneLayout("GitWindow");
            VisualElement scaleHost = new();
            rootVisualElement.Add(scaleHost);
            scaleHost.Add(layout);
            _layout = layout;
            ApplyTheme();
            GitVcsSettings settings = GitVcsSettings.instance;
            _scale = new UiScaleController(scaleHost, layout, () => settings.GitWindowScale, value => settings.GitWindowScale = value);
            _window = layout.Require<VisualElement>("git-window");
            _notReady = layout.Require<VisualElement>("not-ready");
            _notReadyMessage = layout.Require<Label>("not-ready-message");
            layout.Require<Button>("open-settings-button").clicked += () => SettingsService.OpenProjectSettings("Project/Version Control");

            _toolbar = new GitToolbar(layout, _services, this);
            CreateViews(layout.Require<VisualElement>("content"), layout.Require<VisualElement>("sidebar"));

            _services.Repository.Changed += OnRepositoryChanged;
            _services.Runner.StateChanged += OnRunnerStateChanged;

            SelectTab(Mathf.Clamp(_state.SelectedTab, 0, _views.Count - 1));
            OnRepositoryChanged();
        }

        private void OnDisable()
        {
            GitText.LanguageChanged -= Rebuild;
            GitTheme.Changed -= ApplyTheme;
            TearDown();
        }

        private void TearDown()
        {
            if (_services == null)
            {
                return;
            }
            _services.Repository.Changed -= OnRepositoryChanged;
            _services.Runner.StateChanged -= OnRunnerStateChanged;
            foreach (GitView view in _views)
            {
                view.Dispose();
            }
            _views.Clear();
            _tabs.Clear();
            _services = null;
        }

        /// <summary>
        /// 언어가 바뀌면 UI 를 통째로 다시 만든다. 탭 상태(선택 탭·체크·메시지·확대)는 직렬화된 <see cref="GitWindowState"/> 에 있어서 그대로 이어진다.
        /// </summary>
        private void Rebuild()
        {
            TearDown();
            rootVisualElement.Clear();
            Build();
        }

        /// <summary>창 오른쪽 위 ⋮ 메뉴에 언어 선택을 더한다.</summary>
        public void AddItemsToMenu(GenericMenu menu)
        {
            foreach (GitLanguage language in GitText.AllLanguages)
            {
                GitLanguage captured = language;
                menu.AddItem(new GUIContent($"{GitText.Get("common.language")}/{GitText.NativeName(language)}"),
                    GitText.Language == language, () => GitText.SetLanguage(captured));
            }
            string themeMenu = GitText.Get("window.theme");
            foreach (GitThemeStyle style in (GitThemeStyle[])System.Enum.GetValues(typeof(GitThemeStyle)))
            {
                GitThemeStyle captured = style;
                menu.AddItem(new GUIContent($"{themeMenu}/{GitTheme.DisplayName(style)}"), GitTheme.Style == style, () => GitTheme.Style = captured);
            }
            string scaleMenu = GitText.Get("window.uiScale");
            menu.AddItem(new GUIContent($"{scaleMenu}/{GitText.Get("window.uiScaleIn")}"), false, () => _scale?.ZoomIn());
            menu.AddItem(new GUIContent($"{scaleMenu}/{GitText.Get("window.uiScaleOut")}"), false, () => _scale?.ZoomOut());
            menu.AddItem(new GUIContent($"{scaleMenu}/{GitText.Format("window.uiScaleReset", Mathf.RoundToInt(GitVcsSettings.instance.GitWindowScale * 100f))}"), false, () => _scale?.Reset());
        }

        /// <summary>
        /// 테마 시트는 창 내용(템플릿) 루트에 붙인다 — 창 USS 와 같은 높이라 토큰이 모든 탭·목록 줄에 닿는다.
        /// 에디터 스킨(어둡게·밝게)을 바꾸면 창에 포커스가 돌아올 때 맞춘다.
        /// </summary>
        private void ApplyTheme()
        {
            if (_layout != null)
            {
                GitTheme.Apply(_layout);
            }
        }

        private void OnFocus()
        {
            ApplyTheme();
            _services?.Repository.RequestRefresh();
        }

        /// <summary>탭 목록. 새 탭은 여기 한 줄 추가로 끝난다.</summary>
        private void CreateViews(VisualElement content, VisualElement sidebar)
        {
            AddView(new PendingChangesView(_services, _state.Pending), content, sidebar);
            AddView(new IncomingChangesView(_services), content, sidebar);
            AddView(new ChangesetsView(_services, _state.Changesets), content, sidebar);
            AddView(new BranchesView(_services, _state.Branches), content, sidebar);
            AddView(new BranchExplorerView(_services, _state.Explorer, this), content, sidebar);
        }

        private void AddView(GitView view, VisualElement content, VisualElement sidebar)
        {
            int index = _views.Count;
            _views.Add(view);
            content.Add(view.Root);

            SidebarTab tab = new(GitUiAssets.Load("SidebarTab"), view.Title);
            tab.Clicked += () => SelectTab(index);
            view.BadgeChanged += () => tab.SetBadge(view.Badge);
            sidebar.Add(tab.Root);
            _tabs.Add(tab);
        }

        public void SelectTab<T>() where T : GitView
        {
            int index = _views.FindIndex(view => view is T);
            if (index >= 0)
            {
                SelectTab(index);
            }
        }

        private void SelectTab(int index)
        {
            for (int i = 0; i < _views.Count; i++)
            {
                bool selected = i == index;
                _tabs[i].SetSelected(selected);
                if (selected && _views[i].IsActive == false)
                {
                    _views[i].Activate();
                }
                else if (selected == false && _views[i].IsActive)
                {
                    _views[i].Deactivate();
                }
            }
            _state.SelectedTab = index;
        }

        private void OnRepositoryChanged()
        {
            IGitRepositoryState repository = _services.Repository;
            bool ready = repository.IsRunning && repository.IsRepository;
            _window.EnableInClassList(NotReadyModifier, ready == false);
            _notReady.SetVisible(ready == false);
            _notReadyMessage.text = repository.IsRunning == false
                ? GitText.Get("window.notGitMode")
                : string.IsNullOrEmpty(repository.LastError) ? GitText.Get("window.notRepository") : repository.LastError;

            _toolbar.Render();
            if (ready == false)
            {
                return;
            }
            _toolbar.RefreshStashCount();
            foreach (GitView view in _views)
            {
                view.OnRepositoryChanged();
            }
        }

        private void OnRunnerStateChanged()
        {
            _toolbar.Render();
            foreach (GitView view in _views)
            {
                view.OnBusyChanged(_services.Runner.IsBusy);
            }
        }
    }
}
