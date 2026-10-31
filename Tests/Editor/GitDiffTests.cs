using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// diff 인자 만들기, unified diff 해석, 줄 안 강조, 나란히 보기 짝짓기.
    /// </summary>
    public sealed class GitDiffTests
    {
        private static readonly GitDiffOptions DefaultOptions = new(DiffAlgorithm.Histogram, DiffWhitespace.Show, 3);

        private static string Args(GitDiffTarget target, GitDiffOptions options)
        {
            return string.Join(" ", GitDiff.BuildArguments(target, options));
        }

        [Test]
        public void Test_BuildArguments_WorkingTree_ComparesHeadForPath()
        {
            GitDiffTarget target = new() { Source = GitDiffSource.WorkingTree, RepoPath = "Assets/A.cs", State = GitFileState.Modified };

            Assert.AreEqual("diff -M --no-color --no-ext-diff --no-textconv --diff-algorithm=histogram -U3 HEAD -- Assets/A.cs", Args(target, DefaultOptions));
        }

        [Test]
        public void Test_BuildArguments_Untracked_UsesNoIndexAgainstDevNull()
        {
            GitDiffTarget target = new() { Source = GitDiffSource.WorkingTree, RepoPath = "Assets/New.cs", State = GitFileState.Untracked };

            string args = Args(target, DefaultOptions);

            StringAssert.StartsWith("diff --no-index", args);
            StringAssert.EndsWith("-- /dev/null Assets/New.cs", args);
        }

        [Test]
        public void Test_BuildArguments_Rename_PassesBothPaths()
        {
            GitDiffTarget target = new() { Source = GitDiffSource.Commit, Commit = "abc", RepoPath = "B.cs", OriginalRepoPath = "A.cs", State = GitFileState.Renamed };

            string args = Args(target, DefaultOptions);

            StringAssert.StartsWith("diff-tree -p -M --root --diff-merges=first-parent", args);
            StringAssert.EndsWith("abc -- A.cs B.cs", args);
        }

        [Test]
        public void Test_BuildArguments_MergeBaseRange_UsesThreeDots()
        {
            GitDiffTarget target = new() { Source = GitDiffSource.MergeBaseRange, From = "HEAD", To = "@{u}", RepoPath = "A.cs" };

            StringAssert.Contains("HEAD...@{u} -- A.cs", Args(target, DefaultOptions));
        }

        [Test]
        public void Test_BuildArguments_Range_PassesBothCommits()
        {
            GitDiffTarget target = GitDiffTarget.ForRange("aaa", "bbb", new GitFileChange { Status = 'M', RepoPath = "A.cs" });

            StringAssert.EndsWith(" aaa bbb -- A.cs", Args(target, DefaultOptions));
        }

        [TestCase(DiffWhitespace.IgnoreChange, "-b")]
        [TestCase(DiffWhitespace.IgnoreAll, "-w")]
        public void Test_BuildArguments_Whitespace_AddsFlag(DiffWhitespace whitespace, string flag)
        {
            GitDiffTarget target = new() { Source = GitDiffSource.WorkingTree, RepoPath = "A.cs" };

            CollectionAssert.Contains(GitDiff.BuildArguments(target, new GitDiffOptions(DiffAlgorithm.Myers, whitespace, 3)), flag);
        }

        [Test]
        public void Test_BuildArguments_Options_AlgorithmAndContext()
        {
            GitDiffTarget target = new() { Source = GitDiffSource.WorkingTree, RepoPath = "A.cs" };

            var args = GitDiff.BuildArguments(target, new GitDiffOptions(DiffAlgorithm.Patience, DiffWhitespace.Show, 10));

            CollectionAssert.Contains(args, "--diff-algorithm=patience");
            CollectionAssert.Contains(args, "-U10");
        }

        [Test]
        public void Test_Parse_NumbersLinesFromHunkHeader()
        {
            const string output = "diff --git a/A.cs b/A.cs\nindex 1..2 100644\n--- a/A.cs\n+++ b/A.cs\n@@ -10,3 +10,3 @@ class A\n same\n-old\n+new\n tail\n";

            GitDiffDocument document = GitDiff.Parse(output);

            Assert.AreEqual(1, document.HunkCount);
            Assert.AreEqual(1, document.Added);
            Assert.AreEqual(1, document.Removed);
            GitDiffLine[] lines = document.Lines.ToArray();
            Assert.AreEqual(GitDiffLineKind.Hunk, lines[0].Kind);
            Assert.AreEqual((10, 10), (lines[1].OldNumber, lines[1].NewNumber));
            Assert.AreEqual((11, 0), (lines[2].OldNumber, lines[2].NewNumber));
            Assert.AreEqual((0, 11), (lines[3].OldNumber, lines[3].NewNumber));
            Assert.AreEqual((12, 12), (lines[4].OldNumber, lines[4].NewNumber));
            Assert.AreEqual("new", lines[3].Text);
        }

        [Test]
        public void Test_Parse_BinaryFile_IsBinaryWithoutLines()
        {
            GitDiffDocument document = GitDiff.Parse("diff --git a/a.png b/a.png\nindex 1..2 100644\nBinary files a/a.png and b/a.png differ\n");

            Assert.IsTrue(document.IsBinary);
            Assert.AreEqual(0, document.Lines.Count);
        }

        [Test]
        public void Test_Parse_NoNewlineMarker_IsKeptAsMetaLine()
        {
            GitDiffDocument document = GitDiff.Parse("@@ -1 +1 @@\n-a\n\\ No newline at end of file\n+a\n");

            Assert.AreEqual(GitDiffLineKind.NoNewline, document.Lines[2].Kind);
            Assert.AreEqual(1, document.Lines[3].NewNumber);
        }

        [Test]
        public void Test_Parse_EmptyOutput_IsEmpty()
        {
            Assert.IsTrue(GitDiff.Parse(string.Empty).IsEmpty);
        }

        [TestCase("@@ -12,5 +13,7 @@ section", 12, 13)]
        [TestCase("@@ -1 +1 @@", 1, 1)]
        [TestCase("@@ -0,0 +1,3 @@", 0, 1)]
        public void Test_TryParseHunkHeader_ReadsStarts(string header, int oldStart, int newStart)
        {
            Assert.IsTrue(GitDiff.TryParseHunkHeader(header, out int parsedOld, out int parsedNew));
            Assert.AreEqual((oldStart, newStart), (parsedOld, parsedNew));
        }

        [Test]
        public void Test_MarkPair_SmallEdit_HighlightsOnlyChangedPart()
        {
            GitDiffLine removed = new() { Kind = GitDiffLineKind.Removed, Text = "int count = 1;" };
            GitDiffLine added = new() { Kind = GitDiffLineKind.Added, Text = "int count = 10;" };

            GitDiff.MarkPair(removed, added);

            // "int count = 1" 이 공통 앞, ";" 이 공통 뒤 → 지운 쪽은 빈 구간, 더한 쪽은 "0".
            Assert.AreEqual((13, 0), (removed.ChangeStart, removed.ChangeLength));
            Assert.AreEqual((13, 1), (added.ChangeStart, added.ChangeLength));
        }

        [Test]
        public void Test_MarkPair_RewrittenLine_IsNotHighlighted()
        {
            GitDiffLine removed = new() { Kind = GitDiffLineKind.Removed, Text = "/// <summary>Long description here.</summary>" };
            GitDiffLine added = new() { Kind = GitDiffLineKind.Added, Text = "/// <summary>" };

            GitDiff.MarkPair(removed, added);

            Assert.AreEqual(0, removed.ChangeLength);
            Assert.AreEqual(0, added.ChangeLength);
        }

        [Test]
        public void Test_ToSideBySide_PairsRemovedWithAddedAndPadsRest()
        {
            GitDiffDocument document = GitDiff.Parse("@@ -1,3 +1,2 @@\n same\n-a\n-b\n+c\n");

            var rows = GitDiff.ToSideBySide(document.Lines);

            // 머리 줄, 문맥, (a↔c), (b↔빈칸)
            Assert.AreEqual(4, rows.Count);
            Assert.AreEqual("a", rows[2].Left.Text);
            Assert.AreEqual("c", rows[2].Right.Text);
            Assert.AreEqual("b", rows[3].Left.Text);
            Assert.IsNull(rows[3].Right);
        }
    }
}
