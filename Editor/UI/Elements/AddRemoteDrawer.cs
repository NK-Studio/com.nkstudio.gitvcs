using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 원격 추가 서랍(GitKraken 의 Add Remote). 사이드바 리모트 머리 줄의 + 로 연다.
    /// 탭은 둘: URL(이름 · Pull URL · Push URL)과 GitHub. GitHub 탭 위쪽에서 [기존 저장소 | 새로 만들기] 를 고른다 —
    /// 기존 저장소는 로그인한 계정의 저장소를 골라 이름만 정하고, 새로 만들기는 GitHub 에 빈 저장소를 만들어 원격으로 더한 뒤
    /// 원하면 현재 브랜치를 바로 올린다(GitHub Desktop 의 Publish repository). GitLab 등 다른 호스트는 URL 탭으로 넣는다.
    /// 더한 뒤에는 그 원격만 fetch 하고 닫는다 — 사이드바는 작업이 끝나면 스스로 다시 읽는다(<see cref="RefSidebar"/>).
    /// </summary>
    public sealed class AddRemoteDrawer : SideDrawer
    {
        private const string TabSelectedModifier = Block + "__tab--selected";

        private const string ModeCheckedModifier = "git-diff__bar-button--checked";

        private readonly Button _urlTab;
        private readonly Button _gitHubTab;
        private readonly VisualElement _urlPage;
        private readonly VisualElement _gitHubPage;

        private readonly TextField _urlName;
        private readonly TextField _pullUrl;
        private readonly TextField _pushUrl;
        private readonly Label _urlProblem;
        private readonly Button _urlAdd;

        private readonly VisualElement _signedOut;
        private readonly VisualElement _signedIn;
        private readonly SearchSelect<GitHubRepository> _repository;
        private readonly Label _repoStatus;
        private readonly TextField _gitHubName;
        private readonly Label _gitHubProblem;
        private readonly Button _gitHubAdd;

        /// <summary>로그인했을 때 GitHub 탭 내용: 고르기 묶음 + 기존 저장소(<see cref="_signedIn"/>) 또는 새로 만들기(<see cref="_createPanel"/>).</summary>
        private readonly VisualElement _gitHubSignedIn;
        private readonly Button _existingMode;
        private readonly Button _createMode;
        private readonly VisualElement _createPanel;
        private readonly SearchSelect<string> _owner;
        private readonly TextField _repoName;
        private readonly Label _repoNameProblem;
        private readonly Toggle _private;
        private readonly TextField _createRemoteName;
        private readonly Label _createRemoteProblem;
        private readonly Toggle _pushAfter;

        /// <summary>"만든 뒤 올리기" 를 켰을 때 보이는 칸들: 기본 브랜치 · 현재 브랜치 · 다른 브랜치 · 태그.</summary>
        private readonly VisualElement _pushOptions;
        private readonly SearchSelect<string> _defaultBranch;
        private readonly Toggle _pushCurrent;
        private readonly Toggle _pushOthers;
        private readonly Toggle _pushTags;
        private readonly Label _createNote;
        private readonly Button _createButton;

        private List<string> _existing = new();
        private bool _gitHubNameEdited;
        private bool _createRemoteNameEdited;

        /// <summary>GitHub 탭에서 "새로 만들기" 를 고른 상태.</summary>
        private bool _isCreating;

        /// <summary>새 저장소 탭의 소유자 목록을 읽었는지(내 계정 + 조직).</summary>
        private bool _ownersLoaded;

        /// <summary>이 저장소의 로컬 브랜치(새 저장소에 올릴 후보)와 태그 수.</summary>
        private List<string> _localBranches = new();

        /// <summary>이미 다른 원격을 추적하는 로컬 브랜치. 새 저장소에 올려도 추적 원격은 바꾸지 않는다.</summary>
        private HashSet<string> _trackedBranches = new();
        private int _tagCount;

        /// <summary>이 계정이 볼 수 있는 저장소(같은 이름 검사에 쓴다).</summary>
        private List<GitHubRepository> _knownRepositories = new();

        /// <summary>
        /// 이번에 GitHub 에 만든 저장소. 만든 뒤 원격 추가가 실패해 다시 누르면 또 만들지 않고 이것을 쓴다.
        /// </summary>
        private GitHubRepository _created;
        private bool _repositoriesLoaded;
        private bool _repositoriesLoading;
        private string _repositoriesError;

        public AddRemoteDrawer(VisualElement host, GitServices services)
            : base(host, services, ActionGlyphKind.Cloud, GitText.Get("remote.addTitle"))
        {
            VisualElement tabs = new();
            tabs.AddToClassList(Block + "__tabs");
            _urlTab = MakeTab(ActionGlyphKind.Globe, GitText.Get("remote.tabUrl"), () => SelectTab(gitHub: false));
            _gitHubTab = MakeTab(ActionGlyphKind.GitHub, GitText.Get("remote.tabGitHub"), () => SelectTab(gitHub: true));
            tabs.Add(_urlTab);
            tabs.Add(_gitHubTab);
            Body.Add(tabs);

            // ── URL ──
            _urlPage = Section(Body);
            _urlName = Field(_urlPage, GitText.Get("remote.name"), null);
            _pullUrl = Field(_urlPage, GitText.Get("remote.pullUrl"), null);
            _pushUrl = Field(_urlPage, GitText.Get("remote.pushUrl"), GitText.Get("remote.pushUrlPlaceholder"));
            _urlProblem = Problem(_urlPage);
            _urlAdd = PrimaryButton(_urlPage, GitText.Get("remote.add"), AddFromUrl);
            _urlName.RegisterValueChangedCallback(_ => Validate());
            _pullUrl.RegisterValueChangedCallback(_ => Validate());
            _pushUrl.RegisterValueChangedCallback(_ => Validate());

            // ── GitHub ──
            _gitHubPage = Section(Body);
            _signedOut = SignInPrompt(_gitHubPage, GitText.Get("remote.signInMessage"));
            _gitHubSignedIn = new VisualElement();
            // 기존 저장소에 연결할지 새로 만들지 먼저 고른다(diff 막대의 칸 묶음과 같은 모양).
            _existingMode = ModeButton(GitText.Get("remote.modeExisting"), () => SelectGitHubMode(create: false));
            _createMode = ModeButton(GitText.Get("remote.modeCreate"), () => SelectGitHubMode(create: true));
            VisualElement modes = new();
            modes.AddToClassList("git-diff__segment");
            modes.AddToClassList("git-diff__segment--leading");
            modes.AddToClassList(Block + "__modes");
            _createMode.AddToClassList("git-diff__segment-button");
            _existingMode.AddToClassList("git-diff__segment-button");
            _existingMode.AddToClassList("git-diff__segment-button--first");
            modes.Add(_existingMode);
            modes.Add(_createMode);
            _gitHubSignedIn.Add(modes);
            _signedIn = new VisualElement();
            Caption(_signedIn, GitText.Get("remote.gitHubRepo"));
            _repository = new SearchSelect<GitHubRepository>(GitText.Get("drawer.select"), r => r.FullName, r => r.IsPrivate ? GitText.Get("remote.private") : null);
            _repository.Changed += OnRepositoryChosen;
            _repository.Filtered += UpdateRepoStatus;
            _signedIn.Add(_repository.Root);
            _repoStatus = Note(_signedIn);
            _gitHubName = Field(_signedIn, GitText.Get("remote.name"), null);
            // 코드가 채울 때는 SetValueWithoutNotify 라 이 콜백은 사용자가 고친 때만 온다.
            _gitHubName.RegisterValueChangedCallback(_ =>
            {
                _gitHubNameEdited = true;
                Validate();
            });
            _gitHubProblem = Problem(_signedIn);
            _gitHubAdd = PrimaryButton(_signedIn, GitText.Get("remote.add"), AddFromGitHub);
            _gitHubSignedIn.Add(_signedIn);

            // ── GitHub · 새로 만들기 ──
            _createPanel = new VisualElement();
            Caption(_createPanel, GitText.Get("remote.owner"));
            _owner = new SearchSelect<string>(GitText.Get("drawer.select"), owner => owner,
                owner => string.Equals(owner, GitHubAccount.Login, StringComparison.OrdinalIgnoreCase) ? GitText.Get("remote.ownerMe") : null);
            _owner.Changed += _ => Validate();
            _createPanel.Add(_owner.Root);
            _repoName = Field(_createPanel, GitText.Get("remote.repoName"), null);
            _repoName.RegisterValueChangedCallback(_ => Validate());
            _repoNameProblem = Problem(_createPanel);
            // 앞쪽 label 대신 text 를 쓰면 GitKraken 처럼 체크 칸이 글자 앞에 온다.
            _private = new Toggle { text = GitText.Get("remote.createPrivate"), value = true };
            _private.AddToClassList(Block + "__toggle");
            _createPanel.Add(_private);
            _createRemoteName = Field(_createPanel, GitText.Get("remote.remoteName"), null);
            _createRemoteName.RegisterValueChangedCallback(_ =>
            {
                _createRemoteNameEdited = true;
                Validate();
            });
            _createRemoteProblem = Problem(_createPanel);
            // 개발 중인 프로젝트는 지금 작업 브랜치(feature/…)에 있기 쉽다. 빈 저장소에 처음 올린 브랜치가 GitHub 기본 브랜치가 되므로
            // 기본 브랜치(main·master)를 따로 고르게 하고 언제나 그것부터 올린다(GitRemoteForm.PublishBranches).
            _pushAfter = new Toggle { text = GitText.Get("remote.createPush"), value = true };
            _pushAfter.AddToClassList(Block + "__toggle");
            _pushAfter.RegisterValueChangedCallback(_ => Validate());
            _createPanel.Add(_pushAfter);
            _pushOptions = new VisualElement();
            _pushOptions.AddToClassList(Block + "__suboptions");
            Caption(_pushOptions, GitText.Get("remote.defaultBranch"));
            _defaultBranch = new SearchSelect<string>(GitText.Get("drawer.select"), branch => branch,
                branch => branch == GitRepository.Branch?.Name ? GitText.Get("remote.currentBranch") : null);
            _defaultBranch.Changed += _ => Validate();
            _pushOptions.Add(_defaultBranch.Root);
            Show(Note(_pushOptions), GitText.Get("remote.defaultBranchNote"));
            _pushCurrent = OptionToggle(_pushOptions, true);
            _pushOthers = OptionToggle(_pushOptions, false);
            _pushTags = OptionToggle(_pushOptions, false);
            _createPanel.Add(_pushOptions);
            _createNote = Note(_createPanel);
            _createButton = PrimaryButton(_createPanel, GitText.Get("remote.create"), CreateOnGitHub);
            _gitHubSignedIn.Add(_createPanel);
            _gitHubPage.Add(_gitHubSignedIn);

            SelectGitHubMode(create: false);
            SelectTab(gitHub: false);
        }

        /// <summary>
        /// 원격이 없어 Push 를 못 할 때(<see cref="GitServices.AddRemoteRequested"/>) 연다: GitHub 탭의 "새로 만들기" 로 —
        /// 아직 GitHub 에 없는 프로젝트를 올리려는 경우가 대부분이고, 있는 저장소는 바로 옆 "기존 저장소" 로 고를 수 있다.
        /// 로그인하지 않았으면 GitHub 탭이 로그인 안내를 보인다.
        /// </summary>
        public void OpenForPublish()
        {
            Open();
            SelectGitHubMode(create: true);
            SelectTab(gitHub: true);
        }

        private Toggle OptionToggle(VisualElement parent, bool value)
        {
            Toggle toggle = new() { value = value };
            toggle.AddToClassList(Block + "__toggle");
            toggle.RegisterValueChangedCallback(_ => Validate());
            parent.Add(toggle);
            return toggle;
        }

        private static Button ModeButton(string text, Action clicked)
        {
            Button button = new(clicked) { text = text };
            button.AddToClassList("git-diff__bar-button");
            button.AddToClassList(Block + "__mode");
            return button;
        }

        /// <summary>GitHub 탭에서 기존 저장소 고르기와 새로 만들기 사이를 바꾼다.</summary>
        private void SelectGitHubMode(bool create)
        {
            _isCreating = create;
            _existingMode.EnableInClassList(ModeCheckedModifier, create == false);
            _createMode.EnableInClassList(ModeCheckedModifier, create);
            _signedIn.SetVisible(create == false);
            _createPanel.SetVisible(create);
            if (create && GitHubAccount.IsSignedIn && IsOpen && _ownersLoaded == false)
            {
                LoadOwners();
            }
            Validate();
            Root.schedule.Execute(FocusFirstField);
        }

        protected override void Reset()
        {
            _urlName.SetValueWithoutNotify(string.Empty);
            _pullUrl.SetValueWithoutNotify(string.Empty);
            _pushUrl.SetValueWithoutNotify(string.Empty);
            _repository.Select(null);
            _gitHubName.SetValueWithoutNotify(string.Empty);
            _gitHubNameEdited = false;
            _signedOut.SetVisible(GitHubAccount.IsSignedIn == false);
            _gitHubSignedIn.SetVisible(GitHubAccount.IsSignedIn);
            SelectGitHubMode(create: false);

            _created = null;
            _repoName.SetValueWithoutNotify(GitRemoteForm.SuggestRepositoryName(System.IO.Path.GetFileName((GitRepository.RepoRoot ?? GitRepository.ProjectRoot ?? string.Empty).TrimEnd('/', '\\'))));
            _private.SetValueWithoutNotify(true);
            _createRemoteName.SetValueWithoutNotify(string.Empty);
            _createRemoteNameEdited = false;
            _pushAfter.SetValueWithoutNotify(true);
            _pushCurrent.SetValueWithoutNotify(true);
            _pushOthers.SetValueWithoutNotify(false);
            _pushTags.SetValueWithoutNotify(false);
            _defaultBranch.Select(null);
        }

        protected override async void LoadAfterOpen(int openCount)
        {
            List<string> existing = await RemoteNamesAsync();
            if (IsCurrent(openCount) == false)
            {
                return;
            }
            _existing = existing;
            string suggested = GitRemoteForm.SuggestName(_existing, null);
            if (string.IsNullOrEmpty(_urlName.value))
            {
                _urlName.SetValueWithoutNotify(suggested);
            }
            if (_gitHubNameEdited == false && string.IsNullOrEmpty(_gitHubName.value))
            {
                _gitHubName.SetValueWithoutNotify(suggested);
            }
            if (_createRemoteNameEdited == false && string.IsNullOrEmpty(_createRemoteName.value))
            {
                _createRemoteName.SetValueWithoutNotify(suggested);
            }
            Validate();
            LoadBranches(openCount);
            // 열 때마다 다시 읽는다(방금 GitHub 에서 만든 저장소도 보이게). 읽는 동안에는 지난 목록을 그대로 보인다.
            if (GitHubAccount.IsSignedIn)
            {
                LoadRepositories();
                LoadOwners();
            }
        }

        protected override void FocusFirstField()
        {
            if (_gitHubPage.resolvedStyle.display == DisplayStyle.Flex && GitHubAccount.IsSignedIn)
            {
                if (_isCreating)
                {
                    _repoName.Focus();
                }
                else
                {
                    _repository.Focus();
                }
            }
            else if (_urlPage.resolvedStyle.display == DisplayStyle.Flex)
            {
                _urlName.Focus();
            }
        }

        /// <summary>
        /// 이 저장소의 원격 이름(<c>git remote</c>). 실패하면 빈 목록.
        /// </summary>
        public static async Task<List<string>> RemoteNamesAsync()
        {
            GitResult result = await GitRepository.RunAsync("remote");
            return result.Success
                ? result.Output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToList()
                : new List<string>();
        }

        private Button MakeTab(ActionGlyphKind glyph, string text, Action clicked)
        {
            Button tab = new(clicked);
            tab.AddToClassList(Block + "__tab");
            ActionGlyph icon = new(glyph);
            icon.AddToClassList(Block + "__tab-glyph");
            tab.Add(icon);
            Label label = new(text);
            label.AddToClassList(Block + "__tab-label");
            tab.Add(label);
            return tab;
        }

        private void SelectTab(bool gitHub)
        {
            _urlTab.EnableInClassList(TabSelectedModifier, gitHub == false);
            _gitHubTab.EnableInClassList(TabSelectedModifier, gitHub);
            _urlPage.SetVisible(gitHub == false);
            _gitHubPage.SetVisible(gitHub);
            if (gitHub && GitHubAccount.IsSignedIn && IsOpen)
            {
                if (_repositoriesLoaded == false)
                {
                    LoadRepositories();
                }
                if (_isCreating && _ownersLoaded == false)
                {
                    LoadOwners();
                }
            }
            Validate();
            Root.schedule.Execute(FocusFirstField);
        }

        private async void LoadRepositories()
        {
            if (_repositoriesLoading)
            {
                return;
            }
            _repositoriesLoading = true;
            _repositoriesError = null;
            // 먼저 기억해 둔 목록을 바로 보이고(빈 칸으로 기다리지 않게), 새로 읽은 것으로 바꾼다.
            (List<GitHubRepository> cached, _) = await GitHubRepositories.ListAsync();
            if (cached.Count > 0)
            {
                _repository.SetItems(cached);
                _knownRepositories = cached;
            }
            UpdateRepoStatus();
            (List<GitHubRepository> repositories, string error) = await GitHubRepositories.ListAsync(reload: true);
            _repositoriesLoading = false;
            _repositoriesError = error;
            // 실패했으면 다음에 열 때 다시 시도한다.
            _repositoriesLoaded = error == null;
            _repository.SetItems(repositories);
            _knownRepositories = repositories;
            UpdateRepoStatus();
            Validate();
        }

        /// <summary>
        /// 새 저장소에 올릴 후보: 로컬 브랜치·태그 수, 그리고 처음 고를 기본 브랜치(<see cref="GitRemoteForm.PickDefaultBranch"/>).
        /// </summary>
        private async void LoadBranches(int openCount)
        {
            Task<(List<GitBranchRef> branches, string error)> branchTask = Services.History.BranchesAsync();
            Task<GitResult> configTask = GitRepository.RunAsync("config", "--get", "init.defaultBranch");
            Task<GitResult> tagTask = GitRepository.RunAsync("tag");
            (List<GitBranchRef> branches, _) = await branchTask;
            GitResult config = await configTask;
            GitResult tags = await tagTask;
            if (IsCurrent(openCount) == false)
            {
                return;
            }
            _localBranches = branches.Where(b => b.IsRemote == false).Select(b => b.Name).ToList();
            _trackedBranches = new HashSet<string>(branches.Where(b => b.IsRemote == false && string.IsNullOrEmpty(b.Upstream) == false).Select(b => b.Name));
            _tagCount = tags.Success ? tags.Output.Split('\n').Count(line => line.Trim().Length > 0) : 0;
            _defaultBranch.SetItems(_localBranches);
            GitBranchInfo branch = GitRepository.Branch;
            _defaultBranch.Select(GitRemoteForm.PickDefaultBranch(_localBranches, config.Success ? config.Output.Trim() : null, CanPush(branch) ? branch.Name : null));
            Validate();
        }

        /// <summary>
        /// 새 저장소의 소유자 후보: 내 계정을 맨 앞에, 그 뒤에 속한 조직. 처음에는 내 계정을 고른다.
        /// </summary>
        private async void LoadOwners()
        {
            string login = GitHubAccount.Login;
            if (_owner.Selected == null && string.IsNullOrEmpty(login) == false)
            {
                _owner.SetItems(new[] { login });
                _owner.Select(login);
            }
            List<string> organizations = await GitHubRepositories.OrganizationsAsync();
            List<string> owners = new();
            if (string.IsNullOrEmpty(login) == false)
            {
                owners.Add(login);
            }
            owners.AddRange(organizations.Where(o => owners.Contains(o, StringComparer.OrdinalIgnoreCase) == false));
            string selected = _owner.Selected;
            _owner.SetItems(owners);
            _owner.Select(owners.FirstOrDefault(o => string.Equals(o, selected, StringComparison.OrdinalIgnoreCase)) ?? owners.FirstOrDefault());
            _ownersLoaded = true;
            Validate();
        }

        /// <summary>
        /// 목록 아래 한 줄: 읽는 중 · 오류 · 맞는 저장소 없음. 그 밖에는 숨긴다.
        /// </summary>
        private void UpdateRepoStatus()
        {
            Show(_repoStatus, _repositoriesLoading && _repository.ItemCount == 0 ? GitText.Get("common.loading")
                : _repositoriesError != null ? GitText.Format("remote.repoError", _repositoriesError)
                : _repository.HasMatches == false ? GitText.Get(_repository.ItemCount == 0 ? "remote.repoNone" : "remote.repoEmpty")
                : null);
        }

        private void OnRepositoryChosen(GitHubRepository repository)
        {
            if (repository != null && _gitHubNameEdited == false)
            {
                _gitHubName.SetValueWithoutNotify(GitRemoteForm.SuggestName(_existing, repository.Owner));
            }
            Validate();
            if (repository != null)
            {
                _gitHubName.schedule.Execute(() => _gitHubName.Focus());
            }
        }

        protected override void Validate()
        {
            bool busy = Services.Runner.IsBusy;
            BranchNameProblem urlProblem = GitRemoteForm.CheckName(_urlName.value?.Trim(), _existing);
            // 이름 문제를 먼저 보이고, 이름이 괜찮으면 주소(받기·올리기)를 본다. 빈 주소는 설명 없이 버튼만 끈다.
            bool pullBad = string.IsNullOrWhiteSpace(_pullUrl.value) == false && GitRemoteForm.IsPlausibleUrl(_pullUrl.value, GitRemoteForm.PathExistsFromRepo) == false;
            bool pushBad = string.IsNullOrWhiteSpace(_pushUrl.value) == false && GitRemoteForm.IsPlausibleUrl(_pushUrl.value, GitRemoteForm.PathExistsFromRepo) == false;
            string nameText = NameProblemText(urlProblem, "remote.problemExists");
            Show(_urlProblem, nameText ?? (pullBad || pushBad ? GitText.Get("remote.invalidUrl") : null));
            _urlAdd.SetEnabled(busy == false && urlProblem == BranchNameProblem.None && string.IsNullOrWhiteSpace(_pullUrl.value) == false && pullBad == false && pushBad == false);

            BranchNameProblem gitHubProblem = GitRemoteForm.CheckName(_gitHubName.value?.Trim(), _existing);
            Show(_gitHubProblem, NameProblemText(gitHubProblem, "remote.problemExists"));
            _gitHubAdd.SetEnabled(busy == false && gitHubProblem == BranchNameProblem.None && _repository.Selected != null);

            ValidateCreate(busy);
        }

        /// <summary>
        /// 새로 만들기: 저장소 이름(GitHub 규칙·같은 이름) → 원격 이름 순서로 문제를 보이고, 만들 주소와 올릴 브랜치를 미리 보인다.
        /// 올리기 칸은 있는 것만 보인다: 현재 브랜치가 기본 브랜치와 다를 때만 "현재 브랜치도", 다른 로컬 브랜치가 있을 때만 "다른 브랜치도", 태그가 있을 때만 "태그도".
        /// </summary>
        private void ValidateCreate(bool busy)
        {
            string owner = _owner.Selected;
            string name = _repoName.value?.Trim() ?? string.Empty;
            bool alreadyCreated = _created != null && string.Equals(_created.FullName, owner + "/" + name, StringComparison.OrdinalIgnoreCase);
            GitHubRepoNameProblem nameProblem = alreadyCreated ? GitHubRepoNameProblem.None : GitRemoteForm.CheckRepositoryName(name, owner, _knownRepositories);
            Show(_repoNameProblem, nameProblem switch
            {
                GitHubRepoNameProblem.None or GitHubRepoNameProblem.Empty => null,
                GitHubRepoNameProblem.Exists => GitText.Format("remote.repoNameExists", owner),
                _ => GitText.Get("remote.repoNameProblem" + nameProblem),
            });
            BranchNameProblem remoteProblem = GitRemoteForm.CheckName(_createRemoteName.value?.Trim(), _existing);
            Show(_createRemoteProblem, NameProblemText(remoteProblem, "remote.problemExists"));

            bool canPublish = _localBranches.Count > 0 && GitRepository.HeadCommit != null;
            _pushAfter.SetVisible(canPublish);
            _pushOptions.SetVisible(canPublish && _pushAfter.value);
            string current = CurrentBranchName();
            string defaultBranch = _defaultBranch.Selected;
            bool currentDiffers = current != null && defaultBranch != null && current != defaultBranch;
            _pushCurrent.SetVisible(currentDiffers);
            if (currentDiffers)
            {
                _pushCurrent.text = GitText.Format("remote.pushCurrent", current);
            }
            int others = GitRemoteForm.PublishBranches(defaultBranch, current, true, true, _localBranches).Count - 1 - (currentDiffers ? 1 : 0);
            _pushOthers.SetVisible(others > 0);
            _pushOthers.text = GitText.Format("remote.pushOthers", others);
            _pushTags.SetVisible(_tagCount > 0);
            _pushTags.text = GitText.Format("remote.pushTags", _tagCount);

            (List<string> branches, bool tags) = PublishPlan();
            bool ready = owner != null && nameProblem == GitHubRepoNameProblem.None && remoteProblem == BranchNameProblem.None
                         && (canPublish == false || _pushAfter.value == false || defaultBranch != null);
            string preview = null;
            if (ready)
            {
                preview = GitText.Format("remote.createPreview", owner, name, _createRemoteName.value.Trim());
                if (branches.Count > 0)
                {
                    string list = branches[0] + GitText.Get("remote.defaultMark") + string.Concat(branches.Skip(1).Select(b => ", " + b));
                    preview += "\n" + GitText.Format("remote.createPreviewPush", list) + (tags ? GitText.Format("remote.createPreviewTags", _tagCount) : string.Empty);
                }
            }
            Show(_createNote, preview);
            _createButton.SetEnabled(busy == false && GitHubAccount.IsSignedIn && ready);
        }

        /// <summary>현재 브랜치 이름(커밋이 있는 브랜치일 때만).</summary>
        private static string CurrentBranchName()
        {
            GitBranchInfo branch = GitRepository.Branch;
            return CanPush(branch) ? branch.Name : null;
        }

        /// <summary>지금 고른 대로 올릴 브랜치(기본 브랜치가 맨 앞)와 태그를 올릴지.</summary>
        private (List<string> branches, bool tags) PublishPlan()
        {
            bool canPublish = _localBranches.Count > 0 && GitRepository.HeadCommit != null;
            if (canPublish == false || _pushAfter.value == false || _defaultBranch.Selected == null)
            {
                return (new List<string>(), false);
            }
            string current = CurrentBranchName();
            bool includeCurrent = current != null && current != _defaultBranch.Selected && _pushCurrent.value;
            List<string> branches = GitRemoteForm.PublishBranches(_defaultBranch.Selected, current, includeCurrent, _pushOthers.value, _localBranches);
            return (branches, _tagCount > 0 && _pushTags.value);
        }

        private void AddFromUrl()
        {
            Add(_urlName.value.Trim(), _pullUrl.value.Trim(), _pushUrl.value?.Trim());
        }

        private void AddFromGitHub()
        {
            if (_repository.Selected != null)
            {
                Add(_gitHubName.value.Trim(), _repository.Selected.CloneUrl, null);
            }
        }

        /// <summary>바로 올릴 브랜치가 있는지: 커밋이 있는 브랜치(detached 아님).</summary>
        private static bool CanPush(GitBranchInfo branch)
        {
            return branch != null && branch.Detached == false && branch.HasCommits && string.IsNullOrEmpty(branch.Name) == false;
        }

        /// <summary>
        /// GitHub 에 빈 저장소를 만들고 → 원격으로 더하고(가져오기까지) → 고른 브랜치를 기본 브랜치부터 차례로 올리고 → 고르면 태그도 올린 뒤
        /// → 기본 브랜치를 GitHub 에도 한 번 더 정해 둔다. 한 작업으로 묶어 상태 줄·오류 창을 함께 쓴다.
        /// 추적 원격(upstream)은 아직 없는 브랜치에만 건다 — 이미 다른 원격(origin 등)을 추적하던 브랜치는 그대로 두어 평소 Pull·Push 가 바뀌지 않게 한다.
        /// 만들기는 됐는데 그 뒤가 실패하면 다시 눌렀을 때 또 만들지 않고 방금 만든 저장소를 쓴다(<see cref="_created"/>).
        /// </summary>
        private async void CreateOnGitHub()
        {
            string owner = _owner.Selected;
            if (Services.Runner.IsBusy || owner == null)
            {
                return;
            }
            string name = _repoName.value.Trim();
            string remote = _createRemoteName.value.Trim();
            bool isPrivate = _private.value;
            (List<string> branches, bool pushTags) = PublishPlan();
            HashSet<string> tracked = new(_trackedBranches);
            await Services.Runner.RunAsync(GitText.Format("op.createGitHubRepo", owner + "/" + name), async () =>
            {
                if (_created == null || string.Equals(_created.FullName, owner + "/" + name, StringComparison.OrdinalIgnoreCase) == false)
                {
                    (GitHubRepository repository, string error) = await GitHubRepositories.CreateAsync(owner, name, isPrivate);
                    if (repository == null)
                    {
                        return new GitResult(1, string.Empty, GitText.Format("remote.createFailed", error));
                    }
                    _created = repository;
                }
                GitResult result = await Services.Commands.AddRemoteAsync(remote, _created.CloneUrl, null);
                if (result.Success == false)
                {
                    return result;
                }
                foreach (string branch in branches)
                {
                    result = await Services.Commands.PushBranchAsync(remote, branch, setUpstream: tracked.Contains(branch) == false);
                    if (result.Success == false)
                    {
                        return result;
                    }
                }
                if (pushTags)
                {
                    result = await Services.Commands.PushAllTagsAsync(remote);
                    if (result.Success == false)
                    {
                        return result;
                    }
                }
                if (branches.Count > 1)
                {
                    string error = await GitHubRepositories.SetDefaultBranchAsync(_created.FullName, branches[0]);
                    if (error != null)
                    {
                        UnityEngine.Debug.LogWarning(GitText.Format("remote.setDefaultFailed", branches[0], error));
                    }
                }
                return result;
            }, refreshAssets: false);
            _existing = await RemoteNamesAsync();
            if (_existing.Contains(remote))
            {
                Close();
            }
            else
            {
                Validate();
            }
        }

        private async void Add(string name, string pullUrl, string pushUrl)
        {
            if (Services.Runner.IsBusy)
            {
                return;
            }
            await Services.Runner.RunAsync(GitText.Format("op.addRemote", name), () => Services.Commands.AddRemoteAsync(name, pullUrl, pushUrl), refreshAssets: false);
            // fetch 만 실패해도(인증·네트워크) 원격은 이미 더해졌으니 닫는다. 더하기부터 실패했으면 고칠 수 있게 열어 둔다.
            _existing = await RemoteNamesAsync();
            if (_existing.Contains(name))
            {
                Close();
            }
            else
            {
                Validate();
            }
        }
    }
}
