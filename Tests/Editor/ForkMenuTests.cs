using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// Fork 스타일 메뉴가 쓰는 순수 규칙: 히스토리 고치기(<see cref="GitRewrite"/>)·브랜치 명령 인자·커밋의 브랜치 찾기·패치 파일 이름.
    /// </summary>
    public sealed class ForkMenuTests
    {
        private static string Joined(string[] args)
        {
            return string.Join(" ", args);
        }

        private static GitCommitInfo Commit(string hash, params string[] parents)
        {
            return new GitCommitInfo { Hash = hash, ShortHash = hash, Parents = parents, Subject = "s" };
        }

        [Test]
        public void Test_FastForwardBranch_FetchesFromSelfIntoBranchRef()
        {
            Assert.AreEqual("fetch . origin/feature/x:refs/heads/feature/x", Joined(GitCommitCommands.FastForwardBranch("feature/x", "origin/feature/x")));
        }

        [Test]
        public void Test_SetUpstream_And_UpstreamOf()
        {
            Assert.AreEqual("branch --set-upstream-to=origin/main main", Joined(GitCommitCommands.SetUpstream("main", "origin/main")));
            Assert.AreEqual("rev-parse --abbrev-ref --symbolic-full-name main@{upstream}", Joined(GitCommitCommands.UpstreamOf("main")));
        }

        [Test]
        public void Test_FormatPatch_SingleCommitToStdout()
        {
            Assert.AreEqual("format-patch -1 --stdout abc", Joined(GitCommitCommands.FormatPatch("abc")));
        }

        [Test]
        public void Test_Unsupported_RewordAllowsMergeAndRoot()
        {
            Assert.IsNull(GitRewrite.Unsupported(GitRewriteKind.Reword, Commit("a")));
            Assert.IsNull(GitRewrite.Unsupported(GitRewriteKind.Reword, Commit("m", "p1", "p2")));
        }

        [TestCase(GitRewriteKind.Squash)]
        [TestCase(GitRewriteKind.Fixup)]
        [TestCase(GitRewriteKind.Drop)]
        public void Test_Unsupported_FoldAndDropNeedSingleParent(GitRewriteKind kind)
        {
            Assert.AreEqual("rewrite.rootCommit", GitRewrite.Unsupported(kind, Commit("a")));
            Assert.AreEqual("rewrite.mergeCommit", GitRewrite.Unsupported(kind, Commit("m", "p1", "p2")));
            Assert.IsNull(GitRewrite.Unsupported(kind, Commit("c", "p")));
        }

        [Test]
        public void Test_CombinedMessage_SquashJoinsFixupKeepsParent()
        {
            Assert.AreEqual("Parent\n\nChild\n", GitRewrite.CombinedMessage(GitRewriteKind.Squash, "Parent\n", "Child\n"));
            Assert.AreEqual("Parent\n", GitRewrite.CombinedMessage(GitRewriteKind.Fixup, "Parent\n", "Child\n"));
        }

        [Test]
        public void Test_RemapParents_ReturnsNullWhenNothingChanged()
        {
            Dictionary<string, string> replaced = new() { ["b"] = "b2" };
            Assert.IsNull(GitRewrite.RemapParents(new[] { "a" }, replaced));
            CollectionAssert.AreEqual(new[] { "a", "b2" }, GitRewrite.RemapParents(new[] { "a", "b" }, replaced));
        }

        [Test]
        public void Test_CommitTree_PassesEveryParent()
        {
            Assert.AreEqual("commit-tree t -p a -p b -F m.txt", Joined(GitRewrite.CommitTree("t", new[] { "a", "b" }, "m.txt")));
            Assert.AreEqual("commit-tree t -F m.txt", Joined(GitRewrite.CommitTree("t", new string[0], "m.txt")));
        }

        [Test]
        public void Test_DropRebase_KeepsMergesAndStashesChanges()
        {
            Assert.AreEqual("rebase --rebase-merges --autostash --onto abc^ abc", Joined(GitRewrite.DropRebase("abc")));
        }

        [Test]
        public void Test_Parse_ReadsFieldsAndKeepsMultilineMessage()
        {
            const string output = "h1\u001fp1 p2\u001ft1\u001fKim\u001fkim@x.com\u001f1700000000 +0900\u001fSubject\n\nBody line\n\u001e\n"
                                  + "h2\u001f\u001ft2\u001fLee\u001flee@x.com\u001f1700000001 +0000\u001fRoot\n\u001e\n";
            List<GitRewriteCommit> commits = GitRewrite.Parse(output);
            Assert.AreEqual(2, commits.Count);
            CollectionAssert.AreEqual(new[] { "p1", "p2" }, commits[0].Parents);
            Assert.AreEqual("t1", commits[0].Tree);
            Assert.AreEqual("1700000000 +0900", commits[0].AuthorDate);
            Assert.AreEqual("Subject\n\nBody line\n", commits[0].Message);
            Assert.AreEqual(0, commits[1].Parents.Length);
            Assert.AreEqual("Lee", commits[1].AuthorName);
        }

        [Test]
        public void Test_BranchesAt_CurrentThenLocalThenRemote_SkipsRemoteHead()
        {
            List<GitBranchRef> branches = new()
            {
                new GitBranchRef { Name = "origin/main", IsRemote = true, Hash = "c" },
                new GitBranchRef { Name = "origin/HEAD", IsRemote = true, Hash = "c" },
                new GitBranchRef { Name = "zeta", Hash = "c" },
                new GitBranchRef { Name = "main", IsCurrent = true, Hash = "c" },
                new GitBranchRef { Name = "other", Hash = "d" },
            };
            List<GitBranchRef> here = CommitMenu.BranchesAt(Commit("c", "p"), branches);
            CollectionAssert.AreEqual(new[] { "main", "zeta", "origin/main" }, here.Select(b => b.Name).ToArray());
            Assert.AreEqual(0, CommitMenu.BranchesAt(Commit("c", "p"), null).Count);
        }

        [TestCase("Fix: 마을 세이브 GC 문제 수정", "Fix-마을-세이브-GC-문제-수정")]
        [TestCase("  feat(ui): add  menu!  ", "feat-ui-add-menu")]
        [TestCase("!!!", "patch")]
        public void Test_PatchFileSlug(string subject, string expected)
        {
            Assert.AreEqual(expected, GitDisplay.PatchFileSlug(subject));
        }
    }
}
