using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 브랜치 우클릭 메뉴. Branches 탭이 쓰고, Fork 스타일 커밋 메뉴(<see cref="CommitMenu"/>)도 그 커밋을 가리키는 브랜치마다 하위 메뉴로 넣는다.
    /// 구성은 설정(<see cref="GitVcsSettings.MenuStyle"/>)을 따른다. 흐름은 <see cref="BranchActions"/>·<see cref="CommitActions"/> 가 맡는다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed class BranchMenu
    {
        public sealed class Hooks
        {
            /// <summary>
            /// 로컬 브랜치 이름들. 원격 브랜치를 체크아웃할 때 같은 이름의 로컬 브랜치가 있으면 그쪽으로 간다.
            /// </summary>
            public ICollection<string> LocalNames;

            /// <summary>
            /// 브랜치 끝 커밋을 Branch Explorer 에서 보인다. null 이면 항목을 뺀다.
            /// </summary>
            public Action<string> ShowInExplorer;
        }

        private static readonly ICollection<string> NoNames = Array.Empty<string>();

        private readonly GitServices _services;
        private readonly BranchActions _actions;
        private readonly CommitActions _commitActions;

        public BranchMenu(GitServices services)
        {
            _services = services;
            _actions = new BranchActions(services);
            _commitActions = new CommitActions(services);
        }

        public void Populate(DropdownMenu menu, GitBranchRef branch, Hooks hooks)
        {
            if (GitVcsSettings.instance.MenuStyle == GitMenuStyle.UnityVersionControl)
            {
                PopulateUnityVcs(menu, branch, hooks);
            }
            else
            {
                PopulateFork(menu, string.Empty, branch, hooks);
            }
        }

        /// <summary>
        /// Fork·GitKraken 식: 위에서부터 체크아웃·병합·rebase → 원격(Pull·Push·Upstream) → 만들기·이름·삭제 → 비교 → 복사.
        /// 항목 이름에 브랜치 이름을 넣어("Merge 'feature' into 'main'") 무엇이 무엇에 들어가는지 메뉴만 보고 알 수 있게 한다.
        /// </summary>
        /// <param name="prefix">하위 메뉴 경로("브랜치 'x'/"). 맨 위 메뉴면 빈 문자열.</param>
        public void PopulateFork(DropdownMenu menu, string prefix, GitBranchRef branch, Hooks hooks)
        {
            DropdownMenuAction.Status writable = _services.Runner.IsBusy ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            DropdownMenuAction.Status notCurrent = branch.IsCurrent ? DropdownMenuAction.Status.Disabled : writable;
            bool hasTip = string.IsNullOrEmpty(branch.Hash) == false;
            ICollection<string> localNames = hooks?.LocalNames ?? NoNames;
            string name = GitDisplay.MenuSafe(branch.Name);
            string currentName = _commitActions.CurrentBranchName();
            string current = GitDisplay.MenuSafe(currentName);
            string description = $"'{branch.Name}'";

            if (branch.IsCurrent)
            {
                menu.AppendAction(prefix + GitText.Get("fork.pull"), action => _ = _actions.PullAsync(), writable);
                menu.AppendAction(prefix + GitText.Format("fork.push", name), action => _ = _actions.PushAsync(branch), writable);
                menu.AppendAction(prefix + GitText.Get("fork.setUpstream"), action => _ = _actions.SetUpstreamAsync(branch), writable);
            }
            else
            {
                menu.AppendAction(prefix + GitText.Format("fork.checkoutBranch", name), action => _ = _actions.SwitchAsync(branch, localNames), writable);
                Separator(menu, prefix);

                menu.AppendAction(prefix + GitText.Format("fork.mergeInto", name, current),
                    action => _ = _commitActions.MergeAsync(branch.Name, branch.Name, description, GitMergeMode.Default), writable);
                string mergeOptions = prefix + GitText.Get("fork.mergeAdvanced") + "/";
                menu.AppendAction(mergeOptions + GitText.Get("commit.mergeNoFf"),
                    action => _ = _commitActions.MergeAsync(branch.Name, branch.Name, description, GitMergeMode.NoFastForward), writable);
                menu.AppendAction(mergeOptions + GitText.Get("commit.mergeSquash"),
                    action => _ = _commitActions.MergeAsync(branch.Name, branch.Name, description, GitMergeMode.Squash), writable);
                menu.AppendAction(prefix + GitText.Format("fork.rebaseOnto", name, current),
                    action => _ = _commitActions.RebaseOntoAsync(branch.Name, branch.Name, description), writable);

                if (branch.IsRemote == false)
                {
                    // 체크아웃하지 않은 브랜치는 Pull 대신 fast-forward 로 원격까지 따라잡는다(작업 트리를 건드리지 않는다).
                    bool hasUpstream = string.IsNullOrEmpty(branch.Upstream) == false;
                    menu.AppendAction(prefix + (hasUpstream ? GitText.Format("fork.fastForwardTo", name, GitDisplay.MenuSafe(branch.Upstream)) : GitText.Get("fork.fastForwardNoUpstream")),
                        action => _ = _actions.FastForwardAsync(branch), hasUpstream ? writable : DropdownMenuAction.Status.Disabled);
                    Separator(menu, prefix);
                    menu.AppendAction(prefix + GitText.Format("fork.push", name), action => _ = _actions.PushAsync(branch), writable);
                    menu.AppendAction(prefix + GitText.Get("fork.setUpstream"), action => _ = _actions.SetUpstreamAsync(branch), writable);
                }
            }
            Separator(menu, prefix);

            DropdownMenuAction.Status tipWritable = hasTip ? writable : DropdownMenuAction.Status.Disabled;
            menu.AppendAction(prefix + GitText.Format("fork.newBranchFrom", name), action => _ = _commitActions.CreateBranchFromAsync(branch.Hash, description), tipWritable);
            menu.AppendAction(prefix + GitText.Format("fork.newTagOn", name), action => _ = _commitActions.CreateTagAtAsync(branch.Hash, description), tipWritable);
            if (branch.IsRemote)
            {
                menu.AppendAction(prefix + GitText.Format("fork.deleteRemote", name), action => _ = _actions.DeleteRemoteAsync(branch), writable);
            }
            else
            {
                menu.AppendAction(prefix + GitText.Format("fork.rename", name), action => _ = _actions.RenameAsync(branch), writable);
                menu.AppendAction(prefix + GitText.Format("fork.delete", name), action => _ = _actions.DeleteAsync(branch), notCurrent);
            }
            Separator(menu, prefix);

            DropdownMenuAction.Status tipOnly = hasTip ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            if (branch.IsCurrent == false)
            {
                menu.AppendAction(prefix + GitText.Format("fork.compareBranches", name, current), _ => GitCompareWindow.OpenSinceMergeBase("HEAD", branch.Hash), tipOnly);
            }
            menu.AppendAction(prefix + GitText.Get("fork.compareWithOther"), action => _ = _commitActions.CompareWithRevisionAsync(branch.Hash, description, currentName), tipOnly);
            Action<string> showInExplorer = hooks?.ShowInExplorer;
            if (showInExplorer != null)
            {
                menu.AppendAction(prefix + GitText.Get("branch.showInExplorer"), _ => showInExplorer(branch.Hash), tipOnly);
            }
            Separator(menu, prefix);
            menu.AppendAction(prefix + GitText.Get("fork.copyBranchName"), _ => _services.Assets.CopyToClipboard(branch.Name));
        }

        /// <summary>
        /// Unity Version Control 의 브랜치 메뉴 순서를 따른다(만들기·전환 → 병합 → 비교 → 원격 → 이름·삭제).
        /// git 에 없는 항목(다음으로 병합, 숨기기, 코드 검토, 최상위 브랜치, 권한)은 뺐고, "내보내기/가져오기" 는 git 의 원격(푸시·원격 삭제)으로 바꿨다.
        /// </summary>
        private void PopulateUnityVcs(DropdownMenu menu, GitBranchRef branch, Hooks hooks)
        {
            DropdownMenuAction.Status writable = _services.Runner.IsBusy ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            DropdownMenuAction.Status notCurrent = branch.IsCurrent ? DropdownMenuAction.Status.Disabled : writable;
            DropdownMenuAction.Status hasTip = string.IsNullOrEmpty(branch.Hash) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
            ICollection<string> localNames = hooks?.LocalNames ?? NoNames;
            string description = $"'{branch.Name}'";
            string current = GitDisplay.MenuSafe(_commitActions.CurrentBranchName());
            string switchLabel = branch.IsRemote && localNames.Contains(BranchActions.LocalNameOf(branch)) == false
                ? GitText.Format("branches.createLocalAndSwitch", BranchActions.LocalNameOf(branch))
                : GitText.Get("branches.switchTo");

            menu.AppendAction(GitText.Get("branch.createChild"), action => _ = _commitActions.CreateBranchFromAsync(branch.Hash, description),
                string.IsNullOrEmpty(branch.Hash) ? DropdownMenuAction.Status.Disabled : writable);
            menu.AppendAction(switchLabel, action => _ = _actions.SwitchAsync(branch, localNames), notCurrent);
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
            Action<string> showInExplorer = hooks?.ShowInExplorer;
            if (showInExplorer != null)
            {
                menu.AppendAction(GitText.Get("branch.showInExplorer"), _ => showInExplorer(branch.Hash), hasTip);
            }
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
            menu.AppendAction(GitText.Get("common.copyName"), _ => _services.Assets.CopyToClipboard(branch.Name));
        }

        /// <summary>
        /// 하위 메뉴 안의 구분선은 그 경로("브랜치 'x'/")를 줘야 그 하위 메뉴에 들어간다.
        /// </summary>
        private static void Separator(DropdownMenu menu, string prefix)
        {
            menu.AppendSeparator(string.IsNullOrEmpty(prefix) ? null : prefix);
        }
    }
}
