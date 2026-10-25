using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 고른 줄·hunk 만 든 부분 패치 만들기: 줄 고르기 규칙(Stage·Unstage), 머리 줄 줄 수 다시 세기, CRLF, 파일 끝 개행 없음, 한글 경로, 이름 바뀐 파일.
    /// </summary>
    public sealed class GitPartialPatchTests
    {
        private const string Header = "diff --git a/A.cs b/A.cs\n--- a/A.cs\n+++ b/A.cs\n";

        private static List<GitDiffLine> Lines(string diff)
        {
            return GitDiff.Parse(diff).Lines;
        }

        /// <summary>
        /// 줄 목록에서 바뀐 줄 중 내용이 <paramref name="texts"/> 인 것의 인덱스.
        /// </summary>
        private static int[] Pick(IReadOnlyList<GitDiffLine> lines, params string[] texts)
        {
            return lines.Select((line, index) => (line, index))
                .Where(pair => GitPartialPatch.IsChange(pair.line) && texts.Contains(pair.line.Text))
                .Select(pair => pair.index)
                .ToArray();
        }

        [Test]
        public void Test_Build_StageOneAddedLine_DropsOtherAddedLines()
        {
            List<GitDiffLine> lines = Lines("@@ -1,2 +1,4 @@\n a\n+x\n+y\n b\n");

            string patch = GitPartialPatch.Build(lines, Pick(lines, "x"), "A.cs", reverse: false);

            Assert.AreEqual(Header + "@@ -1,2 +1,3 @@\n a\n+x\n b\n", patch);
        }

        [Test]
        public void Test_Build_StageOneRemovedLine_KeepsOtherRemovedAsContext()
        {
            List<GitDiffLine> lines = Lines("@@ -1,3 +1,1 @@\n a\n-x\n-y\n");

            string patch = GitPartialPatch.Build(lines, Pick(lines, "x"), "A.cs", reverse: false);

            // y 는 인덱스(옛 판)에 남으므로 문맥 줄이 된다.
            Assert.AreEqual(Header + "@@ -1,3 +1,2 @@\n a\n-x\n y\n", patch);
        }

        [Test]
        public void Test_Build_UnstageOneAddedLine_KeepsOtherAddedAsContext()
        {
            List<GitDiffLine> lines = Lines("@@ -1,2 +1,4 @@\n a\n+x\n+y\n b\n");

            string patch = GitPartialPatch.Build(lines, Pick(lines, "y"), "A.cs", reverse: true);

            // x 는 인덱스(새 판)에 남으므로 문맥 줄, 옛 판 줄 수는 그만큼 늘어난다.
            Assert.AreEqual(Header + "@@ -1,3 +1,4 @@\n a\n x\n+y\n b\n", patch);
        }

        [Test]
        public void Test_Build_UnstageOneRemovedLine_DropsOtherRemovedLines()
        {
            List<GitDiffLine> lines = Lines("@@ -1,3 +1,1 @@\n a\n-x\n-y\n");

            string patch = GitPartialPatch.Build(lines, Pick(lines, "y"), "A.cs", reverse: true);

            // x 는 인덱스(새 판)에 없으니 뺀다. 거꾸로 적용되므로 새 판 시작 줄(1)이 기준이다.
            Assert.AreEqual(Header + "@@ -1,2 +1,1 @@\n a\n-y\n", patch);
        }

        [Test]
        public void Test_Build_SelectionOutsideChanges_IsNull()
        {
            List<GitDiffLine> lines = Lines("@@ -1,2 +1,3 @@\n a\n+x\n b\n");

            // 0 = 머리 줄, 1 = 문맥 줄.
            Assert.IsNull(GitPartialPatch.Build(lines, new[] { 0, 1 }, "A.cs", reverse: false));
            Assert.IsNull(GitPartialPatch.Build(lines, new int[0], "A.cs", reverse: false));
        }

        [Test]
        public void Test_Build_SkipsHunksWithoutSelection_AndShiftsLaterStarts()
        {
            const string diff = "@@ -1,2 +1,4 @@\n a\n+x\n+y\n b\n@@ -10,2 +12,3 @@\n c\n+z\n d\n@@ -20,2 +23,1 @@\n e\n-f\n";
            List<GitDiffLine> lines = Lines(diff);

            string patch = GitPartialPatch.Build(lines, Pick(lines, "x", "f"), "A.cs", reverse: false);

            // 가운데 묶음은 빠지고, 첫 묶음이 한 줄 늘렸으니 셋째 묶음의 새 시작 줄은 20+1 = 21.
            Assert.AreEqual(Header + "@@ -1,2 +1,3 @@\n a\n+x\n b\n@@ -20,2 +21,1 @@\n e\n-f\n", patch);
        }

        [Test]
        public void Test_Build_Unstage_ShiftsLaterOldStarts()
        {
            const string diff = "@@ -1,2 +1,4 @@\n a\n+x\n+y\n b\n@@ -20,2 +22,1 @@\n e\n-f\n";
            List<GitDiffLine> lines = Lines(diff);

            string patch = GitPartialPatch.Build(lines, Pick(lines, "x", "f"), "A.cs", reverse: true);

            // 첫 묶음: y 가 문맥이 되어 옛 판 3줄 → 새 판 4줄(+1). 둘째 묶음의 옛 시작 줄은 22−1 = 21.
            Assert.AreEqual(Header + "@@ -1,3 +1,4 @@\n a\n+x\n y\n b\n@@ -21,2 +22,1 @@\n e\n-f\n", patch);
        }

        [Test]
        public void Test_Build_ZeroCountSide_UsesLineBeforeConvention()
        {
            // 문맥 없이(-U0) 줄만 지운 묶음: 새 판 줄 수가 0 이면 시작 줄은 그 앞 줄(git 과 같다).
            List<GitDiffLine> lines = Lines("@@ -5,2 +4,0 @@\n-x\n-y\n");

            Assert.AreEqual(Header + "@@ -5,2 +4,0 @@\n-x\n-y\n", GitPartialPatch.Build(lines, Pick(lines, "x", "y"), "A.cs", reverse: false));
            // 한 줄만 고르면 다른 줄은 문맥이 되어 새 판에 1줄 남는다.
            Assert.AreEqual(Header + "@@ -5,2 +5,1 @@\n-x\n y\n", GitPartialPatch.Build(lines, Pick(lines, "x"), "A.cs", reverse: false));
        }

        [Test]
        public void Test_Build_NewFile_StagesChosenLinesFromEmptyOldSide()
        {
            List<GitDiffLine> lines = Lines("@@ -0,0 +1,3 @@\n+a\n+b\n+c\n");

            string patch = GitPartialPatch.Build(lines, Pick(lines, "b"), "A.cs", reverse: false);

            Assert.AreEqual(Header + "@@ -0,0 +1,1 @@\n+b\n", patch);
        }

        [Test]
        public void Test_Build_Crlf_KeepsCarriageReturnOnEveryLine()
        {
            List<GitDiffLine> lines = Lines("@@ -1,2 +1,3 @@\r\n a\r\n+x\r\n b\r\n");

            string patch = GitPartialPatch.Build(lines, Pick(lines, "x"), "A.cs", reverse: false);

            // 머리 줄은 git 의 줄이라 '\r' 이 없고, 내용 줄은 파일의 '\r' 을 그대로 돌려준다.
            Assert.AreEqual(Header + "@@ -1,2 +1,3 @@\n a\r\n+x\r\n b\r\n", patch);
            Assert.IsTrue(lines[2].HasCarriageReturn);
            Assert.AreEqual("x", lines[2].Text);
        }

        [Test]
        public void Test_Build_NoNewlineAtEnd_MarkerFollowsItsLine()
        {
            // 옛 판은 끝에 개행이 없고 새 판에는 있다: "-x\ No newline\n+x".
            List<GitDiffLine> lines = Lines("@@ -1,2 +1,2 @@\n a\n-x\n\\ No newline at end of file\n+x\n");
            int[] both = Pick(lines, "x");

            // 둘 다 고르면 그대로.
            Assert.AreEqual(Header + "@@ -1,2 +1,2 @@\n a\n-x\n\\ No newline at end of file\n+x\n", GitPartialPatch.Build(lines, both, "A.cs", reverse: false));
            // Stage 에서 아무것도 안 고르면 null. Unstage 에서 '-' 줄은 빠지고 표시도 같이 빠지며 '+' 줄은 문맥이 된다 → 바뀐 줄이 없어 null.
            Assert.IsNull(GitPartialPatch.Build(lines, new int[0], "A.cs", reverse: true));
        }

        [Test]
        public void Test_Build_NoNewlineAtEnd_DroppedLineDropsMarker()
        {
            // 새 판 마지막 줄에 개행이 없다: "+y\ No newline". y 를 안 고르면(Stage) 표시도 같이 빠진다.
            List<GitDiffLine> lines = Lines("@@ -1,1 +1,3 @@\n a\n+x\n+y\n\\ No newline at end of file\n");

            string patch = GitPartialPatch.Build(lines, Pick(lines, "x"), "A.cs", reverse: false);

            Assert.AreEqual(Header + "@@ -1,1 +1,2 @@\n a\n+x\n", patch);
        }

        [Test]
        public void Test_Build_NoNewlineAtEnd_ContextLineKeepsMarker()
        {
            // Unstage 에서 안 고른 '+y' 가 문맥이 되면 그 뒤 표시는 남는다(인덱스의 마지막 줄에 개행이 없다는 사실은 그대로).
            List<GitDiffLine> lines = Lines("@@ -1,1 +1,3 @@\n a\n+x\n+y\n\\ No newline at end of file\n");

            string patch = GitPartialPatch.Build(lines, Pick(lines, "x"), "A.cs", reverse: true);

            Assert.AreEqual(Header + "@@ -1,2 +1,3 @@\n a\n+x\n y\n\\ No newline at end of file\n", patch);
        }

        [Test]
        public void Test_Build_KoreanPath_IsQuotedLikeGit()
        {
            List<GitDiffLine> lines = Lines("@@ -1,1 +1,2 @@\n a\n+x\n");

            string patch = GitPartialPatch.Build(lines, Pick(lines, "x"), "Assets/한글.cs", reverse: false);

            StringAssert.StartsWith("diff --git \"a/Assets/\\355\\225\\234\\352\\270\\200.cs\" \"b/Assets/\\355\\225\\234\\352\\270\\200.cs\"\n--- \"a/Assets/\\355\\225\\234\\352\\270\\200.cs\"\n+++ \"b/Assets/\\355\\225\\234\\352\\270\\200.cs\"\n@@ -1,1 +1,2 @@\n", patch);
        }

        [Test]
        public void Test_Build_RenamedFile_UsesNewPathOnBothSides()
        {
            // Staged 목록의 이름 바뀐 파일: 인덱스에는 새 경로만 있으니 양쪽 다 새 경로다(원래 경로를 쓰면 git apply 가 못 찾는다).
            List<GitDiffLine> lines = Lines("diff --git a/Old.cs b/New.cs\nsimilarity index 80%\nrename from Old.cs\nrename to New.cs\n--- a/Old.cs\n+++ b/New.cs\n@@ -1,2 +1,2 @@\n a\n-b\n+c\n");

            string patch = GitPartialPatch.Build(lines, Pick(lines, "b", "c"), "New.cs", reverse: true);

            Assert.AreEqual("diff --git a/New.cs b/New.cs\n--- a/New.cs\n+++ b/New.cs\n@@ -1,2 +1,2 @@\n a\n-b\n+c\n", patch);
        }

        [Test]
        public void Test_Build_KeepsHunkTrailerText()
        {
            List<GitDiffLine> lines = Lines("@@ -3,2 +3,3 @@ void Method()\n a\n+x\n b\n");

            string patch = GitPartialPatch.Build(lines, Pick(lines, "x"), "A.cs", reverse: false);

            StringAssert.Contains("@@ -3,2 +3,3 @@ void Method()\n", patch);
        }

        [Test]
        public void Test_ChangedLinesOfHunk_ReturnsOnlyThatHunksChanges()
        {
            List<GitDiffLine> lines = Lines("@@ -1,2 +1,3 @@\n a\n+x\n b\n@@ -10,2 +11,2 @@\n c\n-d\n+e\n");

            List<int> second = GitPartialPatch.ChangedLinesOfHunk(lines, 1);

            CollectionAssert.AreEqual(new[] { 6, 7 }, second);
            Assert.AreEqual(1, lines[4].HunkIndex);
            Assert.AreEqual(0, lines[0].HunkIndex);
        }

        [TestCase("@@ -12,5 +13,7 @@ section", 12, 5, 13, 7, " section")]
        [TestCase("@@ -1 +1 @@", 1, 1, 1, 1, "")]
        [TestCase("@@ -0,0 +1,3 @@", 0, 0, 1, 3, "")]
        public void Test_TryParseHunkHeader_ReadsCountsAndTrailer(string header, int oldStart, int oldCount, int newStart, int newCount, string trailer)
        {
            Assert.IsTrue(GitPartialPatch.TryParseHunkHeader(header, out GitPartialPatch.HunkHeader parsed));
            Assert.AreEqual((oldStart, oldCount, newStart, newCount, trailer), (parsed.OldStart, parsed.OldCount, parsed.NewStart, parsed.NewCount, parsed.Trailer));
        }

        [TestCase("Assets/A.cs", "Assets/A.cs")]
        [TestCase("Assets/My File.cs", "\"Assets/My File.cs\"")]
        [TestCase("a\"b", "\"a\\\"b\"")]
        [TestCase("a\\b", "\"a\\\\b\"")]
        [TestCase("한.txt", "\"\\355\\225\\234.txt\"")]
        public void Test_QuotePath_MatchesGitCStyleQuoting(string path, string expected)
        {
            Assert.AreEqual(expected, GitPartialPatch.QuotePath(path));
        }
    }
}
