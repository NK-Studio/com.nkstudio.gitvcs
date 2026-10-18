using System.Collections.Generic;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>Changesets 탭: <c>git log</c> 목록과, 고른 커밋의 메시지·바뀐 파일.</summary>
    public sealed class ChangesetsView : GitView
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
        private string _loadedKey;
        private bool _isLoading;
        private bool _hasMore;
        private string _error;

        public override string Title => GitText.Get("tab.changesets");

        private bool HasHistory => Services.Repository.HeadCommit != null || _state.AllBranches;
        private string CurrentKey => $"{Services.Repository.HeadCommit}|{(_state.AllBranches ? Services.Repository.UpstreamCommit + "|" + Services.Repository.LastFetchTime : string.Empty)}|{_state.AllBranches}";

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
                ShowInExplorer = () => _navigator.SelectTab<BranchExplorerView>()?.Reveal(commit.Hash),
                OpenBranchesTab = () => _navigator.SelectTab<BranchesView>(),
            });
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
            (List<GitCommitInfo> commits, string error) = await Services.History.LogAsync(null, _state.AllBranches, skip, PageSize);
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
            _commits.SetLoading(_isLoading);
            _errorNotice.SetVisible(string.IsNullOrEmpty(_error) == false);
            _errorText.text = _error ?? string.Empty;
            _emptyNotice.SetVisible(HasHistory == false);
            _split.SetVisible(HasHistory);
        }
    }
}
