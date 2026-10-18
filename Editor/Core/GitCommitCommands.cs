using System;
using System.Collections.Generic;

namespace GitVersionControl
{
    public enum GitMergeMode
    {
        /// <summary>
        /// git 기본: 앞으로 감기가 되면 감고, 아니면 병합 커밋.
        /// </summary>
        Default,

        /// <summary>
        /// 앞으로 감기가 되어도 병합 커밋을 만든다(<c>--no-ff</c>).
        /// </summary>
        NoFastForward,

        /// <summary>
        /// 변경만 가져와 stage 한다. 커밋은 사용자가 한다(<c>--squash</c>).
        /// </summary>
        Squash,
    }

    public enum GitResetMode
    {
        Soft,
        Mixed,
        Hard,
    }

    /// <summary>
    /// 커밋 하나를 대상으로 하는 명령(Branch Explorer 커밋 메뉴)의 git 인자. 저장소를 건드리지 않는 순수 규칙이라 테스트로 확인한다.
    /// 병합 커밋을 체리픽·revert 할 때는 첫 부모를 기준(<c>-m 1</c>)으로 삼는다 — "그 병합이 들여온 변경" 이라는 뜻이 된다.
    /// </summary>
    public static class GitCommitCommands
    {
        public static string[] Merge(string commit, GitMergeMode mode)
        {
            List<string> args = new() { "merge", "--no-edit" };
            switch (mode)
            {
                case GitMergeMode.Default:
                    break;
                case GitMergeMode.NoFastForward:
                    args.Add("--no-ff");
                    break;
                case GitMergeMode.Squash:
                    args.Add("--squash");
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
            args.Add(commit);
            return args.ToArray();
        }

        public static string[] CherryPick(string commit, bool isMerge)
        {
            return isMerge
                ? new[] { "cherry-pick", "-m", "1", commit }
                : new[] { "cherry-pick", commit };
        }

        public static string[] Revert(string commit, bool isMerge)
        {
            return isMerge
                ? new[] { "revert", "--no-edit", "-m", "1", commit }
                : new[] { "revert", "--no-edit", commit };
        }

        public static string[] Reset(string commit, GitResetMode mode)
        {
            return new[] { "reset", ResetFlag(mode), commit };
        }

        /// <summary>
        /// reset 을 되돌릴 때 쓸 방식. soft·mixed 는 같은 방식으로 돌아가야 stage·작업 트리의 차이를 지킨다.
        /// hard 는 작업 트리가 깨끗하므로 기본(<c>--keep</c>)으로 충분하다 — null.
        /// </summary>
        public static string UndoResetMode(GitResetMode mode)
        {
            return mode == GitResetMode.Hard ? null : ResetFlag(mode);
        }

        public static string[] Rebase(string revision)
        {
            return new[] { "rebase", revision };
        }

        public static string[] RenameBranch(string oldName, string newName)
        {
            return new[] { "branch", "-m", oldName, newName };
        }

        /// <param name="setUpstream">upstream 이 없는 브랜치면 true: 올리면서 추적을 건다(<c>-u</c>).</param>
        public static string[] PushBranch(string remote, string branch, bool setUpstream)
        {
            return setUpstream
                ? new[] { "push", "-u", remote, branch }
                : new[] { "push", remote, branch };
        }

        public static string[] DeleteRemoteBranch(string remote, string branch)
        {
            return new[] { "push", remote, "--delete", branch };
        }

        /// <summary>
        /// 원격 브랜치 이름을 원격과 브랜치로 나눈다. "origin/feature/x" → ("origin", "feature/x"). 원격 이름에는 '/' 가 없다고 본다.
        /// </summary>
        public static (string remote, string branch) SplitRemoteBranch(string remoteBranchName)
        {
            int slash = remoteBranchName.IndexOf('/');
            return slash < 0 ? (remoteBranchName, string.Empty) : (remoteBranchName.Substring(0, slash), remoteBranchName.Substring(slash + 1));
        }

        /// <summary>
        /// 체크아웃하지 않은 로컬 브랜치를 <paramref name="upstream"/> 까지 앞으로 감는다. 작업 트리는 건드리지 않고, 앞으로 감기가 안 되면 git 이 거부한다.
        /// 현재 브랜치에는 쓸 수 없다(git 이 거부한다) — 현재 브랜치는 Pull 을 쓴다.
        /// </summary>
        public static string[] FastForwardBranch(string branch, string upstream)
        {
            return new[] { "fetch", ".", upstream + ":refs/heads/" + branch };
        }

        public static string[] SetUpstream(string branch, string upstream)
        {
            return new[] { "branch", "--set-upstream-to=" + upstream, branch };
        }

        /// <summary>
        /// 브랜치가 추적하는 원격 브랜치 이름("origin/main"). 없으면 git 이 실패한다.
        /// </summary>
        public static string[] UpstreamOf(string branch)
        {
            return new[] { "rev-parse", "--abbrev-ref", "--symbolic-full-name", branch + "@{upstream}" };
        }

        /// <summary>
        /// 커밋 하나를 메일 형식 패치로 stdout 에 쓴다(<c>git am</c> 으로 다시 적용할 수 있다).
        /// </summary>
        public static string[] FormatPatch(string commit)
        {
            return new[] { "format-patch", "-1", "--stdout", commit };
        }

        public static string[] DeleteTag(string name)
        {
            return new[] { "tag", "-d", name };
        }

        public static string[] PushTag(string remote, string name)
        {
            return new[] { "push", remote, "refs/tags/" + name };
        }

        public static string[] DeleteRemoteTag(string remote, string name)
        {
            return new[] { "push", remote, "--delete", "refs/tags/" + name };
        }

        /// <param name="stash">"stash@{0}"</param>
        public static string[] StashApply(string stash, bool drop)
        {
            return new[] { "stash", drop ? "pop" : "apply", stash };
        }

        public static string[] StashDrop(string stash)
        {
            return new[] { "stash", "drop", stash };
        }

        /// <summary>
        /// stash 를 넣었던 커밋에서 새 브랜치를 만들어 전환하고, 그 위에 stash 를 적용한 뒤 지운다.
        /// </summary>
        public static string[] StashBranch(string branch, string stash)
        {
            return new[] { "stash", "branch", branch, stash };
        }

        public static string[] RemoveWorktree(string path)
        {
            return new[] { "worktree", "remove", path };
        }

        public static string[] UpdateSubmodule(string path)
        {
            return new[] { "submodule", "update", "--init", "--recursive", "--", path };
        }

        public static string[] Tag(string name, string commit)
        {
            return new[] { "tag", name, commit };
        }

        public static string[] ValidateTagName(string name)
        {
            return new[] { "check-ref-format", "refs/tags/" + name };
        }

        /// <summary>
        /// 입력한 리비전(브랜치·태그·해시·HEAD~2 등)을 커밋 해시로 바꾸는 명령.
        /// </summary>
        public static string[] ResolveCommit(string revision)
        {
            return new[] { "rev-parse", "--verify", "--quiet", revision + "^{commit}" };
        }

        private static string ResetFlag(GitResetMode mode)
        {
            switch (mode)
            {
                case GitResetMode.Soft:
                    return "--soft";
                case GitResetMode.Mixed:
                    return "--mixed";
                case GitResetMode.Hard:
                    return "--hard";
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }
    }
}
