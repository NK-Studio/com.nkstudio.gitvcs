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

        [Test]
        public void Test_Build_TopDownHeadFirst_PutsCurrentLaneInFirstRow()
        {
            // A ─ B ─ C (master, 오래됨)
            //          └─ D ─ F (feature, 맨 위)
            //              └─ E (current, 작업 중 줄이 그 위에 있다)
            GitGraph graph = GitGraph.Build(new[] { Commit("A"), Commit("B", "A"), Commit("C", "B"), Commit("D", "C"), Commit("E", "D"), Commit("F", "D") }.Reverse().ToList(),
                new List<GitBranchRef> { Branch("master", "B", days: 1), Branch("current", "E", isCurrent: true, days: 5), Branch("feature", "F", days: 4) },
                "E", "merged", topDown: true, headFirst: true);

            Assert.AreEqual(0, LaneNamed(graph, "current").Row);
            Assert.AreEqual("current", graph.NodeByHash["C"].Lane.Name, "작업 중 줄이 있으면 지금 브랜치가 줄기를 차지한다");
            Assert.AreNotEqual(0, LaneNamed(graph, "feature").Row);
        }

        [Test]
        public void Test_Build_TopDownHeadFirst_KeepsColumnFreeAboveCurrentTip()
        {
            // 작업 중 줄(맨 위)에서 지금 브랜치 끝(C)까지 점선이 내려간다 — 그 위에 끝이 있는 다른 가지가 같은 열을 쓰면 안 된다.
            // A ─ B ─ C (current, 아래쪽)
            //      └─ D ─ E (develop, 위쪽)
            GitGraph graph = GitGraph.Build(new[] { Commit("A"), Commit("B", "A"), Commit("C", "B"), Commit("D", "B"), Commit("E", "D") }.Reverse().ToList(),
                new List<GitBranchRef> { Branch("current", "C", isCurrent: true, days: 3), Branch("develop", "E", days: 1) },
                "C", "merged", topDown: true, headFirst: true);

            Assert.AreEqual(0, LaneNamed(graph, "current").Row);
            Assert.AreNotEqual(0, LaneNamed(graph, "develop").Row);
        }

        [Test]
        public void Test_Build_TopDown_TopmostTipTakesFirstRowAndTrunk()
        {
            // GitKraken: 작업 중 줄이 없으면 맨 위 커밋의 브랜치가 맨 왼쪽 열과 줄기를 갖는다. 지금 브랜치라도 끝 커밋이 아래면 오른쪽 열.
            // A ─ B (base)
            //      ├─ C ─ D (current)
            //      └─ E (feature, 맨 위)
            GitGraph graph = GitGraph.Build(new[] { Commit("A"), Commit("B", "A"), Commit("C", "B"), Commit("D", "C"), Commit("E", "B") }.Reverse().ToList(),
                new List<GitBranchRef> { Branch("current", "D", isCurrent: true, days: 2), Branch("feature", "E", days: 1) },
                "D", "merged", topDown: true);

            Assert.AreEqual(0, LaneNamed(graph, "feature").Row);
            Assert.AreEqual("feature", graph.NodeByHash["B"].Lane.Name);
            Assert.AreEqual(1, LaneNamed(graph, "current").Row);
        }

        [Test]
        public void Test_Build_TopDown_MergedBranchOpensNextToItsMerge()
        {
            // GitKraken: M(main) 이 X2 를 병합하면 병합 줄에서 바로 옆 빈 열을 잡는다 — 그 아래 끝 커밋 T 보다 먼저.
            GitGraph graph = TopDown(
                new[] { Commit("A"), Commit("X1", "A"), Commit("X2", "X1"), Commit("T", "A"), Commit("B", "A"), Commit("M", "B", "X2") },
                new[] { Branch("main", "M", isCurrent: true, days: 2), Branch("topic", "T", days: 1) }, "M");

            Assert.AreEqual(0, graph.NodeByHash["M"].Lane.Row);
            Assert.AreEqual(1, graph.NodeByHash["X2"].Lane.Row);
            Assert.AreSame(graph.NodeByHash["X2"].Lane, graph.NodeByHash["X1"].Lane);
            Assert.AreEqual(2, graph.NodeByHash["T"].Lane.Row);
            Assert.AreEqual(3, graph.RowCount);
        }

        [Test]
        public void Test_Build_TopDown_ColumnIsReusedRightAfterBranchEnds()
        {
            // f1 은 C 줄에서 끝나고, 바로 아래 줄의 g1 이 그 열을 이어 쓴다(여유 칸 없이 — GitKraken 과 같다).
            GitGraph graph = TopDown(
                new[] { Commit("A"), Commit("B", "A"), Commit("g1", "A"), Commit("C", "B"), Commit("f1", "C"), Commit("D", "C") },
                new[] { Branch("main", "D", isCurrent: true, days: 3), Branch("f", "f1", days: 2), Branch("g", "g1", days: 1) }, "D");

            Assert.AreEqual(1, graph.NodeByHash["f1"].Lane.Row);
            Assert.AreEqual(1, graph.NodeByHash["g1"].Lane.Row);
            Assert.AreEqual(0, graph.NodeByHash["A"].Lane.Row);
            Assert.AreEqual(2, graph.RowCount);
        }

        [Test]
        public void Test_Build_TopDown_ParentGoesToLeftmostBranchChild()
        {
            // X(1열)가 먼저 P 를 잡아도 아래의 0열 자식 Y1 이 P 를 데려간다 — X 의 열은 P 줄에서 꺾여 끝난다.
            GitGraph graph = TopDown(
                new[] { Commit("P"), Commit("Y1", "P"), Commit("X", "P"), Commit("Y2", "Y1") },
                new[] { Branch("y", "Y2", isCurrent: true, days: 2), Branch("x", "X", days: 1) }, "Y2");

            Assert.AreEqual(0, graph.NodeByHash["Y2"].Lane.Row);
            Assert.AreEqual(1, graph.NodeByHash["X"].Lane.Row);
            Assert.AreSame(graph.NodeByHash["Y1"].Lane, graph.NodeByHash["P"].Lane);
            Assert.AreEqual(0, graph.NodeByHash["P"].Lane.Row);
        }

        [Test]
        public void Test_Build_TopDown_LaneTipLabelFollowsBranchNotColumn()
        {
            // 열은 GitKraken 규칙으로 다시 짜도, 가지 끝 이름표는 브랜치 끝 커밋에만 붙는다.
            GitGraph graph = TopDown(
                new[] { Commit("A"), Commit("X1", "A"), Commit("B", "A"), Commit("M", "B", "X1") },
                new[] { Branch("main", "M", isCurrent: true) }, "M");

            Assert.AreEqual("main", graph.NodeByHash["M"].BranchLane.Name);
            Assert.AreEqual("main", graph.NodeByHash["A"].BranchLane.Name);
            Assert.IsTrue(graph.NodeByHash["X1"].BranchLane.IsAnonymous);
        }

        [Test]
        public void Test_Build_TopDown_StashOnBranchTipContinuesItsColumn()
        {
            GitGraph graph = TopDown(new[] { Commit("A"), Commit("B", "A"), Stash("S", "stash@{0}", "B") },
                new[] { Branch("main", "B", isCurrent: true, days: 1) }, "B");

            GitGraphLane lane = graph.NodeByHash["S"].Lane;
            Assert.IsTrue(lane.IsStash);
            Assert.AreEqual("stash@{0}", lane.Name);
            Assert.AreEqual(graph.NodeByHash["B"].Lane.Row, lane.Row);
            Assert.AreEqual(0, graph.NodeByHash["S"].RefLabels.Count);
            Assert.AreEqual(1, graph.RowCount);
        }

        [Test]
        public void Test_Build_TopDown_StashDoesNotPushBranchLanes()
        {
            // B 에서 stash 한 뒤 C 를 커밋: stash 는 main 줄기를 밀어내지 않고 옆 열에.
            GitGraph graph = TopDown(new[] { Commit("A"), Commit("B", "A"), Commit("C", "B"), Stash("S", "stash@{0}", "B") },
                new[] { Branch("main", "C", isCurrent: true, days: 1) }, "C");
            Assert.AreEqual(0, graph.NodeByHash["C"].Lane.Row);
            Assert.AreEqual(1, graph.NodeByHash["S"].Lane.Row);

            // 같은 끝에서 만든 stash 둘: 새 것(위)이 이어 쓰고 다른 하나는 옆 열.
            graph = TopDown(new[] { Commit("A"), Stash("S1", "stash@{1}", "A"), Stash("S0", "stash@{0}", "A") },
                new[] { Branch("main", "A", isCurrent: true, days: 1) }, "A");
            Assert.AreEqual(0, graph.NodeByHash["S0"].Lane.Row);
            Assert.AreEqual(1, graph.NodeByHash["S1"].Lane.Row);

            // 옆 가지 끝(F) 바로 위는 비어 있으면 쓴다.
            graph = TopDown(new[] { Commit("A"), Commit("F", "A"), Commit("C", "A"), Stash("S1", "stash@{1}", "C"), Stash("S0", "stash@{0}", "C") },
                new[] { Branch("main", "C", isCurrent: true, days: 1), Branch("feature", "F", days: 2) }, "C");
            Assert.AreEqual(0, graph.NodeByHash["C"].Lane.Row);
            Assert.AreEqual(1, graph.NodeByHash["F"].Lane.Row);
            Assert.AreEqual(0, graph.NodeByHash["S0"].Lane.Row);
            Assert.AreEqual(1, graph.NodeByHash["S1"].Lane.Row);
            Assert.AreEqual(2, graph.RowCount);
        }

        [Test]
        public void Test_Build_TopDownHeadFirst_StashLeavesCurrentColumnToWorkInProgress()
        {
            GitGraph graph = TopDown(new[] { Commit("A"), Commit("B", "A"), Stash("S", "stash@{0}", "B") },
                new[] { Branch("main", "B", isCurrent: true, days: 1) }, "B", headFirst: true);

            Assert.AreEqual(0, graph.NodeByHash["B"].Lane.Row);
            Assert.AreEqual(1, graph.NodeByHash["S"].Lane.Row);
        }

        [Test]
        public void Test_InsertStashes_GoAboveTheirBaseInTimeOrder()
        {
            List<GitCommitInfo> commits = new() { At("c3", 30, "c2"), At("c2", 20, "c1"), At("c1", 10) };
            List<(string, GitCommitInfo)> stashes = new()
            {
                // 만든 뒤 c3 가 커밋됐다.
                ("stash@{0}", At("s0", 25, "c2", "i0", "u0")),
                // 시각이 바탕 커밋보다 이르게 찍혀도 바탕 위.
                ("stash@{1}", At("s1", 5, "c1", "i1")),
                // 바탕이 읽은 범위 밖: 시각으로.
                ("stash@{2}", At("s2", 15, "gone")),
            };
            GitHistory.InsertStashes(commits, stashes, truncated: false);
            CollectionAssert.AreEqual(new[] { "c3", "s0", "c2", "s2", "s1", "c1" }, commits.Select(c => c.Hash).ToArray());
            CollectionAssert.AreEqual(new[] { "c2" }, commits[1].Parents, "index·새 파일 부모는 버린다");
            Assert.AreEqual("stash@{0}", commits[1].StashName);
            Assert.IsFalse(commits[0].IsStash);

            // 같은 시각의 stash 둘은 새 것이 위, 둘 다 바탕 위.
            commits = new List<GitCommitInfo> { At("c1", 10) };
            GitHistory.InsertStashes(commits, new List<(string, GitCommitInfo)> { ("stash@{0}", At("s0", 10, "c1")), ("stash@{1}", At("s1", 10, "c1")) }, truncated: false);
            CollectionAssert.AreEqual(new[] { "s0", "s1", "c1" }, commits.Select(c => c.Hash).ToArray());

            // 잘린 히스토리보다 오래되고 바탕도 없으면 뺀다.
            commits = new List<GitCommitInfo> { At("c2", 20, "c1") };
            GitHistory.InsertStashes(commits, new List<(string, GitCommitInfo)> { ("stash@{0}", At("s0", 5, "old")) }, truncated: true);
            CollectionAssert.AreEqual(new[] { "c2" }, commits.Select(c => c.Hash).ToArray());
        }

        private static GitCommitInfo At(string hash, int minutes, params string[] parents)
        {
            GitCommitInfo commit = Commit(hash, parents);
            commit.Date = BaseDate.AddMinutes(minutes);
            return commit;
        }

        private static GitCommitInfo Stash(string hash, string name, string baseHash)
        {
            GitCommitInfo commit = Commit(hash, baseHash);
            commit.StashName = name;
            return commit;
        }

        private static GitGraph TopDown(GitCommitInfo[] commitsOldestFirst, GitBranchRef[] branches, string headHash, bool headFirst = false)
        {
            return GitGraph.Build(commitsOldestFirst.Reverse().ToList(), branches.ToList(), headHash, "merged", topDown: true, headFirst: headFirst);
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
