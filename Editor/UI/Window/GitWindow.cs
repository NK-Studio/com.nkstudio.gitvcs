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

        /// <summary>
        /// 연결 화면에서 "이번에는 건너뛰기" 를 누른 에디터 세션. 에디터를 다시 켜면 연결 화면이 다시 나온다.
        /// </summary>
        private const string SkipConnectKey = "GitVersionControl.SkipGitHubConnect";

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
        private GitHubConnectPanel _connect;

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
            GitLayout.Changed -= Rebuild;
            GitLayout.Changed += Rebuild;
            // 로그인하면 연결 화면에서 본체로, 로그아웃하면 다시 연결 화면으로 바꾼다.
            GitHubAccount.Changed -= Rebuild;
            GitHubAccount.Changed += Rebuild;
            Build();
        }

        /// <summary>
        /// GitHub 에 로그인하지 않았으면 본체 대신 연결 화면을 띄운다. 패키지에 OAuth 앱이 없으면(로그인할 수 없으면) 묻지 않는다.
        /// </summary>
        private static bool NeedsGitHubConnect =>
            GitHubAccount.IsConfigured && GitHubAccount.IsSignedIn == false && SessionState.GetBool(SkipConnectKey, false) == false;

        private void Build()
        {
            if (NeedsGitHubConnect)
            {
                BuildConnect();
                return;
            }
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

            // 왼쪽 탭 경계선도 UI 배율에서 끌리도록 같은 처리기를 붙인다.
            SplitPaneClamp.Attach(layout.Require<TwoPaneSplitView>("body"));
            _toolbar = new GitToolbar(layout, _services, this);
            CreateViews(layout.Require<VisualElement>("content"), layout.Require<VisualElement>("sidebar"));

            _services.Repository.Changed += OnRepositoryChanged;
            _services.Runner.StateChanged += OnRunnerStateChanged;

            SelectTab(Mathf.Clamp(_state.SelectedTab, 0, _views.Count - 1));
            OnRepositoryChanged();
        }

        private void BuildConnect()
        {
            TemplateContainer layout = GitUiAssets.CloneLayout("GitHubConnect");
            rootVisualElement.Add(layout);
            _layout = layout;
            ApplyTheme();
            _connect = new GitHubConnectPanel(layout, () =>
            {
                SessionState.SetBool(SkipConnectKey, true);
                Rebuild();
            });
        }

        private void OnDisable()
        {
            GitText.LanguageChanged -= Rebuild;
            GitTheme.Changed -= ApplyTheme;
            GitLayout.Changed -= Rebuild;
            GitHubAccount.Changed -= Rebuild;
            TearDown();
        }

        private void TearDown()
        {
            _connect?.Dispose();
            _connect = null;
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
            string layoutMenu = GitText.Get("window.layout");
            foreach (GitLayoutStyle style in (GitLayoutStyle[])System.Enum.GetValues(typeof(GitLayoutStyle)))
            {
                GitLayoutStyle captured = style;
                menu.AddItem(new GUIContent($"{layoutMenu}/{GitLayout.DisplayName(style)}"), GitLayout.Style == style, () => GitLayout.Style = captured);
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

            // 저장소 폴더를 OS 도구로 연다. 문구는 OS 이름을 따른다(파일 탐색기 / Finder / 파일 관리자).
            menu.AddSeparator(string.Empty);
            string repoRoot = GitRepository.RepoRoot ?? GitRepository.ProjectRoot;
            menu.AddItem(new GUIContent(GitText.Format("window.openRepoIn", GitFileManager.OpenLabel)), false, () => GitFileManager.OpenFolder(repoRoot));
            menu.AddItem(new GUIContent(GitText.Get("window.openTerminal")), false, () =>
            {
                string error = GitTerminal.Open(repoRoot);
                if (error != null)
                {
                    EditorUtility.DisplayDialog(GitText.Get("actionbar.terminalFailed"), error, GitText.Get("common.ok"));
                }
            });
            menu.AddItem(new GUIContent(GitText.Get("common.vcsSettings")), false, () => SettingsService.OpenProjectSettings("Project/Version Control"));
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
            switch (GitLayout.Style)
            {
                case GitLayoutStyle.Fork:
                    AddView(new ForkChangesView(_services, _state.Staging), content, sidebar);
                    break;
                case GitLayoutStyle.GitKraken:
                    AddView(new GraphWorkspaceView(_services, _state.Staging, this), content, sidebar);
                    break;
                default:
                    AddView(new PendingChangesView(_services, _state.Pending), content, sidebar);
                    break;
            }
            AddView(new IncomingChangesView(_services), content, sidebar);
            AddView(new ChangesetsView(_services, _state.Changesets, this), content, sidebar);
            AddView(new BranchesView(_services, _state.Branches, this), content, sidebar);
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

        public T SelectTab<T>() where T : GitView
        {
            int index = _views.FindIndex(view => view is T);
            if (index < 0)
            {
                return null;
            }
            SelectTab(index);
            return (T)_views[index];
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
