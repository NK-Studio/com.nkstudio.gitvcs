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
        private readonly CommitActions _commitActions;
        private readonly ITabNavigator _navigator;
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

        public BranchesView(GitServices services, BranchesState state, ITabNavigator navigator) : base(services, "BranchesView")
        {
            _state = state;
            _navigator = navigator;
            _actions = new BranchActions(services);
            _commitActions = new CommitActions(services);

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

        /// <summary>
        /// Unity Version Control 의 브랜치 메뉴 순서를 따른다(만들기·전환 → 병합 → 비교 → 원격 → 이름·삭제).
        /// git 에 없는 항목(다음으로 병합, 숨기기, 코드 검토, 최상위 브랜치, 권한)은 뺐고, "내보내기/가져오기" 는 git 의 원격(푸시·원격 삭제)으로 바꿨다.
        /// </summary>
        private void PopulateContextMenu(DropdownMenu menu, BranchListItem item)
        {
            GitBranchRef branch = item.Branch;
            if (branch == null)
            {
                return;
            }

            DropdownMenuAction.Status writable = Services.Runner.IsBusy ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            DropdownMenuAction.Status notCurrent = branch.IsCurrent ? DropdownMenuAction.Status.Disabled : writable;
            DropdownMenuAction.Status hasTip = string.IsNullOrEmpty(branch.Hash) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            string description = $"'{branch.Name}'";
            string current = GitDisplay.MenuSafe(_commitActions.CurrentBranchName());
            string switchLabel = branch.IsRemote && _localNames.Contains(BranchActions.LocalNameOf(branch)) == false
                ? GitText.Format("branches.createLocalAndSwitch", BranchActions.LocalNameOf(branch))
                : GitText.Get("branches.switchTo");

            menu.AppendAction(GitText.Get("branch.createChild"), action => _ = _commitActions.CreateBranchFromAsync(branch.Hash, description),
                string.IsNullOrEmpty(branch.Hash) ? DropdownMenuAction.Status.Disabled : writable);
            menu.AppendAction(switchLabel, action => _ = _actions.SwitchAsync(branch, _localNames), notCurrent);
            menu.AppendSeparator();

            menu.AppendAction(GitText.Format("branch.mergeInto", current), action => _ = _commitActions.MergeAsync(branch.Name, branch.Name, description, GitMergeMode.Default), notCurrent);
            string advancedMerge = GitText.Get("commit.advancedMerge") + "/";
            menu.AppendAction(advancedMerge + GitText.Get("commit.mergeNoFf"), action => _ = _commitActions.MergeAsync(branch.Name, branch.Name, description, GitMergeMode.NoFastForward), notCurrent);
            menu.AppendAction(advancedMerge + GitText.Get("commit.mergeSquash"), action => _ = _commitActions.MergeAsync(branch.Name, branch.Name, description, GitMergeMode.Squash), notCurrent);
            menu.AppendAction(advancedMerge + GitText.Format("branch.rebaseOnto", current), action => _ = _commitActions.RebaseOntoAsync(branch.Name, branch.Name, description), notCurrent);
            menu.AppendSeparator();

            menu.AppendAction(GitText.Format("branch.compareSinceDiverged", current), _ => GitCompareWindow.OpenSinceMergeBase("HEAD", branch.Hash),
                branch.IsCurrent || string.IsNullOrEmpty(branch.Hash) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            menu.AppendAction(GitText.Get("branch.compareWithOther"), action => _ = _commitActions.CompareWithRevisionAsync(branch.Hash, description, current), hasTip);
            menu.AppendAction(GitText.Get("branch.showInExplorer"), _ => _navigator.SelectTab<BranchExplorerView>()?.Reveal(branch.Hash), hasTip);
            menu.AppendSeparator();

            string remote = GitText.Get("branch.remote") + "/";
            if (branch.IsRemote)
            {
                menu.AppendAction(remote + GitText.Get("branch.deleteRemote"), action => _ = _actions.DeleteRemoteAsync(branch), writable);
            }
            else
            {
                menu.AppendAction(remote + GitText.Get("branch.push"), action => _ = _actions.PushAsync(branch), writable);
            }
            menu.AppendSeparator();

            if (branch.IsRemote == false)
            {
                menu.AppendAction(GitText.Get("branch.rename"), action => _ = _actions.RenameAsync(branch), writable);
                menu.AppendAction(GitText.Get("common.delete"), action => _ = _actions.DeleteAsync(branch), notCurrent);
                menu.AppendSeparator();
            }
            menu.AppendAction(GitText.Get("common.copyName"), _ => Services.Assets.CopyToClipboard(branch.Name));
        }
    }
}
