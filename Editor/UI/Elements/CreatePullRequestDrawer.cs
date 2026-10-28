using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Pull Request 만들기 서랍(GitKraken 의 Create Pull Request). GitHub 만 지원한다. 브랜치 우클릭 "Pull Request 만들기…" 로 연다.
    /// From(보낼 저장소·브랜치) → To(받을 저장소·브랜치)는 이 저장소의 GitHub 원격과 그 원격 브랜치에서 고른다 — PR 은 원격에 올라간 브랜치로만 만들 수 있다.
    /// 기본값: From 은 연 브랜치의 upstream(없으면 origin 의 같은 이름), To 는 upstream 원격이 있으면 그쪽, 브랜치는 원격 HEAD(main·master…).
    /// 제목은 From 브랜치 끝 커밋의 첫 줄로 채운다(고치면 그대로 둔다). 만들면 브라우저에서 그 PR 을 연다.
    /// </summary>
    public sealed class CreatePullRequestDrawer : SideDrawer
    {
        private readonly VisualElement _signedOut;
        private readonly VisualElement _signedIn;
        private readonly SearchSelect<GitHubRemoteRepo> _fromRepo;
        private readonly SearchSelect<string> _fromBranch;
        private readonly SearchSelect<GitHubRemoteRepo> _toRepo;
        private readonly SearchSelect<string> _toBranch;
        private readonly TextField _title;
        private readonly TextField _description;
        private readonly Toggle _draft;
        private readonly Label _note;
        private readonly Label _problem;
        private readonly Button _create;

        private GitRefSet _refs = new();
        private List<GitHubRemoteRepo> _repos = new();
        private GitBranchRef _initialBranch;
        private bool _titleEdited;
        private bool _creating;

        /// <summary>
        /// GitHub 가 거절한 이유. 입력을 고치면 지운다.
        /// </summary>
        private string _serverError;

        /// <summary>
        /// PR 을 만들었을 때. 사이드바가 Pull Requests 구역을 다시 받는다.
        /// </summary>
        public event System.Action Created;

        public CreatePullRequestDrawer(VisualElement host, GitServices services)
            : base(host, services, ActionGlyphKind.PullRequest, GitText.Get("pr.createTitle"))
        {
            _signedOut = SignInPrompt(Body, GitText.Get("pr.signInMessage"));

            _signedIn = Section(Body);
            VisualElement columns = new();
            columns.AddToClassList(Block + "__columns");
            VisualElement from = new();
            from.AddToClassList(Block + "__column");
            Caption(from, GitText.Get("pr.fromRepo"));
            _fromRepo = RepoSelect(from);
            Caption(from, GitText.Get("pr.branch"));
            _fromBranch = BranchSelect(from);
            columns.Add(from);
            Label arrow = new("→");
            arrow.AddToClassList(Block + "__arrow");
            columns.Add(arrow);
            VisualElement to = new();
            to.AddToClassList(Block + "__column");
            Caption(to, GitText.Get("pr.toRepo"));
            _toRepo = RepoSelect(to);
            Caption(to, GitText.Get("pr.branch"));
            _toBranch = BranchSelect(to);
            columns.Add(to);
            _signedIn.Add(columns);

            _fromRepo.Changed += _ =>
            {
                FillBranches(_fromRepo, _fromBranch, PreferredFromBranch());
                FillTitle();
                InputChanged();
            };
            _toRepo.Changed += _ =>
            {
                FillBranches(_toRepo, _toBranch, _toRepo.Selected == null ? null : GitPullRequestForm.DefaultBase(_refs.Branches, _toRepo.Selected.Remote));
                InputChanged();
            };
            _fromBranch.Changed += _ =>
            {
                FillTitle();
                InputChanged();
            };
            _toBranch.Changed += _ => InputChanged();

            _title = Field(_signedIn, GitText.Get("pr.title"), GitText.Get("pr.titlePlaceholder"));
            _title.RegisterValueChangedCallback(_ =>
            {
                _titleEdited = true;
                InputChanged();
            });
            _description = Field(_signedIn, GitText.Get("pr.description"), GitText.Get("pr.descriptionPlaceholder"), multiline: true);
            // 앞쪽 label 대신 text 를 쓰면 GitKraken 처럼 체크 칸이 글자 앞에 온다.
            _draft = new Toggle { text = GitText.Get("pr.draft"), tooltip = GitText.Get("pr.draftTooltip") };
            _draft.AddToClassList(Block + "__toggle");
            _signedIn.Add(_draft);
            _note = Note(_signedIn);
            _problem = Problem(_signedIn);

            VisualElement buttons = new();
            buttons.AddToClassList(Block + "__row");
            buttons.AddToClassList(Block + "__buttons");
            SecondaryButton(buttons, GitText.Get("common.cancel"), Close).AddToClassList(Block + "__grow");
            _create = PrimaryButton(buttons, GitText.Get("pr.create"), Create);
            _create.AddToClassList(Block + "__grow");
            _signedIn.Add(buttons);
        }

        /// <summary>
        /// 그 브랜치를 보낼 쪽으로 채워 연다. null 이면 지금 브랜치.
        /// </summary>
        public void OpenFor(GitBranchRef branch)
        {
            _initialBranch = branch;
            if (IsOpen)
            {
                ApplyDefaults();
                return;
            }
            Open();
        }

        private SearchSelect<GitHubRemoteRepo> RepoSelect(VisualElement parent)
        {
            SearchSelect<GitHubRemoteRepo> select = new(GitText.Get("drawer.select"), r => r.FullName, r => r.Remote);
            parent.Add(select.Root);
            return select;
        }

        private static SearchSelect<string> BranchSelect(VisualElement parent)
        {
            SearchSelect<string> select = new(GitText.Get("drawer.select"), b => b);
            parent.Add(select.Root);
            return select;
        }

        protected override void Reset()
        {
            _fromRepo.Select(null);
            _toRepo.Select(null);
            _fromBranch.Select(null);
            _toBranch.Select(null);
            _title.SetValueWithoutNotify(string.Empty);
            _description.SetValueWithoutNotify(string.Empty);
            _draft.SetValueWithoutNotify(false);
            _titleEdited = false;
            _creating = false;
            _serverError = null;
            Show(_note, null);
            _signedOut.SetVisible(GitHubAccount.IsSignedIn == false);
            _signedIn.SetVisible(GitHubAccount.IsSignedIn);
        }

        protected override async void LoadAfterOpen(int openCount)
        {
            GitRefSet refs = await Services.History.RefsAsync();
            if (IsCurrent(openCount) == false)
            {
                return;
            }
            _refs = refs;
            _repos = GitPullRequestForm.GitHubRemotes(refs.RemoteUrls);
            _fromRepo.SetItems(_repos);
            _toRepo.SetItems(_repos);
            ApplyDefaults();
        }

        protected override void FocusFirstField()
        {
            if (GitHubAccount.IsSignedIn)
            {
                _title.Focus();
            }
        }

        /// <summary>
        /// 연 브랜치(없으면 지금 브랜치)에 맞춰 저장소·브랜치·제목을 다시 채운다.
        /// </summary>
        private void ApplyDefaults()
        {
            GitBranchRef branch = InitialBranch();
            string upstream = branch?.IsRemote == true ? branch.Name : branch?.Upstream;
            string upstreamRemote = string.IsNullOrEmpty(upstream) ? null : GitCommitCommands.SplitRemoteBranch(upstream).remote;
            GitHubRemoteRepo from = _repos.FirstOrDefault(r => r.Remote == upstreamRemote) ?? _repos.FirstOrDefault();
            // 포크에서 일하면 upstream 원격이 받는 쪽이다.
            GitHubRemoteRepo to = _repos.FirstOrDefault(r => r.Remote == "upstream" && r != from) ?? from;
            _fromRepo.Select(from);
            _toRepo.Select(to);
            FillBranches(_fromRepo, _fromBranch, PreferredFromBranch());
            FillBranches(_toRepo, _toBranch, to == null ? null : GitPullRequestForm.DefaultBase(_refs.Branches, to.Remote));
            if (_titleEdited == false)
            {
                _title.SetValueWithoutNotify(string.Empty);
            }
            FillTitle();
            Show(_note, _repos.Count == 0 ? GitText.Get("pr.noGitHubRemote") : PushHint());
            Validate();
        }

        private GitBranchRef InitialBranch()
        {
            return _initialBranch ?? _refs.Branches.FirstOrDefault(b => b.IsCurrent);
        }

        /// <summary>
        /// From 브랜치 기본값: 연 브랜치의 upstream 이 그 원격에 있으면 그것, 아니면 같은 이름.
        /// </summary>
        private string PreferredFromBranch()
        {
            GitBranchRef branch = InitialBranch();
            GitHubRemoteRepo repo = _fromRepo.Selected;
            if (branch == null || repo == null)
            {
                return null;
            }
            string tracked = branch.IsRemote ? branch.Name : branch.Upstream;
            if (string.IsNullOrEmpty(tracked) == false)
            {
                (string remote, string name) = GitCommitCommands.SplitRemoteBranch(tracked);
                if (remote == repo.Remote)
                {
                    return name;
                }
            }
            return branch.IsRemote ? GitCommitCommands.SplitRemoteBranch(branch.Name).branch : branch.Name;
        }

        private void FillBranches(SearchSelect<GitHubRemoteRepo> repo, SearchSelect<string> branch, string preferred)
        {
            List<string> names = repo.Selected == null ? new List<string>() : GitPullRequestForm.RemoteBranches(_refs.Branches, repo.Selected.Remote);
            branch.SetItems(names);
            branch.Select(preferred != null && names.Contains(preferred) ? preferred : null);
        }

        /// <summary>
        /// 연 로컬 브랜치가 고른 원격에 아직 없으면 먼저 Push 하라고 알린다.
        /// </summary>
        private string PushHint()
        {
            GitBranchRef branch = InitialBranch();
            if (branch == null || branch.IsRemote || _fromRepo.Selected == null || _fromBranch.Selected != null)
            {
                return null;
            }
            return GitText.Format("pr.pushFirst", branch.Name, _fromRepo.Selected.Remote);
        }

        private void FillTitle()
        {
            if (_titleEdited || _fromRepo.Selected == null || _fromBranch.Selected == null)
            {
                return;
            }
            string full = _fromRepo.Selected.Remote + "/" + _fromBranch.Selected;
            GitBranchRef tip = _refs.Branches.FirstOrDefault(b => b.IsRemote && b.Name == full);
            _title.SetValueWithoutNotify(tip?.Subject ?? string.Empty);
        }

        private void InputChanged()
        {
            _serverError = null;
            Validate();
        }

        protected override void Validate()
        {
            string problem = null;
            if (_fromRepo.Selected != null && _toRepo.Selected != null && _fromBranch.Selected != null
                && _fromRepo.Selected.FullName == _toRepo.Selected.FullName && _fromBranch.Selected == _toBranch.Selected)
            {
                problem = GitText.Get("pr.problemSameBranch");
            }
            Show(_problem, problem ?? _serverError);
            _create.SetEnabled(GitHubAccount.IsSignedIn && _creating == false && problem == null
                               && _fromRepo.Selected != null && _fromBranch.Selected != null && _toRepo.Selected != null && _toBranch.Selected != null
                               && string.IsNullOrWhiteSpace(_title.value) == false);
        }

        private async void Create()
        {
            GitHubRemoteRepo from = _fromRepo.Selected;
            GitHubRemoteRepo to = _toRepo.Selected;
            string head = _fromBranch.Selected;
            string baseBranch = _toBranch.Selected;
            if (_creating || from == null || to == null || head == null || baseBranch == null)
            {
                return;
            }
            _creating = true;
            _serverError = null;
            Show(_note, GitText.Get("pr.creating"));
            Validate();
            (int number, string url, string error) = await GitHubPullRequests.CreateAsync(to, GitPullRequestForm.HeadSpec(from, to, head), baseBranch,
                _title.value.Trim(), _description.value, _draft.value);
            _creating = false;
            Show(_note, null);
            if (error != null)
            {
                _serverError = GitText.Format("pr.failed", error);
                Validate();
                return;
            }
            Services.Runner.SetStatus(GitText.Format("pr.created", number, to.FullName));
            Created?.Invoke();
            if (string.IsNullOrEmpty(url) == false)
            {
                Application.OpenURL(url);
            }
            Close();
        }
    }
}
