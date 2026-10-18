using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// Branch Explorer 커밋 메뉴(병합·체리픽·revert·reset·태그)가 만드는 git 인자.
    /// </summary>
    public sealed class GitCommitCommandsTests
    {
        private static string Joined(string[] args)
        {
            return string.Join(" ", args);
        }

        [TestCase(GitMergeMode.Default, "merge --no-edit abc")]
        [TestCase(GitMergeMode.NoFastForward, "merge --no-edit --no-ff abc")]
        [TestCase(GitMergeMode.Squash, "merge --no-edit --squash abc")]
        public void Test_Merge_AddsModeFlag(GitMergeMode mode, string expected)
        {
            Assert.AreEqual(expected, Joined(GitCommitCommands.Merge("abc", mode)));
        }

        [TestCase(false, "cherry-pick abc")]
        [TestCase(true, "cherry-pick -m 1 abc")]
        public void Test_CherryPick_MergeCommitUsesFirstParent(bool isMerge, string expected)
        {
            Assert.AreEqual(expected, Joined(GitCommitCommands.CherryPick("abc", isMerge)));
        }

        [TestCase(false, "revert --no-edit abc")]
        [TestCase(true, "revert --no-edit -m 1 abc")]
        public void Test_Revert_MergeCommitUsesFirstParent(bool isMerge, string expected)
        {
            Assert.AreEqual(expected, Joined(GitCommitCommands.Revert("abc", isMerge)));
        }

        [TestCase(GitResetMode.Soft, "reset --soft abc", "--soft")]
        [TestCase(GitResetMode.Mixed, "reset --mixed abc", "--mixed")]
        [TestCase(GitResetMode.Hard, "reset --hard abc", null)]
        public void Test_Reset_UndoUsesMatchingMode(GitResetMode mode, string expected, string undoMode)
        {
            Assert.AreEqual(expected, Joined(GitCommitCommands.Reset("abc", mode)));
            Assert.AreEqual(undoMode, GitCommitCommands.UndoResetMode(mode));
        }

        [Test]
        public void Test_TagAndResolve_Arguments()
        {
            Assert.AreEqual("tag v1.0 abc", Joined(GitCommitCommands.Tag("v1.0", "abc")));
            Assert.AreEqual("check-ref-format refs/tags/v1.0", Joined(GitCommitCommands.ValidateTagName("v1.0")));
            Assert.AreEqual("rev-parse --verify --quiet HEAD~1^{commit}", Joined(GitCommitCommands.ResolveCommit("HEAD~1")));
        }

        [Test]
        public void Test_BranchCommands_Arguments()
        {
            Assert.AreEqual("rebase origin/main", Joined(GitCommitCommands.Rebase("origin/main")));
            Assert.AreEqual("branch -m old new", Joined(GitCommitCommands.RenameBranch("old", "new")));
            Assert.AreEqual("push -u origin feature/x", Joined(GitCommitCommands.PushBranch("origin", "feature/x", setUpstream: true)));
            Assert.AreEqual("push origin feature/x", Joined(GitCommitCommands.PushBranch("origin", "feature/x", setUpstream: false)));
            Assert.AreEqual("push origin --delete feature/x", Joined(GitCommitCommands.DeleteRemoteBranch("origin", "feature/x")));
        }

        [Test]
        public void Test_SplitRemoteBranch_KeepsSlashesInBranchName()
        {
            Assert.AreEqual(("origin", "feature/x"), GitCommitCommands.SplitRemoteBranch("origin/feature/x"));
        }

        [Test]
        public void Test_MenuSafe_ReplacesSubmenuSeparator()
        {
            Assert.AreEqual("feature∕x", GitDisplay.MenuSafe("feature/x"));
        }

        [TestCase("0123456789abcdef0123456789abcdef01234567", "0123456")]
        [TestCase("HEAD~2", "HEAD~2")]
        [TestCase("main", "main")]
        public void Test_ShortRevision_ShortensOnlyFullHashes(string revision, string expected)
        {
            Assert.AreEqual(expected, GitDisplay.ShortRevision(revision));
        }
    }
}
