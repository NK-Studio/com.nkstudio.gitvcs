using System.Collections.Generic;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 세로 그래프(GitKraken 형태)의 표시 규칙: 작업 중 줄의 종류별 개수, 브랜치/태그 칸의 이름표 분류.
    /// </summary>
    public sealed class CommitGraphLookTests
    {
        private static GitStatusEntry Entry(GitFileState state)
        {
            return new GitStatusEntry { RepoPath = "a.cs", State = state };
        }

        [Test]
        public void Test_WipCounts_From_SortsByChangeKind()
        {
            List<GitStatusEntry> entries = new()
            {
                Entry(GitFileState.Modified),
                Entry(GitFileState.Conflicted),
                Entry(GitFileState.Renamed),
                Entry(GitFileState.Added),
                Entry(GitFileState.Untracked),
                Entry(GitFileState.Deleted),
            };

            WipCounts counts = WipCounts.From(entries);

            Assert.AreEqual(3, counts.Modified);
            Assert.AreEqual(2, counts.Added);
            Assert.AreEqual(1, counts.Deleted);
            Assert.AreEqual(6, counts.Total);
        }

        [TestCase("tag: v1.0", "v1.0", CommitRefKind.Tag)]
        [TestCase("origin/main", "origin/main", CommitRefKind.Remote)]
        [TestCase("feature/x", "feature/x", CommitRefKind.Local)]
        public void Test_CommitRefs_Classify(string label, string name, CommitRefKind kind)
        {
            CommitRef commitRef = CommitRefs.Classify(label);

            Assert.AreEqual(name, commitRef.Name);
            Assert.AreEqual(kind, commitRef.Kind);
        }

        [Test]
        public void Test_CommitRefs_Of_LaneTipFirstThenOtherRefs()
        {
            GitGraphLane lane = new() { Name = "main", IsCurrent = true, LastColumn = 2 };
            GitGraphNode node = new() { Column = 2, Lane = lane, RefLabels = new List<string> { "origin/main", "tag: v1" } };

            IReadOnlyList<CommitRef> refs = CommitRefs.Of(node);

            Assert.AreEqual(3, refs.Count);
            Assert.AreEqual(CommitRefKind.Current, refs[0].Kind);
            Assert.AreEqual("main", refs[0].Name);
            Assert.AreEqual(CommitRefKind.Remote, refs[1].Kind);
            Assert.AreEqual(CommitRefKind.Tag, refs[2].Kind);
        }

        [Test]
        public void Test_CommitRefs_Of_HeadBranchNotLane_IsCurrentAndFirst()
        {
            GitGraphLane lane = new() { Name = "master", LastColumn = 2 };
            GitGraphNode node = new() { Column = 2, Lane = lane, RefLabels = new List<string> { "feature/x" }, HeadBranch = "feature/x" };

            IReadOnlyList<CommitRef> refs = CommitRefs.Of(node);

            Assert.AreEqual(2, refs.Count);
            Assert.AreEqual("feature/x", refs[0].Name);
            Assert.AreEqual(CommitRefKind.Current, refs[0].Kind);
            Assert.AreEqual("master", refs[1].Name);
            Assert.AreEqual(CommitRefKind.Local, refs[1].Kind);
        }

        [Test]
        public void Test_CommitRefs_Of_MiddleOfLane_HasNoLaneName()
        {
            GitGraphLane lane = new() { Name = "main", LastColumn = 5 };
            GitGraphNode node = new() { Column = 2, Lane = lane };

            Assert.AreEqual(0, CommitRefs.Of(node).Count);
        }
    }
}
