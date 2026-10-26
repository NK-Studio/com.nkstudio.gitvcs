using System.Collections.Generic;
using System.Linq;
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
    public sealed class GitWindow : EditorWindow, ITabNavigator, IInlineBranchCreator, IHasCustomMenu
    {
        private const string NotReadyModifier = "git-window--not-ready";

        /// <summary>
        /// 연결 화면에서 "이번에는 건너뛰기" 를 누른 에디터 세션. 에디터를 다시 켜면 연결 화면이 다시 나온다.
        /// </summary>
        private const string SkipConnectKey = "GitVersionControl.SkipGitHubConnect";

        [SerializeField] private GitWindowState _state = new();

        private GitServices _services;
        private GitToolbar _toolbar;
        private OperationBanner _operationBanner;
        private readonly List<GitView> _views = new();
        private readonly List<SidebarTab> _tabs = new();
        private VisualElement _window;
        private VisualElement _notReady;
        private Label _notReadyMessage;
        private UiScaleController _scale;
        private VisualElement _layout;
        private GitHubConnectPanel _connect;
        private RefSidebar _refs;
        private TwoPaneSplitView _body;
        private SidebarRail _rail;
        private bool _isSidebarCollapsed;

        [MenuItem("Window/Version Control/Git")]
        public static void Open()
        {
            GitWindow window = GetWindow<GitWindow>();
            window.titleContent = new GUIContent("Git", EditorGUIUtility.IconContent("d_UnityEditor.VersionControl").image);
            window.Show();
        }

        /// <summary>
        /// Git 창을 열고 그 위에 이 저장소의 GitHub 계정 고르기 팝업을 띄운다(Project Settings > Git 의 "바꾸기").
        /// </summary>
        public static void ChooseAccount()
        {
            Open();
            GitWindow window = GetWindow<GitWindow>();
            window.rootVisualElement.schedule.Execute(() =>
            {
                if (window._services != null)
                {
                    _ = GitAccountDialog.ShowAsync(window._services, null);
                }
            });
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
            // 서브모듈을 열거나 돌아오면 탭을 다시 만든다 — 지난 저장소의 커밋 목록·선택·diff 가 남지 않게.
            GitRepository.RepositorySwitched -= Rebuild;
            GitRepository.RepositorySwitched += Rebuild;
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
            layout.Require<Button>("open-settings-button").clicked += () => SettingsService.OpenProjectSettings(GitIntegration.ProjectSettingsPath);

            // 왼쪽 탭 경계선도 UI 배율에서 끌리도록 같은 처리기를 붙인다.
            _body = layout.Require<TwoPaneSplitView>("body");
            SplitPaneClamp.Attach(_body);
            _isSidebarCollapsed = false;
            _rail = new SidebarRail(layout.Require<VisualElement>("sidebar-rail"));
            _rail.Root.SetVisible(false);
            _rail.ExpandRequested += RestoreSidebar;
            _toolbar = new GitToolbar(layout, _services, this);
            _operationBanner = new OperationBanner(layout, _services);
            _toolbar.RefreshRequested += () =>
            {
                _views.Find(view => view.IsActive)?.Reload();
                _refs?.Reload();
            };
            CreateViews(layout.Require<VisualElement>("content"), layout.Require<VisualElement>("sidebar-views"));
            _services.InlineBranches = this;
            _services.PopupHost = _window;
            GitContextMenu.Attach(layout, _window);
            _refs =new RefSidebar(layout.Require<VisualElement>("sidebar-refs"), _services, _state.Refs);
            _refs.RevealRequested += Reveal;
            _refs.ShowInExplorerRequested += hash => SelectTab<BranchExplorerView>()?.Reveal(hash);
            foreach (RefSection section in RefTreeBuilder.Sections)
            {
                RefSection captured = section;
                _rail.AddSection(section, RefSidebar.SectionIcon(section), () =>
                {
                    RestoreSidebar();
                    _refs.RevealSection(captured);
                });
            }
            _refs.Loaded += () =>
            {
                foreach (RefSection section in RefTreeBuilder.Sections)
                {
                    _rail.SetSectionCount(section, _refs.SectionCount(section));
                }
            };

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
            GitRepository.RepositorySwitched -= Rebuild;
            TearDown();
        }

        private void TearDown()
        {
            _connect?.Dispose();
            _connect = null;
            _refs?.Dispose();
            _refs = null;
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
                menu.AddItem(new GUIContent($"{layoutMenu}/{GitDisplay.MenuSafe(GitLayout.DisplayName(style))}"), GitLayout.Style == style, () => GitLayout.Style = captured);
            }
            string themeMenu = GitText.Get("window.theme");
            foreach (GitThemeStyle style in (GitThemeStyle[])System.Enum.GetValues(typeof(GitThemeStyle)))
            {
                GitThemeStyle captured = style;
                menu.AddItem(new GUIContent($"{themeMenu}/{GitDisplay.MenuSafe(GitTheme.DisplayName(style))}"), GitTheme.Style == style, () => GitTheme.Style = captured);
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
            menu.AddItem(new GUIContent(GitText.Get("common.vcsSettings")), false, () => SettingsService.OpenProjectSettings(GitIntegration.ProjectSettingsPath));
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
            // 보조 창(파일 히스토리 등)과 배율 설정을 같이 쓰므로 거기서 바꿨으면 돌아올 때 맞춘다.
            _scale?.Apply();
            _services?.Repository.RequestRefresh();
        }

        /// <summary>탭 목록. 새 탭은 여기 한 줄 추가로 끝난다.</summary>
        private void CreateViews(VisualElement content, VisualElement sidebar)
        {
            // 파일 히스토리는 GitKraken 레이아웃에서만 창 안(Graph 탭 가운데)에 크게 보이고, 나머지 레이아웃은 따로 뜨는 창이다.
            _services.FileHistory = null;
            switch (GitLayout.Style)
            {
                case GitLayoutStyle.Fork:
                    AddView(new ForkChangesView(_services, _state.Staging), content, sidebar);
                    break;
                case GitLayoutStyle.GitKraken:
                    GraphWorkspaceView graph = new(_services, _state.Staging, this);
                    // GitKraken 처럼 파일 diff 를 여는 동안 왼쪽 사이드바를 아이콘 줄로 접고, × 로 닫으면 다시 편다.
                    graph.DiffShownChanged += SetSidebarCollapsed;
                    _services.FileHistory = graph;
                    AddView(graph, content, sidebar);
                    break;
                default:
                    AddView(new PendingChangesView(_services, _state.Pending), content, sidebar);
                    break;
            }
            AddView(new BranchExplorerView(_services, _state.Explorer, this), content, sidebar);
            AddView(new WorkspaceView(_services, _state.Workspace), content, sidebar);
            AddView(new IncomingChangesView(_services), content, sidebar);
            AddView(new ChangesetsView(_services, _state.Changesets, this), content, sidebar);
        }

        public void FocusRefs()
        {
            _refs?.FocusFilter();
        }

        /// <summary>
        /// GitKraken 레이아웃(Graph 탭이 있을 때)만 받는다. Unity VCS·Fork 레이아웃은 false 라 부른 쪽이 대화상자로 묻는다.
        /// </summary>
        public bool TryBeginCreateBranch(string startPoint, bool revealGraph)
        {
            GraphWorkspaceView graph = _views.OfType<GraphWorkspaceView>().FirstOrDefault();
            if (graph == null || (graph.IsActive == false && revealGraph == false))
            {
                return false;
            }
            SelectTab(_views.IndexOf(graph));
            return graph.BeginCreateBranch(startPoint);
        }

        /// <summary>
        /// 사이드바에서 고른 브랜치·태그·스태시의 커밋을 보인다. 보고 있는 탭이 보일 수 있으면 그 탭에서,
        /// 아니면(작업 공간·수신 변경, 또는 목록에 없는 커밋) 그래프 탭(GitKraken 형태면 Graph, 아니면 Branch Explorer)으로 넘어가서 보인다.
        /// </summary>
        private void Reveal(string hash)
        {
            if (_views.Find(view => view.IsActive) is ICommitRevealer active && active.RevealCommit(hash))
            {
                return;
            }
            GitView target = (GitView)_views.OfType<GraphWorkspaceView>().FirstOrDefault() ?? _views.OfType<BranchExplorerView>().FirstOrDefault();
            if (target == null)
            {
                return;
            }
            SelectTab(_views.IndexOf(target));
            ((ICommitRevealer)target).RevealCommit(hash);
        }

        private void AddView(GitView view, VisualElement content, VisualElement sidebar)
        {
            int index = _views.Count;
            _views.Add(view);
            content.Add(view.Root);

            SidebarTab tab = new(GitUiAssets.Load("SidebarTab"), view.Title);
            tab.Clicked += () => SelectTab(index);
            _rail.AddView(RailIcon(view), view.Title, () =>
            {
                SelectTab(index);
                RestoreSidebar();
            });
            view.BadgeChanged += () =>
            {
                tab.SetBadge(view.Badge);
                _rail.SetViewBadge(index, view.Badge);
            };
            sidebar.Add(tab.Root);
            _tabs.Add(tab);
        }

        /// <summary>
        /// 접힌 사이드바(<see cref="SidebarRail"/>)의 탭 아이콘. 임시 아이콘이다 — 새 아이콘으로 바꿀 때 여기만 고친다.
        /// </summary>
        private static ActionGlyphKind RailIcon(GitView view)
        {
            return view switch
            {
                GraphWorkspaceView => ActionGlyphKind.Hierarchy,
                BranchExplorerView => ActionGlyphKind.Branch,
                WorkspaceView => ActionGlyphKind.Folder,
                IncomingChangesView => ActionGlyphKind.Pull,
                ChangesetsView => ActionGlyphKind.PathList,
                _ => ActionGlyphKind.Pencil,
            };
        }

        /// <summary>
        /// 접힌 줄에서 무엇을 누르든(펼치기·탭·구역) GitKraken 처럼 원래 화면으로 돌아온다: Graph 가운데의 diff 를 닫고 사이드바를 편다.
        /// 그래서 diff 가 열려 있는 동안에는 늘 접혀 있다.
        /// </summary>
        private void RestoreSidebar()
        {
            _views.OfType<GraphWorkspaceView>().FirstOrDefault()?.CloseDiff();
            SetSidebarCollapsed(false);
        }

        /// <summary>
        /// 왼쪽 사이드바를 아이콘 줄로 접거나 다시 편다. 접어도 사이드바 폭(<see cref="SplitPaneClamp"/> 가 기억)은 그대로라 펴면 원래 폭으로 돌아온다.
        /// </summary>
        private void SetSidebarCollapsed(bool collapsed)
        {
            if (_body == null || _isSidebarCollapsed == collapsed)
            {
                return;
            }
            _isSidebarCollapsed = collapsed;
            _rail.Root.SetVisible(collapsed);
            if (collapsed)
            {
                _body.CollapseChild(0);
            }
            else
            {
                _body.UnCollapse();
            }
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
            _rail.SetSelectedView(index);
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
            _operationBanner.Render();
            if (ready == false)
            {
                return;
            }
            // stash 개수는 stash·pop·전환 같은 전체 새로고침 작업에서만 바뀐다. Stage·파일 저장 뒤에는 git 을 또 부르지 않는다.
            if (repository.LastRefreshWasWorkingTreeOnly == false)
            {
                _toolbar.RefreshStashCount();
                // 작업 막대 브랜치 목록을 뒤에서 읽어 둔다 — 누르자마자 다 그린 목록이 보이게.
                _ = GitBranchPicker.Prefetch();
            }
            _refs.OnRepositoryChanged();
            foreach (GitView view in _views)
            {
                view.OnRepositoryChanged();
            }
        }

        private void OnRunnerStateChanged()
        {
            _toolbar.Render();
            _operationBanner.Render();
            foreach (GitView view in _views)
            {
                view.OnBusyChanged(_services.Runner.IsBusy);
            }
        }
    }
}
