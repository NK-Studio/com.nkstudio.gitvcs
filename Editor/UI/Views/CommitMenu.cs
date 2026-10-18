using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 커밋 우클릭 메뉴. Graph·Branch Explorer·Changesets 탭이 같은 메뉴를 쓴다. 구성은 설정(<see cref="GitVcsSettings.MenuStyle"/>)을 따른다:
    /// Fork·GitKraken 식(이 커밋의 브랜치 → 만들기·Interactive Rebase·Reset → 적용 → 비교 → 복사) 또는
    /// Unity Version Control 의 체인지 세트 메뉴 순서(비교 → 만들기·전환 → 병합 → 이동 → 고급).
    /// 흐름은 <see cref="CommitActions"/> 가 맡고, 탭마다 다른 것(부모로 이동, 다른 탭 열기, 브랜치 목록)만 <see cref="Hooks"/> 로 받는다.
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

            /// <summary>
            /// 탭이 읽어 둔 브랜치 목록. Fork 스타일 메뉴가 이 커밋을 가리키는 브랜치의 체크아웃·병합 항목을 만든다. 없으면 null.
            /// </summary>
            public IReadOnlyList<GitBranchRef> Branches;
        }

        private readonly GitServices _services;
        private readonly CommitActions _actions;
        private readonly BranchActions _branchActions;
        private readonly BranchMenu _branchMenu;

        /// <summary>
        /// "비교 기준으로 표시" 한 커밋. 탭마다 하나씩 둔다.
        /// </summary>
        private GitCommitInfo _compareBase;

        public CommitMenu(GitServices services)
        {
            _services = services;
            _actions = new CommitActions(services);
            _branchActions = new BranchActions(services);
            _branchMenu = new BranchMenu(services);
        }

        public void Populate(DropdownMenu menu, GitCommitInfo commit, Hooks hooks)
        {
            if (GitVcsSettings.instance.MenuStyle == GitMenuStyle.UnityVersionControl)
            {
                PopulateUnityVcs(menu, commit, hooks);
            }
            else
            {
                PopulateFork(menu, commit, hooks);
            }
        }

        /// <summary>
        /// Fork 의 커밋 메뉴에 GitKraken 의 브랜치 항목을 더했다. git rebase -i 의 편집기는 띄울 수 없어서 Interactive Rebase 는
        /// 빠른 작업(Reword·Squash·Fixup·Drop)만 두고, 결과가 같은 명령(<see cref="GitRewrite"/>)으로 실행한다.
        /// </summary>
        private void PopulateFork(DropdownMenu menu, GitCommitInfo commit, Hooks hooks)
        {
            bool isHead = commit.Hash == _services.Repository.HeadCommit;
            DropdownMenuAction.Status writable = _services.Runner.IsBusy ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            DropdownMenuAction.Status notHead = isHead ? DropdownMenuAction.Status.Disabled : writable;
            string current = GitDisplay.MenuSafe(_actions.CurrentBranchName());

            // 이 커밋을 가리키는 브랜치: 체크아웃은 바로 누를 수 있게 맨 위에, 나머지 브랜치 작업은 브랜치마다 하위 메뉴로.
            List<GitBranchRef> here = BranchesAt(commit, hooks.Branches);
            if (here.Count > 0)
            {
                HashSet<string> localNames = new(hooks.Branches.Where(b => b.IsRemote == false).Select(b => b.Name));
                BranchMenu.Hooks branchHooks = new() { LocalNames = localNames };
                foreach (GitBranchRef branch in here)
                {
                    bool hasSameLocal = branch.IsRemote && localNames.Contains(BranchActions.LocalNameOf(branch));
                    if (branch.IsCurrent == false && hasSameLocal == false)
                    {
                        menu.AppendAction(GitText.Format("fork.checkoutBranch", GitDisplay.MenuSafe(branch.Name)), action => _ = _branchActions.SwitchAsync(branch, localNames), writable);
                    }
                }
                foreach (GitBranchRef branch in here)
                {
                    _branchMenu.PopulateFork(menu, GitText.Format("fork.branchSubmenu", GitDisplay.MenuSafe(branch.Name)) + "/", branch, branchHooks);
                }
                menu.AppendSeparator();
            }

            menu.AppendAction(GitText.Get("fork.newBranchHere"), action => _ = _actions.CreateBranchAsync(commit), writable);
            menu.AppendAction(GitText.Get("fork.newTagHere"), action => _ = _actions.CreateTagAsync(commit), writable);
            string rebase = GitText.Get("fork.interactiveRebase") + "/";
            menu.AppendAction(rebase + GitText.Get("fork.reword"), action => _ = _actions.RewriteAsync(commit, GitRewriteKind.Reword), RewriteStatus(commit, GitRewriteKind.Reword));
            menu.AppendAction(rebase + GitText.Get("fork.squash"), action => _ = _actions.RewriteAsync(commit, GitRewriteKind.Squash), RewriteStatus(commit, GitRewriteKind.Squash));
            menu.AppendAction(rebase + GitText.Get("fork.fixup"), action => _ = _actions.RewriteAsync(commit, GitRewriteKind.Fixup), RewriteStatus(commit, GitRewriteKind.Fixup));
            menu.AppendAction(rebase + GitText.Get("fork.drop"), action => _ = _actions.RewriteAsync(commit, GitRewriteKind.Drop), RewriteStatus(commit, GitRewriteKind.Drop));
            string reset = GitText.Format("fork.resetTo", current) + "/";
            menu.AppendAction(reset + GitText.Get("fork.resetSoft"), action => _ = _actions.ResetAsync(commit, GitResetMode.Soft), notHead);
            menu.AppendAction(reset + GitText.Get("fork.resetMixed"), action => _ = _actions.ResetAsync(commit, GitResetMode.Mixed), notHead);
            menu.AppendAction(reset + GitText.Get("fork.resetHard"), action => _ = _actions.ResetAsync(commit, GitResetMode.Hard), notHead);
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("fork.checkoutCommit"), action => _ = _actions.SwitchToAsync(commit), notHead);
            menu.AppendAction(GitText.Format("fork.mergeCommitInto", current), action => _ = _actions.MergeAsync(commit, GitMergeMode.Default), notHead);
            menu.AppendAction(GitText.Get("fork.cherryPick"), action => _ = _actions.CherryPickAsync(commit), notHead);
            menu.AppendAction(GitText.Get("fork.revert"), action => _ = _actions.RevertAsync(commit), writable);
            menu.AppendAction(GitText.Get("fork.savePatch"), action => _ = _actions.SavePatchAsync(commit), writable);
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("fork.compareLocal"), _ => _actions.CompareWithWorkingTree(commit));
            menu.AppendAction(GitText.Get("fork.compareParent"), _ => _actions.CompareWithParent(commit));
            AppendCompareWith(menu, GitText.Get("fork.compareWith") + "/", commit);
            if (hooks.GoToParent != null)
            {
                Action goToParent = hooks.GoToParent;
                menu.AppendAction(GitText.Get("commit.goToParent"), _ => goToParent());
            }
            if (hooks.ShowInExplorer != null)
            {
                Action showInExplorer = hooks.ShowInExplorer;
                menu.AppendAction(GitText.Get("branch.showInExplorer"), _ => showInExplorer());
            }
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("fork.copySha"), _ => _services.Assets.CopyToClipboard(commit.Hash));
            menu.AppendAction(GitText.Get("fork.copyInfo"), action => _ = _actions.CopyInfoAsync(commit));
            menu.AppendAction(GitText.Get("fork.copySubject"), _ => _services.Assets.CopyToClipboard(commit.Subject));
        }

        /// <summary>
        /// 이 커밋을 끝으로 하는 브랜치. 현재 브랜치 → 로컬 → 원격 순서.
        /// </summary>
        public static List<GitBranchRef> BranchesAt(GitCommitInfo commit, IReadOnlyList<GitBranchRef> branches)
        {
            if (branches == null)
            {
                return new List<GitBranchRef>();
            }
            return branches
                .Where(b => b.Hash == commit.Hash && b.Name.EndsWith("/HEAD", StringComparison.Ordinal) == false)
                .OrderByDescending(b => b.IsCurrent)
                .ThenBy(b => b.IsRemote)
                .ThenBy(b => b.Name, StringComparer.Ordinal)
                .ToList();
        }

        private DropdownMenuAction.Status RewriteStatus(GitCommitInfo commit, GitRewriteKind kind)
        {
            if (_services.Runner.IsBusy || GitRewrite.Unsupported(kind, commit) != null)
            {
                return DropdownMenuAction.Status.Disabled;
            }
            return DropdownMenuAction.Status.Normal;
        }

        /// <summary>
        /// 다른 리비전과 비교·비교 기준 표시. 두 스타일이 같은 항목을 쓰고 위치(하위 메뉴 경로)만 다르다.
        /// </summary>
        private void AppendCompareWith(DropdownMenu menu, string prefix, GitCommitInfo commit)
        {
            GitCommitInfo compareBase = _compareBase;
            bool canCompareWithMarked = compareBase != null && compareBase.Hash != commit.Hash;
            menu.AppendAction(prefix + GitText.Get("commit.compareWithRevision"), action => _ = _actions.CompareWithRevisionAsync(commit));
            menu.AppendAction(prefix + (canCompareWithMarked ? GitText.Format("commit.compareWithMarked", compareBase.ShortHash) : GitText.Get("commit.compareWithMarkedNone")),
                _ => GitCompareWindow.OpenRange(compareBase.Hash, commit.Hash),
                canCompareWithMarked ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            menu.AppendAction(prefix + GitText.Get("commit.markForCompare"), _ => _compareBase = commit,
                compareBase?.Hash == commit.Hash ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
        }

        private void PopulateUnityVcs(DropdownMenu menu, GitCommitInfo commit, Hooks hooks)
        {
            bool isHead = commit.Hash == _services.Repository.HeadCommit;
            DropdownMenuAction.Status writable = _services.Runner.IsBusy ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            DropdownMenuAction.Status notHead = isHead ? DropdownMenuAction.Status.Disabled : writable;

            menu.AppendAction(GitText.Get("commit.compareWithParent"), _ => _actions.CompareWithParent(commit));
            AppendCompareWith(menu, string.Empty, commit);
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
        }
    }
}
