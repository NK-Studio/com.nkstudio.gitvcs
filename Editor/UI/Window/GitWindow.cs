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

        /// <summary>
        /// 저장소 만들기 카드를 이번 에디터 세션에 이미 지나왔는지(만들었거나 "나중에"). 그 뒤로는 본체의 "저장소가 아님" 안내가 맡는다.
        /// </summary>
        private const string SkipInitKey = "GitVersionControl.SkipInitCard";

        [SerializeField] private GitWindowState _state = new();

        private GitServices _services;
        private GitToolbar _toolbar;
        private OperationBanner _operationBanner;
        private readonly List<GitView> _views = new();
        private readonly List<SidebarTab> _tabs = new();
        private VisualElement _window;
        private VisualElement _notReady;
        private Label _notReadyMessage;
        private Button _notReadyAction;
        private bool _isInitializing;
        private UiScaleController _scale;
        private VisualElement _layout;
        private GitHubConnectPanel _connect;
        private RefSidebar _refs;
        private readonly List<SideDrawer> _drawers = new();
        private TwoPaneSplitView _body;
        private SidebarRail _rail;
        private bool _isSidebarCollapsed;

        [MenuItem("Window/Version Control/Git")]
        public static void Open()
        {
            GitWindow window = GetWindow<GitWindow>();
            window.titleContent = new GUIContent("Git", GitIcons.Window);
            window.Show();
        }

        /// <summary>
        /// Git 창을 열고 그 위에 이 저장소의 GitHub 계정 고르기 팝업을 띄운다(따로 뜬 창의 LFS "계정 고르기" 등).
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

        /// <summary>
        /// Git 창을 열고 변경 탭에서 그 파일을 골라 diff 를 연다(Project 창 우클릭 "Git 창에서 보기").
        /// </summary>
        public static void RevealChange(string repoPath)
        {
            Open();
            GitWindow window = GetWindow<GitWindow>();
            // 창을 막 만들었으면 탭이 아직 없다. 다음 틱에 고른다.
            window.rootVisualElement.schedule.Execute(() =>
            {
                int index = window._views.FindIndex(view => view.ShowsChanges);
                if (index < 0)
                {
                    return;
                }
                window.SelectTab(index);
                window._views[index].RevealChange(repoPath);
            });
        }

        /// <summary>
        /// 열려 있는 Git 창의 서비스. Git 창 밖(Project 창 메뉴)에서 작업할 때 같은 실행기를 써서 창의 작업과 겹치지 않게 한다. 창이 없으면 null.
        /// </summary>
        public static GitServices OpenWindowServices()
        {
            foreach (GitWindow window in Resources.FindObjectsOfTypeAll<GitWindow>())
            {
                if (window._services != null)
                {
                    return window._services;
                }
            }
            return null;
        }

        private void CreateGUI()
        {
            GitIcons.ApplyTo(this);
            GitText.LanguageChanged -= Rebuild;
            GitText.LanguageChanged += Rebuild;
            GitTheme.Changed -= ApplyTheme;
            GitTheme.Changed += ApplyTheme;
            GitLayout.Changed -= Rebuild;
            GitLayout.Changed += Rebuild;
            GitExplorerStyle.Changed -= Rebuild;
            GitExplorerStyle.Changed += Rebuild;
            // 로그인하면 연결 화면에서 본체로, 로그아웃하면 다시 연결 화면으로 바꾼다.
            GitHubAccount.Changed -= OnAccountChanged;
            GitHubAccount.Changed += OnAccountChanged;
            // 서브모듈을 열거나 돌아오면 탭을 다시 만든다 — 지난 저장소의 커밋 목록·선택·diff 가 남지 않게.
            GitRepository.RepositorySwitched -= Rebuild;
            GitRepository.RepositorySwitched += Rebuild;
            // Unity 기본 툴팁 대신 창 위 덮개에 우리 말풍선으로(두 번 뜨지 않게, 테마·배율을 따르게). 루트는 다시 만들어도 그대로라 한 번만 단다.
            HoverCallout.ReplaceTooltips(rootVisualElement, () => _window);
            Build();
        }

        /// <summary>
        /// GitHub 에 로그인하지 않았으면 본체 대신 연결 화면을 띄운다. 패키지에 OAuth 앱이 없으면(로그인할 수 없으면) 묻지 않는다.
        /// </summary>
        private static bool NeedsGitHubConnect =>
            GitHubAccount.IsConfigured && GitHubAccount.IsSignedIn == false && SessionState.GetBool(SkipConnectKey, false) == false;

        /// <summary>
        /// 저장소가 아닌 프로젝트에서 창을 열면 로그인보다 저장소 만들기를 먼저 보인다. 저장소가 없으면 어떤 기능도 못 쓰지만 로그인은 없어도 되고,
        /// 로그인은 PC 마다 한 번·저장소는 프로젝트마다 한 번이라 두 번째 프로젝트부터는 이 카드만 나온다.
        /// </summary>
        private static bool NeedsInitCard => NeedsRepository && SessionState.GetBool(SkipInitKey, false) == false;

        private void Build()
        {
            if (NeedsInitCard)
            {
                BuildConnect();
                _connect.ShowInitStep();
                return;
            }
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
            _notReadyAction = layout.Require<Button>("not-ready-action");
            _notReadyAction.clicked += OnNotReadyAction;

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
            GitPopupHosts.Register(this, _window);
            GitContextMenu.Attach(layout, _window);
            _refs =new RefSidebar(layout.Require<VisualElement>("sidebar-refs"), _services, _state.Refs);
            // 사이드바는 혼자 걷지 않고 처음 보이는 탭의 칸과 함께 걷는다(CoverWhileLoading).
            _refs.HoldReveal = true;
            _refs.RevealRequested += Reveal;
            _refs.ShowInExplorerRequested += hash => SelectTab<ExplorerView>()?.Reveal(hash);
            // 본문 왼쪽 가장자리(사이드바 바로 옆)에서 밀려 나오는 서랍들. 본문 위에 덮어 띄우고, 한 번에 하나만 연다.
            VisualElement content = layout.Require<VisualElement>("content");
            AddRemoteDrawer addRemote = AddDrawer(new AddRemoteDrawer(content, _services));
            AddWorktreeDrawer addWorktree = AddDrawer(new AddWorktreeDrawer(content, _services));
            AddSubmoduleDrawer addSubmodule = AddDrawer(new AddSubmoduleDrawer(content, _services));
            EditSubmoduleDrawer editSubmodule = AddDrawer(new EditSubmoduleDrawer(content, _services));
            _refs.SubmoduleEditRequested += editSubmodule.OpenFor;
            editSubmodule.Changed += _refs.Reload;
            CreatePullRequestDrawer createPullRequest = AddDrawer(new CreatePullRequestDrawer(content, _services));
            PullRequestFilterDrawer pullRequestFilter = AddDrawer(new PullRequestFilterDrawer(content, _services));
            PullRequestDetailDrawer pullRequestDetail = AddDrawer(new PullRequestDetailDrawer(content, _services));
            _refs.PullRequestOpened += pullRequestDetail.OpenFor;
            // 닫기·다시 열기·Draft 전환·병합은 사이드바 목록(묶음·Draft 표시)에 보인다.
            pullRequestDetail.StateChanged += _refs.ReloadPullRequests;
            // 병합 커밋을 그래프·Pull 숫자에 바로 보이게 원격을 받아 온다.
            pullRequestDetail.Merged += () => _ = GitAutoFetch.FetchNowAsync();
            _refs.PullRequestFilterRequested += index =>
            {
                if (index < 0)
                {
                    pullRequestFilter.OpenNew();
                }
                else
                {
                    pullRequestFilter.OpenEdit(index);
                }
            };
            _refs.CreateWorktreeRequested += addWorktree.OpenFor;
            _refs.AddRequested += section =>
            {
                switch (section)
                {
                    case RefSection.Remote:
                        addRemote.Open();
                        break;
                    case RefSection.Worktrees:
                        addWorktree.Open();
                        break;
                    case RefSection.Submodules:
                        addSubmodule.Open();
                        break;
                    case RefSection.PullRequests:
                        createPullRequest.OpenFor(null);
                        break;
                }
            };
            _refs.CreatePullRequestRequested += createPullRequest.OpenFor;
            _services.AddRemoteRequested = addRemote.Open;
            _services.ShowConflictsRequested = ShowConflicts;
            _services.CreatePullRequestRequested = createPullRequest.OpenFor;
            createPullRequest.Created += _refs.ReloadPullRequests;
            foreach (RefSection section in RefTreeBuilder.Sections)
            {
                RefSection captured = section;
                _rail.AddSection(section, RefSidebar.SectionIcon(section), () =>
                {
                    RestoreSidebar();
                    _refs.RevealSection(captured);
                });
                _rail.SetSectionVisible(section, _refs.IsSectionShown(section));
            }
            // 창을 다시 만들거나 닫은 뒤에도 그 전에 시작한 읽기가 늦게 끝나 Loaded 가 올 수 있다. 이 사이드바·레일을 붙잡고, 이미 바뀌었으면 무시한다.
            RefSidebar refs = _refs;
            SidebarRail rail = _rail;
            refs.Loaded += () =>
            {
                if (refs != _refs)
                {
                    return;
                }
                foreach (RefSection section in RefTreeBuilder.Sections)
                {
                    rail.SetSectionVisible(section, refs.IsSectionShown(section));
                    rail.SetSectionCount(section, refs.SectionCount(section));
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
                ProceedFromConnect();
            }, OnInitFinished);
        }

        /// <summary>
        /// 저장소 만들기 카드를 마쳤을 때: 만들었고 아직 로그인하지 않았으면 같은 카드에서 GitHub 연결을 선택으로 권하고, 아니면 본체로.
        /// "나중에" 는 지금은 Git 창만 보겠다는 뜻이라 이번 세션의 로그인 화면도 건너뛴다.
        /// </summary>
        private void OnInitFinished(bool created)
        {
            SessionState.SetBool(SkipInitKey, true);
            if (created && NeedsGitHubConnect)
            {
                _connect.ShowLoginStep();
                return;
            }
            if (created == false)
            {
                SessionState.SetBool(SkipConnectKey, true);
            }
            LeaveConnect();
        }

        /// <summary>
        /// git 은 찾았는데 이 프로젝트가 아직 저장소가 아닌지. 그러면 로그인 화면보다 저장소 만들기 카드를 먼저 보인다(<see cref="NeedsInitCard"/>).
        /// </summary>
        private static bool NeedsRepository => GitRepository.IsRunning && GitRepository.IsRepository == false;

        /// <summary>
        /// 연결 화면을 마칠 때(로그인했거나 로그인 없이 계속): 아직 저장소가 없고 이 카드에서 만들지 않았으면 같은 카드에서 git init 단계를, 아니면 본체로.
        /// 창을 열 때 git 이 아직 준비되지 않아 로그인 화면이 먼저 뜬 경우나, ⋮ 메뉴로 로그인 화면에 돌아온 경우다.
        /// 만든 직후에는 저장소 상태가 아직 갱신되지 않았을 수 있어 <see cref="GitHubConnectPanel.CreatedRepository"/> 로도 본다.
        /// </summary>
        private void ProceedFromConnect()
        {
            if (_connect != null && NeedsRepository && _connect.CreatedRepository == false)
            {
                _connect.ShowInitStep();
                return;
            }
            LeaveConnect();
        }

        /// <summary>
        /// ⋮ 메뉴 "GitHub 로그인…": 이미 본체를 쓰는 중에도(로그인 없이 계속했던 사람) 연결 화면으로 돌아가 로그인할 수 있게 한다.
        /// </summary>
        private void ShowConnect()
        {
            SessionState.SetBool(SkipConnectKey, false);
            Rebuild();
        }

        /// <summary>
        /// 로그인·로그아웃. 연결 화면에서 로그인에 성공했으면 연출과 함께 본체로(<see cref="LeaveConnect"/>), 그 밖에는 다시 만든다.
        /// </summary>
        private void OnAccountChanged()
        {
            if (_connect != null && NeedsGitHubConnect == false)
            {
                ProceedFromConnect();
                return;
            }
            Rebuild();
        }

        /// <summary>연결 화면 카드가 사라지는 시간·본체가 나타나는 시간(ms)과, 본체가 올라오는 거리(px).</summary>
        private const int ConnectLeaveMs = 200;
        private const int ShellEnterMs = 260;
        private const float ShellEnterRise = 6f;

        private bool _leavingConnect;

        /// <summary>
        /// 연결 화면 → 본체 전환 연출: 카드가 살짝 작아지며 사라진 뒤("다시 그리는 중" 덮개 없이) 본체를 만들고, 본체가 조금 아래에서 올라오며 나타난다.
        /// opacity·scale·translate 전환만 써서 가볍다. 한 번뿐인 순간이라 다른 화면 바꾸기보다 조금 길게 둔다.
        /// </summary>
        private void LeaveConnect()
        {
            if (_leavingConnect)
            {
                return;
            }
            _leavingConnect = true;
            VisualElement card = _layout?.Q(className: "github-connect__card");
            if (card != null)
            {
                card.style.transitionProperty = new List<StylePropertyName> { "opacity", "scale" };
                card.style.transitionDuration = new List<TimeValue> { new(ConnectLeaveMs, TimeUnit.Millisecond) };
                card.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseInCubic) };
                card.style.opacity = 0f;
                card.style.scale = new Scale(new Vector2(0.96f, 0.96f));
            }
            rootVisualElement.schedule.Execute(() =>
            {
                _leavingConnect = false;
                TearDown();
                rootVisualElement.Clear();
                Build();
                EnterShell();
            }).StartingIn(card != null ? ConnectLeaveMs : 0);
        }

        /// <summary>
        /// 방금 만든 본체를 투명·조금 아래에서 시작해 제자리로 올리며 나타낸다. 본체를 만든 직후에는 사이드바·변경 목록을 읽느라 에디터가 몇백 ms 멈추므로,
        /// 그대로 시작하면 멈춘 사이에 전환 시간이 다 지나 한 번에 튀어나온다 — 틀 간격이 다시 짧아질 때(<see cref="SmoothFrameSeconds"/>)까지 기다렸다 시작한다.
        /// 끝나면 인라인 전환을 지워 평소 스타일에 섞이지 않게 한다.
        /// </summary>
        private void EnterShell()
        {
            if (rootVisualElement.childCount == 0)
            {
                return;
            }
            VisualElement shell = rootVisualElement[0];
            shell.style.opacity = 0f;
            shell.style.translate = new Translate(0, ShellEnterRise);
            double started = EditorApplication.timeSinceStartup;
            double lastTick = started;
            IVisualElementScheduledItem wait = null;
            wait = shell.schedule.Execute(() =>
            {
                double now = EditorApplication.timeSinceStartup;
                bool smooth = now - lastTick < SmoothFrameSeconds;
                lastTick = now;
                if (smooth == false && now - started < ShellEnterMaxWait)
                {
                    return;
                }
                wait.Pause();
                Fade(shell);
            }).Every(16);
        }

        /// <summary>틀 간격이 이보다 짧으면 에디터가 다시 매끄럽게 도는 것으로 본다(초). 오래 멈춰도 이 시간(초)이 지나면 그냥 시작한다.</summary>
        private const double SmoothFrameSeconds = 0.05;
        private const double ShellEnterMaxWait = 1.5;

        private static void Fade(VisualElement shell)
        {
            shell.style.transitionProperty = new List<StylePropertyName> { "opacity", "translate" };
            shell.style.transitionDuration = new List<TimeValue> { new(ShellEnterMs, TimeUnit.Millisecond) };
            shell.style.transitionTimingFunction = new List<EasingFunction> { new(EasingMode.EaseOutCubic) };
            shell.style.opacity = 1f;
            shell.style.translate = new Translate(0, 0);
            shell.schedule.Execute(() =>
            {
                shell.style.transitionProperty = StyleKeyword.Null;
                shell.style.transitionDuration = StyleKeyword.Null;
                shell.style.transitionTimingFunction = StyleKeyword.Null;
                shell.style.opacity = StyleKeyword.Null;
                shell.style.translate = StyleKeyword.Null;
            }).StartingIn(ShellEnterMs + 60);
        }

        private void Update()
        {
            // git 이 도는 동안·막 끝난 직후만 스스로 다시 그려 에디터 틱을 빠르게 둔다(GitCommand.IsBusyOrRecent). 평소에는 아무것도 하지 않는다.
            if (GitCommand.IsBusyOrRecent)
            {
                Repaint();
            }
        }

        private void OnDisable()
        {
            GitText.LanguageChanged -= Rebuild;
            GitTheme.Changed -= ApplyTheme;
            GitLayout.Changed -= Rebuild;
            GitExplorerStyle.Changed -= Rebuild;
            GitHubAccount.Changed -= OnAccountChanged;
            GitRepository.RepositorySwitched -= Rebuild;
            GitPopupHosts.Unregister(this);
            TearDown();
        }

        private void TearDown()
        {
            _connect?.Dispose();
            _connect = null;
            _refs?.Dispose();
            _refs = null;
            _drawers.Clear();
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
            _covering.Clear();
            _sidebarCovering = false;
            _tabs.Clear();
            _services = null;
        }

        /// <summary>
        /// 언어가 바뀌면 UI 를 통째로 다시 만든다. 탭 상태(선택 탭·체크·메시지·확대)는 직렬화된 <see cref="GitWindowState"/> 에 있어서 그대로 이어진다.
        /// </summary>
        private bool _rebuildScheduled;

        /// <summary>
        /// 창 내용을 다시 만든다(언어·배치·테마 스타일 바꾸기). 다시 만드는 데 1초 남짓 걸리므로, 먼저 "다시 그리는 중" 을 덮어 보이고
        /// 다음 틀에 만든다 — 메뉴를 고른 뒤 아무 반응 없이 멈춘 것처럼 보이지 않게.
        /// </summary>
        private void Rebuild()
        {
            if (_rebuildScheduled)
            {
                return;
            }
            _rebuildScheduled = true;
            Label busy = new(GitText.Get("window.rebuilding"));
            busy.AddToClassList("git-window__rebuilding");
            busy.style.position = Position.Absolute;
            busy.style.left = 0;
            busy.style.right = 0;
            busy.style.top = 0;
            busy.style.bottom = 0;
            busy.style.unityTextAlign = TextAnchor.MiddleCenter;
            busy.style.backgroundColor = new Color(0, 0, 0, 0.25f);
            busy.style.color = Color.white;
            busy.style.fontSize = 14;
            rootVisualElement.Add(busy);
            rootVisualElement.schedule.Execute(() =>
            {
                _rebuildScheduled = false;
                TearDown();
                rootVisualElement.Clear();
                Build();
            });
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
            GitExplorerStyle.AddMenuItems(menu);
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

            // 저장소 폴더를 OS 도구로 연다. 문구는 OS 이름을 따른다(파일 탐색기 / Finder / 파일 관리자).
            menu.AddSeparator(string.Empty);
            string repoRoot = GitRepository.RepoRoot ?? GitRepository.ProjectRoot;
            menu.AddItem(new GUIContent(GitText.Format("window.openRepoIn", GitFileManager.OpenLabel)), false, () => GitFileManager.OpenFolder(repoRoot));
            menu.AddItem(new GUIContent(GitText.Get("window.openTerminal")), false, () =>
            {
                string error = GitTerminal.Open(repoRoot);
                if (error != null)
                {
                    _ = _services.Dialogs.AlertAsync(GitText.Get("actionbar.terminalFailed"), error, ActionGlyphKind.Terminal);
                }
            });
            menu.AddSeparator(string.Empty);
            // 로그인하지 않고 쓰는 중이면 연결 화면으로 돌아가 로그인할 수 있게(이미 연결 화면이면 빼고).
            if (GitHubAccount.IsConfigured && GitHubAccount.IsSignedIn == false && _connect == null)
            {
                menu.AddItem(new GUIContent(GitText.Get("window.connectGitHub")), false, ShowConnect);
            }
            if (GitRepository.IsRepository)
            {
                menu.AddItem(new GUIContent(GitText.Get("account.menu")), false, () => _ = GitAccountDialog.ShowAsync(_services, null));
            }
            menu.AddItem(new GUIContent(GitText.Get("common.vcsSettings")), false, GitPreferences.Open);
        }

        /// <summary>
        /// 테마 시트는 창 내용(템플릿) 루트에 붙인다 — 창 USS 와 같은 높이라 토큰이 모든 탭·목록 줄에 닿는다.
        /// 테마가 시스템일 때 에디터 스킨(어둡게·밝게)을 바꾸면 창에 포커스가 돌아올 때 맞춘다.
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
            // 브랜치 탐색기 모양은 개인 설정이다. GitKraken 배치는 가로 브랜치 그래프로 고정(GitExplorerStyle.Effective).
            AddView(GitExplorerStyle.Effective == ExplorerStyle.CommitList
                ? new CommitHistoryView(_services, _state.Explorer, this)
                : new BranchExplorerView(_services, _state.Explorer, this), content, sidebar);
            AddView(new WorkspaceView(_services, _state.Workspace), content, sidebar);
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
        /// 아니면(작업 공간, 또는 목록에 없는 커밋) 그래프 탭(GitKraken 형태면 Graph, 아니면 Branch Explorer)으로 넘어가서 보인다.
        /// </summary>
        private void Reveal(string hash)
        {
            if (_views.Find(view => view.IsActive) is ICommitRevealer active && active.RevealCommit(hash))
            {
                return;
            }
            GitView target = (GitView)_views.OfType<GraphWorkspaceView>().FirstOrDefault() ?? _views.OfType<ExplorerView>().FirstOrDefault();
            if (target == null)
            {
                return;
            }
            SelectTab(_views.IndexOf(target));
            ((ICommitRevealer)target).RevealCommit(hash);
        }

        private T AddDrawer<T>(T drawer) where T : SideDrawer
        {
            _drawers.Add(drawer);
            drawer.Opening += opening =>
            {
                // 다른 서랍이 열려 있으면 미끄러짐 없이 바꿔 낀다(나가는 서랍과 들어오는 서랍이 같은 자리에서 겹쳐 움직이지 않게).
                foreach (SideDrawer other in _drawers)
                {
                    if (other != opening && other.IsOpen)
                    {
                        other.CloseInstantly();
                        opening.OpenNextInstantly();
                    }
                }
            };
            return drawer;
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
                ExplorerView => ActionGlyphKind.Branch,
                WorkspaceView => ActionGlyphKind.Folder,
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

        /// <summary>
        /// 충돌 안내의 "충돌 보기": 지금 레이아웃의 변경 탭(GitKraken 작업 공간·Fork 로컬 변경·Unity VCS 대기 중인 변경)을 열고 첫 충돌 파일을 고른다.
        /// </summary>
        private void ShowConflicts()
        {
            int index = _views.FindIndex(view => view.ShowsChanges);
            if (index < 0)
            {
                return;
            }
            SelectTab(index);
            _views[index].ShowConflicts();
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

        /// <summary>스켈레톤으로 덮여 있는 탭(같은 탭을 두 번 덮지 않게)과, 사이드바도 함께 덮는 중인지.</summary>
        private readonly HashSet<GitView> _covering = new();
        private bool _sidebarCovering;

        /// <summary>
        /// 처음 읽기가 오래 걸려도 이 시간(초)이 지나면 스켈레톤을 걷는다. 컴파일 직후에는 에디터가 몇 초 멈추고 git 연동도 그 뒤에 준비되므로 넉넉히 둔다.
        /// </summary>
        private const double CoverTimeout = 20.0;

        /// <summary>
        /// 탭의 칸(<see cref="GitView.SkeletonHosts"/>)을 스켈레톤으로 덮고, 탭이 처음 읽기를 마치면(사이드바도 함께면 사이드바까지) 한꺼번에 걷는다.
        /// 한쪽만 먼저 채워지고 다른 쪽은 로딩 중인 어색함이 없게 — 모든 칸이 같이 물결치다 같이 드러난다.
        /// </summary>
        private void CoverWhileLoading(GitView view, bool withSidebar)
        {
            if (_covering.Add(view) == false)
            {
                return;
            }
            RefSidebar refs = withSidebar ? _refs : null;
            if (refs != null)
            {
                _sidebarCovering = true;
            }
            List<VisualElement> covers = view.SkeletonHosts.Select(host => GitSkeleton.Cover(host.Host, host.Shape)).ToList();
            double started = EditorApplication.timeSinceStartup;
            IVisualElementScheduledItem poll = null;
            poll = view.Root.schedule.Execute(() =>
            {
                bool ready = view.HasLoaded && (refs == null || refs.HasLoaded);
                if (ready == false && EditorApplication.timeSinceStartup - started < CoverTimeout)
                {
                    return;
                }
                poll.Pause();
                _covering.Remove(view);
                if (refs != null && refs == _refs)
                {
                    _sidebarCovering = false;
                    refs.Reveal();
                }
                covers.ForEach(GitSkeleton.Uncover);
            }).Every(50);
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
                    // 창을 처음 만든 뒤 처음 보이는 탭은 사이드바와 함께, 그 뒤 아직 읽지 않은 탭은 그 칸만 스켈레톤으로 덮는다.
                    if (_refs != null && _refs.HoldReveal && _sidebarCovering == false)
                    {
                        CoverWhileLoading(_views[i], withSidebar: true);
                    }
                    else if (_views[i].HasLoaded == false)
                    {
                        CoverWhileLoading(_views[i], withSidebar: false);
                    }
                }
                else if (selected == false && _views[i].IsActive)
                {
                    _views[i].Deactivate();
                }
            }
            _rail.SetSelectedView(index);
            _state.SelectedTab = index;
        }

        private void OnNotReadyAction()
        {
            if (string.IsNullOrEmpty(_services.Repository.LastError))
            {
                _ = InitRepositoryAsync();
            }
            else
            {
                GitPreferences.Open();
            }
        }

        /// <summary>
        /// 이 프로젝트 폴더에 git 저장소를 만든다. .gitignore 가 없으면 Unity 용을 함께 만든다(<see cref="GitInit"/>).
        /// </summary>
        private async System.Threading.Tasks.Task InitRepositoryAsync()
        {
            string root = GitRepository.ProjectRoot;
            string message = GitText.Format("init.message", root)
                             + GitText.Get(GitInit.HasGitignore(root) ? "init.keepGitignore" : "init.addGitignore");
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("init.title"), message, GitText.Get("common.create"), icon: ActionGlyphKind.Branch) == false)
            {
                return;
            }
            _isInitializing = true;
            _notReadyAction.SetEnabled(false);
            (GitResult result, string gitignoreError) = await GitInit.CreateRepositoryAsync(root);
            _isInitializing = false;
            _notReadyAction.SetEnabled(true);
            if (result.Success == false)
            {
                await _services.Dialogs.AlertAsync(GitText.Get("init.failed"), result.Message, ActionGlyphKind.Branch);
                return;
            }
            GitRepository.RequestRefresh(immediate: true);
            if (gitignoreError != null)
            {
                await _services.Dialogs.AlertAsync(GitText.Get("init.title"), GitText.Format("init.gitignoreFailed", gitignoreError), ActionGlyphKind.Branch);
            }
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
            // 저장소가 아니면 여기서 바로 git init, git 을 못 찾는 등 오류면 git 경로가 있는 Preferences 를 연다.
            _notReadyAction.SetVisible(ready == false && repository.IsRunning);
            _notReadyAction.text = string.IsNullOrEmpty(repository.LastError) ? "git init" : GitText.Get("common.openVcsSettings");
            _notReadyAction.SetEnabled(_isInitializing == false);

            _toolbar.Render();
            _operationBanner.Render();
            if (ready == false)
            {
                return;
            }
            foreach (GitView view in _views)
            {
                view.OnRepositoryChanged();
            }
            // 사이드바·stash 개수·브랜치 목록 미리 읽기는 git 을 열 개 넘게 띄운다. 보이는 뷰(그래프)의 git 과 같은 순간에 띄우면
            // 프로세스 띄우기가 서로 줄을 서 둘 다 늦어진다. 뷰의 git 이 끝난 뒤(최대 SidebarWaitMs) 시작한다.
            _sidebarRefreshFull |= repository.LastRefreshWasWorkingTreeOnly == false;
            _sidebarRefreshSince = EditorApplication.timeSinceStartup;
            _sidebarRefresh ??= rootVisualElement.schedule.Execute(RefreshSidebarWhenIdle).Every(SidebarPollMs);
            _sidebarRefresh.Resume();
        }

        private const long SidebarPollMs = 30;
        private const double SidebarWaitSeconds = 1.0;
        private IVisualElementScheduledItem _sidebarRefresh;
        private bool _sidebarRefreshFull;
        private double _sidebarRefreshSince;

        private void RefreshSidebarWhenIdle()
        {
            if (GitCommand.IsRunning && EditorApplication.timeSinceStartup - _sidebarRefreshSince < SidebarWaitSeconds)
            {
                return;
            }
            _sidebarRefresh.Pause();
            if (_refs == null)
            {
                return;
            }
            // stash 개수는 stash·pop·전환 같은 전체 새로고침 작업에서만 바뀐다. Stage·파일 저장 뒤에는 git 을 또 부르지 않는다.
            if (_sidebarRefreshFull)
            {
                _toolbar.RefreshStashCount();
                // 작업 막대 브랜치 목록을 뒤에서 읽어 둔다 — 누르자마자 다 그린 목록이 보이게.
                _ = GitBranchPicker.Prefetch();
            }
            _sidebarRefreshFull = false;
            _refs.OnRepositoryChanged();
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
