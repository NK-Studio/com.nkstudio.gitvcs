using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 원격 추가 서랍(GitKraken 의 Add Remote). 사이드바 리모트 머리 줄의 + 로 연다.
    /// 탭은 둘: URL(이름 · Pull URL · Push URL)과 GitHub(로그인한 계정의 저장소를 골라 이름만 정함). GitLab 등 다른 호스트는 URL 탭으로 넣는다.
    /// 더한 뒤에는 그 원격만 fetch 하고 닫는다 — 사이드바는 작업이 끝나면 스스로 다시 읽는다(<see cref="RefSidebar"/>).
    /// </summary>
    public sealed class AddRemoteDrawer : SideDrawer
    {
        private const string TabSelectedModifier = Block + "__tab--selected";

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

        private List<string> _existing = new();
        private bool _gitHubNameEdited;
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

            // ── GitHub ──
            _gitHubPage = Section(Body);
            _signedOut = SignInPrompt(_gitHubPage, GitText.Get("remote.signInMessage"));
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
            _gitHubPage.Add(_signedIn);

            SelectTab(gitHub: false);
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
            _signedIn.SetVisible(GitHubAccount.IsSignedIn);
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
            Validate();
            if (GitHubAccount.IsSignedIn && _repositoriesLoaded == false)
            {
                LoadRepositories();
            }
        }

        protected override void FocusFirstField()
        {
            if (_gitHubPage.resolvedStyle.display == DisplayStyle.Flex && GitHubAccount.IsSignedIn)
            {
                _repository.Focus();
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
            if (gitHub && GitHubAccount.IsSignedIn && _repositoriesLoaded == false && IsOpen)
            {
                LoadRepositories();
            }
            Validate();
            Root.schedule.Execute(FocusFirstField);
        }

        private async void LoadRepositories()
        {
            _repositoriesLoaded = true;
            _repositoriesLoading = true;
            _repositoriesError = null;
            UpdateRepoStatus();
            (List<GitHubRepository> repositories, string error) = await GitHubRepositories.ListAsync();
            _repositoriesLoading = false;
            _repositoriesError = error;
            // 실패했으면 다음에 열 때 다시 시도한다.
            _repositoriesLoaded = error == null;
            _repository.SetItems(repositories);
            UpdateRepoStatus();
        }

        /// <summary>
        /// 목록 아래 한 줄: 읽는 중 · 오류 · 맞는 저장소 없음. 그 밖에는 숨긴다.
        /// </summary>
        private void UpdateRepoStatus()
        {
            Show(_repoStatus, _repositoriesLoading ? GitText.Get("common.loading")
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
            Show(_urlProblem, NameProblemText(urlProblem, "remote.problemExists"));
            _urlAdd.SetEnabled(busy == false && urlProblem == BranchNameProblem.None && string.IsNullOrWhiteSpace(_pullUrl.value) == false);

            BranchNameProblem gitHubProblem = GitRemoteForm.CheckName(_gitHubName.value?.Trim(), _existing);
            Show(_gitHubProblem, NameProblemText(gitHubProblem, "remote.problemExists"));
            _gitHubAdd.SetEnabled(busy == false && gitHubProblem == BranchNameProblem.None && _repository.Selected != null);
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
