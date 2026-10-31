using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>Changesets 탭: <c>git log</c> 목록과, 고른 커밋의 메시지·바뀐 파일.</summary>
    public sealed class ChangesetsView : GitView, ICommitRevealer
    {
        private const int PageSize = 200;

        private readonly ChangesetsState _state;
        private readonly ToolbarToggle _allBranches;
        private readonly Label _summary;
        private readonly ToolbarButton _moreButton;
        private readonly VisualElement _errorNotice;
        private readonly Label _errorText;
        private readonly VisualElement _emptyNotice;
        private readonly VisualElement _split;
        private readonly CommitList _commits;
        private readonly CommitDetailPanel _detail;
        private readonly CommitMenu _commitMenu;
        private readonly ITabNavigator _navigator;

        private List<GitCommitInfo> _loaded = new();

        /// <summary>
        /// 커밋 메뉴가 그 커밋을 가리키는 브랜치를 찾을 때 쓴다(<see cref="CommitMenu.Hooks.Branches"/>).
        /// </summary>
        private List<GitBranchRef> _branches = new();
        private string _loadedKey;

        public override bool HasLoaded => _loadedKey != null && _isLoading == false;

        public override System.Collections.Generic.IEnumerable<(VisualElement Host, SkeletonShape Shape)> SkeletonHosts
        {
            get
            {
                // ListView 자체가 아니라 감싸는 칸 — ListView 는 contentContainer 가 없어 덮개를 자식으로 받지 못한다.
                yield return (Root.Require<VisualElement>("list-host"), SkeletonShape.Commits);
            }
        }
        private bool _isLoading;
        private bool _hasMore;
        private string _error;

        public override string Title => GitText.Get("tab.changesets");

        private bool HasHistory => Services.Repository.HeadCommit != null || _state.AllBranches;
        private string CurrentKey => $"{Services.Repository.HeadCommit}|{(_state.AllBranches ? Services.Repository.UpstreamCommit + "|" + Services.Repository.LastFetchTime + "|" + Services.Repository.RefsFingerprint : string.Empty)}|{_state.AllBranches}";

        public ChangesetsView(GitServices services, ChangesetsState state, ITabNavigator navigator) : base(services, "ChangesetsView")
        {
            _state = state;
            _navigator = navigator;
            _commitMenu = new CommitMenu(services);
            _allBranches = Root.Require<ToolbarToggle>("all-branches");
            _summary = Root.Require<Label>("summary");
            _moreButton = Root.Require<ToolbarButton>("more-button");
            _errorNotice = Root.Require<VisualElement>("error-notice");
            _errorText = Root.Require<Label>("error-text");
            _emptyNotice = Root.Require<VisualElement>("empty-notice");
            _split = Root.Require<VisualElement>("split");

            _commits = new CommitList(Root.Require<ListView>("commit-list"), services.Assets) { ShowCopyItems = false };
            _detail = new CommitDetailPanel(Root.Require<VisualElement>("detail-host"), services, closable: false);
            _detail.ShowPlaceholder();

            _allBranches.SetValueWithoutNotify(state.AllBranches);
            _allBranches.RegisterValueChangedCallback(evt =>
            {
                _state.AllBranches = evt.newValue;
                ReloadIfNeeded();
            });
            _moreButton.text = GitText.Format("changesets.more", PageSize);
            _moreButton.clicked += () => Load(append: true);
            _commits.SelectionChanged += OnCommitSelected;
            _commits.ContextMenuRequested += PopulateCommitMenu;
        }

        /// <summary>
        /// Branch Explorer 와 같은 커밋 메뉴(<see cref="CommitMenu"/>). 부모로 이동은 이미 읽은 목록 안에 부모가 있을 때만 된다.
        /// </summary>
        private void PopulateCommitMenu(DropdownMenu menu, GitCommitInfo commit)
        {
            GitCommitInfo parent = commit.Parents.Length == 0 ? null : _loaded.Find(c => c.Hash == commit.Parents[0]);
            _commitMenu.Populate(menu, commit, new CommitMenu.Hooks
            {
                GoToParent = parent == null ? null : () => _commits.Select(parent),
                ShowInExplorer = () => _navigator.SelectTab<ExplorerView>()?.Reveal(commit.Hash),
                Branches = _branches,
            });
        }

        /// <summary>
        /// 이미 읽은 목록에 있으면 고른다. 없으면 false — 창이 그래프 탭으로 넘긴다.
        /// </summary>
        public bool RevealCommit(string hash)
        {
            GitCommitInfo commit = _loaded.Find(c => c.Hash == hash);
            if (commit == null)
            {
                return false;
            }
            _commits.Select(commit);
            return true;
        }

        protected override void OnActivated()
        {
            ReloadIfNeeded();
            Render();
        }

        public override void OnRepositoryChanged()
        {
            if (IsActive)
            {
                ReloadIfNeeded();
            }
        }

        public override void Reload()
        {
            if (_isLoading == false)
            {
                Load(append: false);
            }
        }

        private void ReloadIfNeeded()
        {
            if (_isLoading == false && _loadedKey != CurrentKey)
            {
                Load(append: false);
            }
        }

        private async void Load(bool append)
        {
            string key = CurrentKey;
            if (HasHistory == false)
            {
                _loaded = new List<GitCommitInfo>();
                _commits.SetItems(_loaded);
                _loadedKey = key;
                _hasMore = false;
                _detail.ShowPlaceholder();
                Render();
                return;
            }

            _isLoading = true;
            Render();
            int skip = append ? _loaded.Count : 0;
            Task<(List<GitBranchRef> branches, string error)> branchTask = append ? null : Services.History.BranchesAsync();
            (List<GitCommitInfo> commits, string error) = await Services.History.LogAsync(null, _state.AllBranches, skip, PageSize);
            if (branchTask != null)
            {
                _branches = (await branchTask).branches;
            }
            _isLoading = false;

            _error = error;
            _hasMore = commits.Count == PageSize;
            if (append)
            {
                _loaded.AddRange(commits);
            }
            else
            {
                _loaded = commits;
            }
            _loadedKey = key;
            _commits.SetItems(_loaded, c => c.Hash);
            if (_commits.SelectedItem == null)
            {
                _detail.ShowPlaceholder();
            }
            Render();
            // 기다리는 동안 저장소가 또 바뀌었으면 한 번 더 읽는다.
            ReloadIfNeeded();
        }

        private void OnCommitSelected(GitCommitInfo commit)
        {
            if (commit == null)
            {
                _detail.ShowPlaceholder();
            }
            else if (_detail.ShownHash != commit.Hash)
            {
                _detail.Show(commit);
            }
        }

        private void Render()
        {
            _summary.text = _isLoading ? GitText.Get("common.loading") : GitText.Format("changesets.summary", _loaded.Count, _hasMore ? "+" : string.Empty);
            _moreButton.SetEnabled(_isLoading == false && _hasMore);
            // 더 읽을 것이 없으면 숨긴다(꺼진 버튼은 눌러도 되는 것처럼 보였다).
            _moreButton.SetVisible(_hasMore);
            _commits.SetLoading(_isLoading);
            _errorNotice.SetVisible(string.IsNullOrEmpty(_error) == false);
            _errorText.text = _error ?? string.Empty;
            _emptyNotice.SetVisible(HasHistory == false);
            _split.SetVisible(HasHistory);
        }
    }
}
