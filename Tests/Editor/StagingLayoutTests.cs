using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// Fork·GitKraken 레이아웃의 순수 규칙: 상태 나누기, .meta 짝, 커밋 메시지 나누기·합치기, 세로 그래프 배치, 인덱스 diff 인자.
    /// </summary>
    public sealed class StagingLayoutTests
    {
        private static GitStatusEntry Entry(string path, char index, char workTree, string original = null)
        {
            return new GitStatusEntry
            {
                RepoPath = path,
                OriginalRepoPath = original,
                IndexCode = index,
                WorkTreeCode = workTree,
                State = GitStatusParser.ToState(index, workTree),
            };
        }

        [Test]
        public void Test_Split_PartlyStagedFile_AppearsOnBothSidesWithOwnState()
        {
            List<GitStatusEntry> entries = new() { Entry("a.cs", 'A', 'M') };

            Assert.AreEqual(GitFileState.Added, GitStaging.Staged(entries).Single().State);
            Assert.AreEqual(GitFileState.Modified, GitStaging.Unstaged(entries).Single().State);
        }

        [Test]
        public void Test_Split_UntrackedAndConflicted_OnlyUnstaged()
        {
            List<GitStatusEntry> entries = new() { Entry("new.cs", '?', '?'), Entry("both.cs", 'U', 'U') };

            Assert.AreEqual(0, GitStaging.Staged(entries).Count);
            CollectionAssert.AreEquivalent(new[] { GitFileState.Untracked, GitFileState.Conflicted }, GitStaging.Unstaged(entries).Select(e => e.State));
        }

        [Test]
        public void Test_Split_StagedRename_KeepsOriginalOnlyOnStagedSide()
        {
            List<GitStatusEntry> entries = new() { Entry("b.cs", 'R', 'M', original: "a.cs") };

            Assert.AreEqual("a.cs", GitStaging.Staged(entries).Single().OriginalRepoPath);
            Assert.IsNull(GitStaging.Unstaged(entries).Single().OriginalRepoPath);
        }

        [Test]
        public void Test_Split_FullyStaged_NotInUnstaged()
        {
            List<GitStatusEntry> entries = new() { Entry("a.cs", 'M', ' ') };

            Assert.AreEqual(1, GitStaging.Staged(entries).Count);
            Assert.AreEqual(0, GitStaging.Unstaged(entries).Count);
        }

        [Test]
        public void Test_WithMetaPairs_AddsPairFromSameSideOnce()
        {
            List<GitStatusEntry> side = new() { Entry("A.cs", ' ', 'M'), Entry("A.cs.meta", ' ', 'M'), Entry("B.cs", ' ', 'M') };

            List<GitStatusEntry> result = GitStaging.WithMetaPairs(new[] { side[1], side[0] }, side);

            CollectionAssert.AreEqual(new[] { "A.cs.meta", "A.cs" }, result.Select(e => e.RepoPath));
        }

        [Test]
        public void Test_WithMetaPairs_NoPairOnSide_KeepsOnlyPicked()
        {
            List<GitStatusEntry> side = new() { Entry("B.cs", ' ', 'M') };

            Assert.AreEqual(1, GitStaging.WithMetaPairs(side, side).Count);
        }

        [TestCase("Fix: a", "Fix: a", "")]
        [TestCase("Fix: a\n\nbody line", "Fix: a", "body line")]
        [TestCase("Fix: a\r\n\r\nbody", "Fix: a", "body")]
        public void Test_CommitBox_SplitMessage_SubjectAndBody(string message, string subject, string description)
        {
            Assert.AreEqual((subject, description), CommitBox.SplitMessage(message));
        }

        [Test]
        public void Test_CommitBox_JoinMessage_BlankLineBetween()
        {
            Assert.AreEqual("Fix: a\n\nbody", CommitBox.JoinMessage(" Fix: a ", "body\n"));
            Assert.AreEqual("Fix: a", CommitBox.JoinMessage("Fix: a", "  "));
        }

        [Test]
        public void Test_VerticalGraph_NewestOnTopBelowWip()
        {
            GitGraph graph = GitGraph.Build(new List<GitCommitInfo>
            {
                new() { Hash = "c", ShortHash = "c", Parents = new[] { "b" }, Subject = "c" },
                new() { Hash = "b", ShortHash = "b", Parents = new[] { "a" }, Subject = "b" },
                new() { Hash = "a", ShortHash = "a", Parents = new string[0], Subject = "a" },
            }, new List<GitBranchRef> { new() { Name = "main", RefName = "refs/heads/main", Hash = "c", IsCurrent = true } }, "c");

            VerticalGraphLayout layout = VerticalGraphLayout.Build(graph, hasWip: true);

            Assert.AreEqual(4, layout.RowCount);
            Assert.AreEqual("c", layout.NodesNewestFirst[0].Commit.Hash);
            Assert.AreEqual(1, layout.RowOf[graph.NodeByHash["c"]]);
            Assert.AreEqual(3, layout.RowOf[graph.NodeByHash["a"]]);
            Assert.GreaterOrEqual(layout.Width, VerticalGraphLayout.MinWidth);
        }

        [Test]
        public void Test_DiffArguments_Staged_UsesCached()
        {
            GitDiffTarget target = GitDiffTarget.ForStaging(Entry("a.cs", 'M', ' '), staged: true);

            List<string> args = GitDiff.BuildArguments(target, new GitDiffOptions(DiffAlgorithm.Myers, DiffWhitespace.Show, 3));

            CollectionAssert.Contains(args, "--cached");
            CollectionAssert.DoesNotContain(args, "HEAD");
        }

        [Test]
        public void Test_DiffArguments_Unstaged_ComparesIndexWithoutHead()
        {
            GitDiffTarget target = GitDiffTarget.ForStaging(Entry("a.cs", ' ', 'M'), staged: false);

            List<string> args = GitDiff.BuildArguments(target, new GitDiffOptions(DiffAlgorithm.Myers, DiffWhitespace.Show, 3));

            CollectionAssert.DoesNotContain(args, "--cached");
            CollectionAssert.DoesNotContain(args, "HEAD");
            Assert.AreEqual("a.cs", args.Last());
        }

        private static string Codes(GitStatusEntry entry)
        {
            return $"{entry.IndexCode}{entry.WorkTreeCode}";
        }

        [Test]
        public void Test_PredictStage_MovesWorkTreeChangesToIndex()
        {
            List<GitStatusEntry> entries = new()
            {
                Entry("new.cs", '?', '?'),
                Entry("mod.cs", ' ', 'M'),
                Entry("gone.cs", ' ', 'D'),
                Entry("added.cs", 'A', 'M'),
            };

            List<GitStatusEntry> predicted = GitStaging.PredictStage(entries, null);

            CollectionAssert.AreEqual(new[] { "A ", "M ", "D ", "A " }, predicted.Select(Codes));
            Assert.AreEqual(GitFileState.Added, predicted[0].State);
            Assert.AreEqual(0, GitStaging.Unstaged(predicted).Count);
        }

        [Test]
        public void Test_PredictStage_OnlyGivenPaths_LeavesOthersAndConflicts()
        {
            List<GitStatusEntry> entries = new() { Entry("a.cs", ' ', 'M'), Entry("b.cs", ' ', 'M'), Entry("both.cs", 'U', 'U') };

            List<GitStatusEntry> predicted = GitStaging.PredictStage(entries, new HashSet<string> { "a.cs", "both.cs" });

            CollectionAssert.AreEqual(new[] { "M ", " M", "UU" }, predicted.Select(Codes));
        }

        [Test]
        public void Test_PredictStage_AddedThenDeleted_Disappears()
        {
            List<GitStatusEntry> entries = new() { Entry("tmp.cs", 'A', 'D') };

            Assert.AreEqual(0, GitStaging.PredictStage(entries, null).Count);
        }

        [Test]
        public void Test_PredictUnstage_ResetsIndexToHead()
        {
            List<GitStatusEntry> entries = new()
            {
                Entry("new.cs", 'A', ' '),
                Entry("mod.cs", 'M', 'M'),
                Entry("gone.cs", 'D', ' '),
                Entry("both.cs", 'M', 'D'),
            };

            List<GitStatusEntry> predicted = GitStaging.PredictUnstage(entries, null);

            CollectionAssert.AreEqual(new[] { "??", " M", " D", " D" }, predicted.Select(Codes));
            Assert.AreEqual(GitFileState.Untracked, predicted[0].State);
            Assert.AreEqual(0, GitStaging.Staged(predicted).Count);
        }

        [Test]
        public void Test_PredictUnstage_RenameIsLeftForRealStatus()
        {
            List<GitStatusEntry> entries = new() { Entry("b.cs", 'R', ' ', original: "a.cs") };

            GitStatusEntry predicted = GitStaging.PredictUnstage(entries, null).Single();

            Assert.AreEqual("R ", Codes(predicted));
            Assert.AreEqual("a.cs", predicted.OriginalRepoPath);
        }
    }
}
