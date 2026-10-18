using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>Branches 탭: 로컬·원격 브랜치 목록. 전환·생성·삭제 흐름은 <see cref="BranchActions"/> 에 맡긴다.</summary>
    public sealed class BranchesView : GitView
    {
        private readonly BranchesState _state;
        private readonly BranchActions _actions;
        private readonly TextField _newName;
        private readonly ToolbarToggle _switchAfterCreate;
        private readonly ToolbarButton _createButton;
        private readonly ToolbarButton _reloadButton;
        private readonly VisualElement _errorNotice;
        private readonly Label _errorText;
        private readonly VisualElement _emptyNotice;
        private readonly VisualElement _listPane;
        private readonly BranchList _branches;

        private HashSet<string> _localNames = new();
        private string _loadedKey;
        private bool _isLoading;

        public override string Title => GitText.Get("tab.branches");

        private string CurrentKey => $"{Services.Repository.HeadCommit}|{Services.Repository.UpstreamCommit}|{Services.Repository.Branch?.Name}|{Services.Repository.LastFetchTime}";

        public BranchesView(GitServices services, BranchesState state) : base(services, "BranchesView")
        {
            _state = state;
            _actions = new BranchActions(services);

            _newName = Root.Require<TextField>("new-name");
            _switchAfterCreate = Root.Require<ToolbarToggle>("switch-after-create");
            _createButton = Root.Require<ToolbarButton>("create-button");
            _reloadButton = Root.Require<ToolbarButton>("reload-button");
            _errorNotice = Root.Require<VisualElement>("error-notice");
            _errorText = Root.Require<Label>("error-text");
            _emptyNotice = Root.Require<VisualElement>("empty-notice");
            _listPane = Root.Require<VisualElement>("list-pane");
            _branches = new BranchList(Root.Require<ListView>("branch-list"));

            _newName.SetValueWithoutNotify(state.NewBranchName);
            _newName.RegisterValueChangedCallback(evt =>
            {
                _state.NewBranchName = evt.newValue;
                Render();
            });
            _switchAfterCreate.SetValueWithoutNotify(state.SwitchAfterCreate);
            _switchAfterCreate.RegisterValueChangedCallback(evt => _state.SwitchAfterCreate = evt.newValue);
            _createButton.clicked += Create;
            _reloadButton.clicked += Load;

            _branches.ItemChosen += item =>
            {
                if (item.Branch != null && item.Branch.IsCurrent == false && Services.Runner.IsBusy == false)
                {
                    _ = _actions.SwitchAsync(item.Branch, _localNames);
                }
            };
            _branches.ContextMenuRequested += PopulateContextMenu;
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
            Render();
        }

        public override void OnBusyChanged(bool isBusy)
        {
            Render();
        }

        private void ReloadIfNeeded()
        {
            if (_isLoading == false && _loadedKey != CurrentKey)
            {
                Load();
            }
        }

        private async void Load()
        {
            _isLoading = true;
            string key = CurrentKey;
            Render();
            (List<GitBranchRef> branches, string error) = await Services.History.BranchesAsync();
            _isLoading = false;
            _loadedKey = key;

            _errorNotice.SetVisible(string.IsNullOrEmpty(error) == false);
            _errorText.text = error ?? string.Empty;
            _localNames = new HashSet<string>(branches.Where(b => b.IsRemote == false).Select(b => b.Name));
            _branches.SetItems(BranchListItem.Build(branches), item => item.Key);
            Render();
        }

        private void Render()
        {
            bool hasCommits = Services.Repository.HeadCommit != null;
            _emptyNotice.SetVisible(hasCommits == false);
            _listPane.SetVisible(hasCommits);
            _reloadButton.SetEnabled(_isLoading == false);
            _createButton.SetEnabled(Services.Runner.IsBusy == false && hasCommits && string.IsNullOrWhiteSpace(_newName.value) == false);
        }

        private async void Create()
        {
            bool created = await _actions.CreateAsync(_newName.value.Trim(), _switchAfterCreate.value);
            if (created)
            {
                _newName.value = string.Empty;
            }
        }

        private void PopulateContextMenu(DropdownMenu menu, BranchListItem item)
        {
            GitBranchRef branch = item.Branch;
            if (branch == null)
            {
                return;
            }

            bool canAct = Services.Runner.IsBusy == false && branch.IsCurrent == false;
            DropdownMenuAction.Status status = canAct ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            string switchLabel = branch.IsRemote && _localNames.Contains(BranchActions.LocalNameOf(branch)) == false
                ? GitText.Format("branches.createLocalAndSwitch", BranchActions.LocalNameOf(branch))
                : GitText.Get("branches.switchTo");

            menu.AppendAction(switchLabel, action => _ = _actions.SwitchAsync(branch, _localNames), status);
            if (branch.IsRemote == false)
            {
                menu.AppendAction(GitText.Get("common.delete"), action => _ = _actions.DeleteAsync(branch), status);
            }
            menu.AppendSeparator();
            menu.AppendAction(GitText.Get("common.copyName"), _ => Services.Assets.CopyToClipboard(branch.Name));
        }
    }
}
