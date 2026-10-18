using System;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 커밋 우클릭 메뉴. Branch Explorer 와 Changesets 탭이 같은 메뉴를 쓴다(Unity Version Control 의 체인지 세트 메뉴와 같은 순서:
    /// 비교 → 만들기·전환 → 병합 → 이동 → 고급). 흐름은 <see cref="CommitActions"/> 가 맡고, 탭마다 다른 것(부모로 이동, 다른 탭 열기)만 <see cref="Hooks"/> 로 받는다.
    /// 오른쪽 클릭이 곧 선택이라 "선택한 두 커밋 비교" 대신 "비교 기준으로 표시" 후 "표시한 커밋과 비교" 로 두 커밋을 고른다.
    /// </summary>
    public sealed class CommitMenu
    {
        /// <summary>
        /// 탭마다 다른 항목. null 인 항목은 메뉴에서 빼거나(탭 이동) 끈다(부모로 이동).
        /// </summary>
        public sealed class Hooks
        {
            /// <summary>
            /// 부모 커밋으로 이동. 부모가 지금 목록·그래프에 없으면 null.
            /// </summary>
            public Action GoToParent;
            public Action ShowInExplorer;
            public Action OpenBranchesTab;
        }

        private readonly GitServices _services;
        private readonly CommitActions _actions;

        /// <summary>
        /// "비교 기준으로 표시" 한 커밋. 탭마다 하나씩 둔다.
        /// </summary>
        private GitCommitInfo _compareBase;

        public CommitMenu(GitServices services)
        {
            _services = services;
            _actions = new CommitActions(services);
        }

        public void Populate(DropdownMenu menu, GitCommitInfo commit, Hooks hooks)
        {
            bool isHead = commit.Hash == _services.Repository.HeadCommit;
            DropdownMenuAction.Status writable = _services.Runner.IsBusy ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            DropdownMenuAction.Status notHead = isHead ? DropdownMenuAction.Status.Disabled : writable;
            GitCommitInfo compareBase = _compareBase;
            bool canCompareWithMarked = compareBase != null && compareBase.Hash != commit.Hash;

            menu.AppendAction(GitText.Get("commit.compareWithParent"), _ => _actions.CompareWithParent(commit));
            menu.AppendAction(GitText.Get("commit.compareWithRevision"), action => _ = _actions.CompareWithRevisionAsync(commit));
            menu.AppendAction(canCompareWithMarked ? GitText.Format("commit.compareWithMarked", compareBase.ShortHash) : GitText.Get("commit.compareWithMarkedNone"),
                _ => GitCompareWindow.OpenRange(compareBase.Hash, commit.Hash),
                canCompareWithMarked ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction(GitText.Get("commit.markForCompare"), _ => _compareBase = commit,
                compareBase?.Hash == commit.Hash ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("commit.createBranch"), action => _ = _actions.CreateBranchAsync(commit), writable);
            menu.AppendAction(GitText.Get("commit.tag"), action => _ = _actions.CreateTagAsync(commit), writable);
            menu.AppendAction(GitText.Get("commit.switchTo"), action => _ = _actions.SwitchToAsync(commit), notHead);
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("commit.merge"), action => _ = _actions.MergeAsync(commit, GitMergeMode.Default), notHead);
            menu.AppendAction(GitText.Get("commit.cherryPick"), action => _ = _actions.CherryPickAsync(commit), notHead);
            string advancedMerge = GitText.Get("commit.advancedMerge") + "/";
            menu.AppendAction(advancedMerge + GitText.Get("commit.mergeNoFf"), action => _ = _actions.MergeAsync(commit, GitMergeMode.NoFastForward), notHead);
            menu.AppendAction(advancedMerge + GitText.Get("commit.mergeSquash"), action => _ = _actions.MergeAsync(commit, GitMergeMode.Squash), notHead);
            menu.AppendAction(advancedMerge + GitText.Get("commit.revert"), action => _ = _actions.RevertAsync(commit), writable);
            menu.AppendSeparator();

            Action goToParent = hooks.GoToParent;
            menu.AppendAction(GitText.Get("commit.goToParent"), _ => goToParent(), goToParent == null ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            if (hooks.ShowInExplorer != null)
            {
                Action showInExplorer = hooks.ShowInExplorer;
                menu.AppendAction(GitText.Get("branch.showInExplorer"), _ => showInExplorer());
            }
            menu.AppendSeparator();

            string advanced = GitText.Get("commit.advanced") + "/";
            string reset = advanced + GitText.Get("commit.resetHere") + "/";
            menu.AppendAction(reset + GitText.Get("commit.resetSoft"), action => _ = _actions.ResetAsync(commit, GitResetMode.Soft), notHead);
            menu.AppendAction(reset + GitText.Get("commit.resetMixed"), action => _ = _actions.ResetAsync(commit, GitResetMode.Mixed), notHead);
            menu.AppendAction(reset + GitText.Get("commit.resetHard"), action => _ = _actions.ResetAsync(commit, GitResetMode.Hard), notHead);
            menu.AppendAction(advanced + GitText.Get("common.copyHash"), _ => _services.Assets.CopyToClipboard(commit.Hash));
            menu.AppendAction(advanced + GitText.Get("common.copySubject"), _ => _services.Assets.CopyToClipboard(commit.Subject));
            if (hooks.OpenBranchesTab != null)
            {
                Action openBranchesTab = hooks.OpenBranchesTab;
                menu.AppendAction(advanced + GitText.Get("common.openBranchesTab"), _ => openBranchesTab());
            }
        }
    }
}
