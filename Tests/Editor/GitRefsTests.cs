using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 사이드바 참조 트리: 태그·스태시·워크트리·서브모듈 출력 파싱, 앞섬/뒤처짐 배지, 트리 묶음·필터, 새 명령 인자.
    /// </summary>
    public sealed class GitRefsTests
    {
        private static string Joined(string[] args)
        {
            return string.Join(" ", args);
        }

        [Test]
        public void Test_ParseTags_AnnotatedUsesPeeledCommit()
        {
            const string output = "v1.0\u001ftagobj\u001fcommit1\u001ftag\u001fRelease 1.0\u001e\n"
                                  + "light\u001fcommit2\u001f\u001fcommit\u001fFix bug\u001e\n";
            List<GitTagRef> tags = GitRefs.ParseTags(output);
            Assert.AreEqual(2, tags.Count);
            Assert.AreEqual("commit1", tags[0].Commit);
            Assert.IsTrue(tags[0].IsAnnotated);
            Assert.AreEqual("Release 1.0", tags[0].Subject);
            Assert.AreEqual("commit2", tags[1].Commit);
            Assert.IsFalse(tags[1].IsAnnotated);
        }

        [Test]
        public void Test_ParseStashes()
        {
            const string output = "stash@{0}\u001fabc\u001f1700000000\u001fWIP on main: 123 msg\u001e\nstash@{1}\u001fdef\u001f0\u001fOn dev: saved\u001e\n";
            List<GitStashRef> stashes = GitRefs.ParseStashes(output);
            Assert.AreEqual(2, stashes.Count);
            Assert.AreEqual("stash@{0}", stashes[0].Name);
            Assert.AreEqual("abc", stashes[0].Hash);
            Assert.AreEqual("WIP on main: 123 msg", stashes[0].Message);
            Assert.AreEqual(System.DateTime.MinValue, stashes[1].Date);
        }

        [Test]
        public void Test_ParseWorktrees_PorcelainBlocks()
        {
            const string output = "worktree E:/repo\nHEAD aaa\nbranch refs/heads/main\n\n"
                                  + "worktree E:/repo-unity63\nHEAD bbb\nbranch refs/heads/feature/x\nlocked\n\n"
                                  + "worktree E:/repo-detached\nHEAD ccc\ndetached\nprunable gitdir file points to non-existent location\n";
            List<GitWorktreeRef> worktrees = GitRefs.ParseWorktrees(output, "E:/repo");
            Assert.AreEqual(3, worktrees.Count);
            Assert.IsTrue(worktrees[0].IsCurrent);
            Assert.AreEqual("main", worktrees[0].Branch);
            Assert.AreEqual("feature/x", worktrees[1].Branch);
            Assert.IsTrue(worktrees[1].IsLocked);
            Assert.AreEqual("repo-unity63", worktrees[1].DisplayName);
            Assert.IsTrue(worktrees[2].IsDetached);
            Assert.IsTrue(worktrees[2].IsPrunable);
            Assert.IsFalse(worktrees[2].IsCurrent);
        }

        [Test]
        public void Test_ParseSubmodules_StateAndDescribe()
        {
            const string output = " aaa Libs/Core (v1.2-3-gaaa)\n-bbb Libs/Not Init\n+ccc Libs/Moved (heads/main)\nUddd Libs/Conflict\n";
            List<GitSubmoduleRef> submodules = GitRefs.ParseSubmodules(output);
            Assert.AreEqual(4, submodules.Count);
            Assert.AreEqual(GitSubmoduleState.UpToDate, submodules[0].State);
            Assert.AreEqual("Libs/Core", submodules[0].Path);
            Assert.AreEqual("v1.2-3-gaaa", submodules[0].Describe);
            Assert.AreEqual(GitSubmoduleState.NotInitialized, submodules[1].State);
            Assert.AreEqual("Libs/Not Init", submodules[1].Path);
            Assert.AreEqual(GitSubmoduleState.Modified, submodules[2].State);
            Assert.AreEqual(GitSubmoduleState.Conflicted, submodules[3].State);
        }

        [Test]
        public void Test_ParseGitlinkPaths_OnlyGitlinksOnce()
        {
            const string output = "100644 aaa 0\tAssets/a.cs\0"
                                  + "160000 bbb 0\tPackages/\ud55c\uae00 \ubaa8\ub4c8\0"
                                  + "160000 ccc 1\tLibs/Conflict\0160000 ddd 2\tLibs/Conflict\0160000 eee 3\tLibs/Conflict\0";
            List<string> paths = GitRefs.ParseGitlinkPaths(output);
            CollectionAssert.AreEqual(new[] { "Packages/\ud55c\uae00 \ubaa8\ub4c8", "Libs/Conflict" }, paths);
        }

        [TestCase("[ahead 1, behind 2]", "1\u2191 2\u2193")]
        [TestCase("[ahead 3]", "3\u2191")]
        [TestCase("[behind 4]", "4\u2193")]
        [TestCase("[gone]", "")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void Test_TrackBadge(string track, string expected)
        {
            Assert.AreEqual(expected, GitRefs.TrackBadge(track));
        }

        private static GitRefSet SampleRefs()
        {
            return new GitRefSet
            {
                Branches = new List<GitBranchRef>
                {
                    new() { Name = "main", RefName = "refs/heads/main", Hash = "m" },
                    new() { Name = "feature/spine-vat", RefName = "refs/heads/feature/spine-vat", Hash = "a" },
                    new() { Name = "feature/unity63", RefName = "refs/heads/feature/unity63", Hash = "b", IsCurrent = true, Track = "[ahead 1]" },
                    new() { Name = "origin/main", RefName = "refs/remotes/origin/main", Hash = "m", IsRemote = true },
                    new() { Name = "origin/HEAD", RefName = "refs/remotes/origin/HEAD", Hash = "m", IsRemote = true },
                },
                Tags = new List<GitTagRef> { new() { Name = "release/1.0", Commit = "m" } },
                Stashes = new List<GitStashRef> { new() { Name = "stash@{0}", Hash = "s", Message = "WIP on main" } },
            };
        }

        [Test]
        public void Test_Build_GroupsFoldersAndRemotes()
        {
            List<RefNode> roots = RefTreeBuilder.Build(SampleRefs(), null, section => section.ToString());
            CollectionAssert.AreEqual(RefTreeBuilder.Sections.Select(s => s.ToString()).ToArray(), roots.Select(r => r.Label).ToArray());

            RefNode local = roots[0];
            Assert.AreEqual("3", local.Badge);
            Assert.AreEqual(RefNodeKind.Folder, local.Children[0].Kind, "folders come before leaves");
            Assert.AreEqual("feature", local.Children[0].Label);
            CollectionAssert.AreEqual(new[] { "spine-vat", "unity63" }, local.Children[0].Children.Select(c => c.Label).ToArray());
            RefNode current = local.Children[0].Children[1];
            Assert.IsTrue(current.IsCurrent);
            Assert.AreEqual("1\u2191", current.Badge);
            Assert.AreEqual("main", local.Children[1].Label);

            RefNode remote = roots[1];
            Assert.AreEqual("1", remote.Badge, "origin/HEAD is skipped");
            Assert.AreEqual(RefNodeKind.Remote, remote.Children[0].Kind);
            Assert.AreEqual("origin", remote.Children[0].Label);

            RefNode tags = roots.Single(r => r.Section == RefSection.Tags);
            Assert.AreEqual("release", tags.Children[0].Label);
            Assert.AreEqual("m", tags.Children[0].Children[0].Commit);
        }

        [Test]
        public void Test_Build_FilterKeepsMatchesAndAncestors()
        {
            List<RefNode> roots = RefTreeBuilder.Build(SampleRefs(), "VAT", section => section.ToString());
            // 걸린 것이 없는 구역(원격·stash 등)은 검색 중에 빠진다.
            RefNode local = roots.Single();
            Assert.AreEqual(RefSection.Local, local.Section);
            Assert.AreEqual("1", local.Badge);
            Assert.AreEqual("spine-vat", local.Children.Single().Children.Single().Label);
        }

        [Test]
        public void Test_Build_NoFilter_KeepsEmptySections()
        {
            List<RefNode> roots = RefTreeBuilder.Build(new GitRefSet(), null, section => section.ToString());

            Assert.AreEqual(RefTreeBuilder.Sections.Length, roots.Count);
            Assert.IsTrue(roots.All(r => r.Children.Count == 0));
        }

        [TestCase("section:Local", true)]
        [TestCase("section:Remote", true)]
        [TestCase("section:Tags", false)]
        [TestCase("section:Stashes", false)]
        [TestCase("folder:Local/feature", true)]
        public void Test_ExpandedByDefault(string id, bool expected)
        {
            Assert.AreEqual(expected, RefTreeBuilder.ExpandedByDefault(id));
        }

        [Test]
        public void Test_RefCommands()
        {
            Assert.AreEqual("tag -d v1", Joined(GitCommitCommands.DeleteTag("v1")));
            Assert.AreEqual("push origin refs/tags/v1", Joined(GitCommitCommands.PushTag("origin", "v1")));
            Assert.AreEqual("push origin --delete refs/tags/v1", Joined(GitCommitCommands.DeleteRemoteTag("origin", "v1")));
            Assert.AreEqual("stash apply stash@{1}", Joined(GitCommitCommands.StashApply("stash@{1}", drop: false)));
            Assert.AreEqual("stash pop stash@{1}", Joined(GitCommitCommands.StashApply("stash@{1}", drop: true)));
            Assert.AreEqual("stash drop stash@{0}", Joined(GitCommitCommands.StashDrop("stash@{0}")));
            Assert.AreEqual("stash branch fix stash@{0}", Joined(GitCommitCommands.StashBranch("fix", "stash@{0}")));
            Assert.AreEqual("worktree remove E:/wt", Joined(GitCommitCommands.RemoveWorktree("E:/wt")));
            Assert.AreEqual("submodule update --init --recursive -- Libs/Core", Joined(GitCommitCommands.UpdateSubmodule("Libs/Core")));
        }

        [TestCase("feature/login", BranchNameProblem.None)]
        [TestCase("", BranchNameProblem.Empty)]
        [TestCase("my branch", BranchNameProblem.Whitespace)]
        [TestCase("fix:crash", BranchNameProblem.InvalidCharacter)]
        [TestCase("-oops", BranchNameProblem.BadStart)]
        [TestCase("feature/", BranchNameProblem.BadEnd)]
        [TestCase("topic.lock", BranchNameProblem.BadEnd)]
        [TestCase("a..b", BranchNameProblem.BadSequence)]
        [TestCase("a//b", BranchNameProblem.BadSequence)]
        [TestCase("feature/.hidden", BranchNameProblem.DotComponent)]
        public void Test_BranchName_Check(string name, BranchNameProblem expected)
        {
            Assert.AreEqual(expected, GitBranchNames.Check(name, new[] { "master" }));
        }

        [Test]
        public void Test_BranchName_ExistingIgnoresCase()
        {
            Assert.AreEqual(BranchNameProblem.Exists, GitBranchNames.Check("Master", new[] { "master" }));
            Assert.AreEqual(BranchNameProblem.None, GitBranchNames.Check("master-2", new[] { "master" }));
        }
    }
}
