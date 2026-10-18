using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// <see cref="GitGraph.Build"/> 의 줄 배정·행 공유·참조 표시 규칙. 커밋은 오래된 것부터 적고 <see cref="Build"/> 에서 뒤집어 넘긴다
    /// (<c>git log --date-order</c> 는 새 커밋이 앞).
    /// </summary>
    public sealed class GitGraphTests
    {
        private static readonly DateTime BaseDate = new(2026, 1, 1);

        [Test]
        public void Test_Build_ColumnsAreOldestFirst()
        {
            GitGraph graph = Build(
                new[] { Commit("A"), Commit("B", "A"), Commit("C", "B") },
                new[] { Branch("main", "C") },
                "C");

            Assert.AreEqual(3, graph.Nodes.Count);
            Assert.AreEqual(0, graph.NodeByHash["A"].Column);
            Assert.AreEqual(1, graph.NodeByHash["B"].Column);
            Assert.AreEqual(2, graph.NodeByHash["C"].Column);
        }

        [Test]
        public void Test_Build_MainLaneTakesSharedHistoryBeforeCurrentBranch()
        {
            // A ─ B ─ C (main)
            //      └─ F1 ─ F2 (feature, 현재·더 최근)
            GitGraph graph = Build(
                new[] { Commit("A"), Commit("B", "A"), Commit("F1", "B"), Commit("F2", "F1"), Commit("C", "B") },
                new[] { Branch("feature", "F2", isCurrent: true, days: 10), Branch("main", "C", days: 1) },
                "F2");

            GitGraphLane main = LaneNamed(graph, "main");
            GitGraphLane feature = LaneNamed(graph, "feature");

            Assert.AreSame(main, graph.Lanes[0]);
            Assert.AreSame(main, graph.NodeByHash["A"].Lane);
            Assert.AreSame(main, graph.NodeByHash["B"].Lane);
            Assert.AreSame(main, graph.NodeByHash["C"].Lane);
            Assert.AreSame(feature, graph.NodeByHash["F1"].Lane);
            Assert.AreSame(feature, graph.NodeByHash["F2"].Lane);
            Assert.IsTrue(feature.IsCurrent);
            Assert.IsFalse(feature.IsAnonymous);
            Assert.AreEqual(2, feature.FirstColumn);
            Assert.AreEqual(3, feature.LastColumn);
            Assert.AreEqual(0, main.Row);
            Assert.AreEqual(1, feature.Row);
        }

        [Test]
        public void Test_Build_MasterIsAlsoPreferredOverOtherLocalBranches()
        {
            GitGraph graph = Build(
                new[] { Commit("A"), Commit("B", "A"), Commit("X", "B") },
                new[] { Branch("aaa", "X", days: 10), Branch("master", "B", days: 1) },
                "X");

            Assert.AreEqual("master", graph.Lanes[0].Name);
            Assert.AreEqual("master", graph.NodeByHash["A"].Lane.Name);
            Assert.AreEqual("aaa", graph.NodeByHash["X"].Lane.Name);
        }

        [Test]
        public void Test_Build_RemoteMainDoesNotStealLocalMainCommits()
        {
            GitGraph graph = Build(
                new[] { Commit("A"), Commit("B", "A"), Commit("C", "B") },
                new[] { Branch("origin/main", "C", isRemote: true, days: 10), Branch("main", "B", days: 1) },
                "B");

            Assert.AreEqual("main", graph.NodeByHash["B"].Lane.Name);
            Assert.AreEqual("main", graph.NodeByHash["A"].Lane.Name);
            GitGraphLane remote = graph.NodeByHash["C"].Lane;
            Assert.AreEqual("origin/main", remote.Name);
            Assert.IsTrue(remote.IsRemote);
        }

        [Test]
        public void Test_Build_BranchWithoutNewCommits_HasNoLane()
        {
            GitGraph graph = Build(
                new[] { Commit("A"), Commit("B", "A") },
                new[] { Branch("main", "B"), Branch("origin/main", "A", isRemote: true) },
                "B");

            Assert.AreEqual(1, graph.Lanes.Count);
            Assert.AreEqual("main", graph.Lanes[0].Name);
        }

        [Test]
        public void Test_Build_BranchWithoutHashIsIgnored()
        {
            GitGraph graph = Build(
                new[] { Commit("A") },
                new[] { new GitBranchRef { Name = "main", Hash = null } },
                "A");

            Assert.AreEqual(1, graph.Lanes.Count);
            Assert.IsTrue(graph.Lanes[0].IsAnonymous);
        }

        [TestCase("Merge branch 'feature/old'", "feature/old")]
        [TestCase("Merge branch 'hotfix' into main", "hotfix")]
        [TestCase("Merge remote-tracking branch 'origin/dev'", "origin/dev")]
        [TestCase("Merge pull request #12 from someone/topic", null)]
        public void Test_Build_DeletedMergedBranch_NameGuessedFromMergeMessage(string mergeSubject, string expectedName)
        {
            // A ─ B ───── M (main)
            //  └ X1 ─ X2 ┘ (지워진 브랜치)
            GitGraph graph = Build(
                new[] { Commit("A"), Commit("X1", "A"), Commit("X2", "X1"), Commit("B", "A"), CommitWithSubject("M", mergeSubject, "B", "X2") },
                new[] { Branch("main", "M") },
                "M");

            GitGraphLane anonymous = graph.NodeByHash["X2"].Lane;
            Assert.AreSame(anonymous, graph.NodeByHash["X1"].Lane);
            Assert.IsTrue(anonymous.IsAnonymous);
            // null 이면 메시지에서 이름을 못 찾은 경우. 기본 이름은 표시 언어를 따르므로 고아 커밋 줄의 이름과 같은지만 본다.
            Assert.AreEqual(expectedName ?? DefaultAnonymousName(), anonymous.Name);
            Assert.AreEqual(2, graph.Lanes.Count);

            GitGraphEdge mergeEdge = graph.Edges.Single(e => e.Child.Commit.Hash == "M" && e.Parent.Commit.Hash == "X2");
            Assert.IsTrue(mergeEdge.IsMergeEdge);
            GitGraphEdge firstParentEdge = graph.Edges.Single(e => e.Child.Commit.Hash == "M" && e.Parent.Commit.Hash == "B");
            Assert.IsFalse(firstParentEdge.IsMergeEdge);
        }

        [Test]
        public void Test_Build_UnmergedOrphanCommit_GetsDefaultAnonymousName()
        {
            GitGraph graph = Build(
                new[] { Commit("A"), Commit("Z", "A"), Commit("B", "A") },
                new[] { Branch("main", "B") },
                "B");

            GitGraphLane lane = graph.NodeByHash["Z"].Lane;
            Assert.IsTrue(lane.IsAnonymous);
            Assert.IsFalse(string.IsNullOrEmpty(lane.Name));
            Assert.AreNotEqual("main", lane.Name);
        }

        [Test]
        public void Test_Build_NonOverlappingLanes_ShareRow()
        {
            // 열: A0 a1 a2 B3 C4 D5 b1 b2 E8. feature/a = [1,2], feature/b = [6,7] → 여백을 둬도 겹치지 않는다.
            GitGraph graph = Build(
                new[]
                {
                    Commit("A"), Commit("a1", "A"), Commit("a2", "a1"), Commit("B", "A"), Commit("C", "B"),
                    Commit("D", "C"), Commit("b1", "D"), Commit("b2", "b1"), Commit("E", "D"),
                },
                new[] { Branch("main", "E"), Branch("feature/a", "a2", days: 2), Branch("feature/b", "b2", days: 1) },
                "E");

            GitGraphLane a = LaneNamed(graph, "feature/a");
            GitGraphLane b = LaneNamed(graph, "feature/b");
            Assert.AreEqual(1, a.Row);
            Assert.AreEqual(1, b.Row);
            Assert.AreEqual(2, graph.RowCount);
        }

        [Test]
        public void Test_Build_LanesWithinMargin_DoNotShareRow()
        {
            // 열: A0 a1 a2 B3 c1 c2 C6. feature/a = [1,2], feature/c = [4,5] → 앞뒤 한 칸 여백이 맞닿아 다른 행.
            GitGraph graph = Build(
                new[] { Commit("A"), Commit("a1", "A"), Commit("a2", "a1"), Commit("B", "A"), Commit("c1", "B"), Commit("c2", "c1"), Commit("C", "B") },
                new[] { Branch("main", "C"), Branch("feature/a", "a2", days: 2), Branch("feature/c", "c2", days: 1) },
                "C");

            GitGraphLane a = LaneNamed(graph, "feature/a");
            GitGraphLane c = LaneNamed(graph, "feature/c");
            Assert.AreEqual(1, a.Row);
            Assert.AreEqual(2, c.Row);
            Assert.AreEqual(3, graph.RowCount);
        }

        [Test]
        public void Test_Build_FirstParentOutsideRange_IsTruncated()
        {
            // A 의 첫 부모 "old" 와 M 의 두 번째 부모 "gone" 은 기간 밖이라 목록에 없다.
            GitGraph graph = Build(
                new[] { Commit("A", "old"), Commit("B", "A"), Commit("M", "B", "gone"), Commit("R") },
                new[] { Branch("main", "M") },
                "M");

            Assert.IsTrue(graph.TruncatedNodes.Contains(graph.NodeByHash["A"]));
            Assert.IsFalse(graph.TruncatedNodes.Contains(graph.NodeByHash["M"]));
            Assert.IsFalse(graph.TruncatedNodes.Contains(graph.NodeByHash["B"]));
            Assert.IsFalse(graph.TruncatedNodes.Contains(graph.NodeByHash["R"]));
            Assert.AreEqual(2, graph.Edges.Count);
        }

        [Test]
        public void Test_Build_MarksHeadNodeOnly()
        {
            GitGraph graph = Build(
                new[] { Commit("A"), Commit("B", "A"), Commit("C", "B") },
                new[] { Branch("main", "C") },
                "B");

            Assert.IsFalse(graph.NodeByHash["A"].IsHead);
            Assert.IsTrue(graph.NodeByHash["B"].IsHead);
            Assert.IsFalse(graph.NodeByHash["C"].IsHead);
        }

        [Test]
        public void Test_Build_RefLabels_SkipLaneNameAtTipAndKeepTagPrefix()
        {
            GitGraph graph = Build(
                new[]
                {
                    Commit("A"),
                    CommitWithRefs("B", "origin/main, tag: v0.1", "A"),
                    CommitWithRefs("C", "HEAD -> main, origin/HEAD, tag: v1.0, release", "B"),
                },
                new[] { Branch("main", "C"), Branch("origin/main", "B", isRemote: true) },
                "C");

            CollectionAssert.AreEqual(new[] { "tag: v1.0", "release" }, graph.NodeByHash["C"].RefLabels);
            CollectionAssert.AreEqual(new[] { "origin/main", "tag: v0.1" }, graph.NodeByHash["B"].RefLabels);
            Assert.AreEqual(0, graph.NodeByHash["A"].RefLabels.Count);
        }

        [Test]
        public void Test_Build_RefLabels_LaneNameBeforeTipIsKept()
        {
            // 줄 이름과 같은 참조라도 줄이 더 이어지는(끝이 아닌) 커밋이면 다시 붙인다.
            GitGraph graph = Build(
                new[] { Commit("A"), CommitWithRefs("B", "main", "A"), Commit("C", "B") },
                new[] { Branch("main", "C") },
                "C");

            CollectionAssert.AreEqual(new[] { "main" }, graph.NodeByHash["B"].RefLabels);
        }

        [Test]
        public void Test_Build_DetachedHeadRef_IsNotLabeled()
        {
            GitGraph graph = Build(
                new[] { Commit("A"), CommitWithRefs("B", "HEAD", "A") },
                new[] { Branch("main", "A") },
                "B");

            Assert.AreEqual(0, graph.NodeByHash["B"].RefLabels.Count);
        }

        private static GitGraph Build(GitCommitInfo[] commitsOldestFirst, GitBranchRef[] branches, string headHash)
        {
            List<GitCommitInfo> newestFirst = commitsOldestFirst.Reverse().ToList();
            return GitGraph.Build(newestFirst, branches.ToList(), headHash);
        }

        /// <summary>병합 메시지 없이 남은 커밋이 받는 기본 줄 이름.</summary>
        private static string DefaultAnonymousName()
        {
            GitGraph graph = Build(new[] { Commit("A"), Commit("Z", "A"), Commit("B", "A") }, new[] { Branch("main", "B") }, "B");
            return graph.NodeByHash["Z"].Lane.Name;
        }

        private static GitGraphLane LaneNamed(GitGraph graph, string name)
        {
            return graph.Lanes.Single(l => l.Name == name);
        }

        private static GitCommitInfo Commit(string hash, params string[] parents)
        {
            return CommitWith(hash, string.Empty, $"commit {hash}", parents);
        }

        private static GitCommitInfo CommitWithSubject(string hash, string subject, params string[] parents)
        {
            return CommitWith(hash, string.Empty, subject, parents);
        }

        private static GitCommitInfo CommitWithRefs(string hash, string refs, params string[] parents)
        {
            return CommitWith(hash, refs, $"commit {hash}", parents);
        }

        private static GitCommitInfo CommitWith(string hash, string refs, string subject, string[] parents)
        {
            return new GitCommitInfo
            {
                Hash = hash,
                ShortHash = hash,
                Parents = parents,
                AuthorName = "tester",
                AuthorEmail = "tester@example.com",
                Date = BaseDate,
                Refs = refs,
                Subject = subject,
            };
        }

        private static GitBranchRef Branch(string name, string hash, bool isRemote = false, bool isCurrent = false, int days = 0)
        {
            return new GitBranchRef
            {
                Name = name,
                RefName = (isRemote ? "refs/remotes/" : "refs/heads/") + name,
                Hash = hash,
                ShortHash = hash,
                IsRemote = isRemote,
                IsCurrent = isCurrent,
                Date = BaseDate.AddDays(days),
            };
        }
    }
}
