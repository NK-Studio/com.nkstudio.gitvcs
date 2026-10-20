using System.Collections.Generic;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 충돌 표시를 조각으로 나누고, 고른 쪽·차례·줄대로 출력 글을 만드는지 확인한다.
    /// </summary>
    public sealed class GitConflictFileTests
    {
        private const string Text = "top\n<<<<<<< HEAD\na1\na2\n=======\nb1\n>>>>>>> origin/develop\nbottom\n";

        [Test]
        public void Test_Parse_SplitsCommonAndConflict()
        {
            GitConflictFile file = GitConflictFile.Parse(Text);
            Assert.IsNotNull(file);
            Assert.AreEqual(3, file.Segments.Count);
            Assert.AreEqual(1, file.ConflictCount);
            Assert.AreEqual("HEAD", file.ALabel);
            Assert.AreEqual("origin/develop", file.BLabel);
            CollectionAssert.AreEqual(new[] { "a1", "a2" }, file.Segments[1].A);
            CollectionAssert.AreEqual(new[] { "b1" }, file.Segments[1].B);
            Assert.IsTrue(file.EndsWithNewLine);
        }

        [Test]
        public void Test_Parse_Diff3_SkipsBase()
        {
            GitConflictFile file = GitConflictFile.Parse("<<<<<<< ours\na\n||||||| base\no\n=======\nb\n>>>>>>> theirs");
            CollectionAssert.AreEqual(new[] { "a" }, file.Segments[0].A);
            CollectionAssert.AreEqual(new[] { "o" }, file.Segments[0].Base);
            CollectionAssert.AreEqual(new[] { "b" }, file.Segments[0].B);
            Assert.IsFalse(file.EndsWithNewLine);
        }

        [Test]
        public void Test_Parse_NoMarkers_Null()
        {
            Assert.IsNull(GitConflictFile.Parse("plain\n======= not a conflict\n"));
        }

        [Test]
        public void Test_Build_OrderFollowsChoice()
        {
            GitConflictFile file = GitConflictFile.Parse(Text);
            GitMergeChoice choice = new(file.Segments[1]);
            choice.SetSide(GitMergeSide.B, true);
            choice.SetSide(GitMergeSide.A, true);
            Assert.AreEqual("top\nb1\na1\na2\nbottom\n", file.Build(new List<GitMergeChoice> { choice }));
        }

        [Test]
        public void Test_Build_SingleLineToggle()
        {
            GitConflictFile file = GitConflictFile.Parse(Text);
            GitMergeChoice choice = new(file.Segments[1]);
            choice.ToggleLine(GitMergeSide.A, 1);
            Assert.AreEqual("top\na2\nbottom\n", file.Build(new List<GitMergeChoice> { choice }));
            choice.ToggleLine(GitMergeSide.A, 1);
            Assert.IsFalse(choice.IsDecided);
        }

        [Test]
        public void Test_Build_KeepsCrlf()
        {
            GitConflictFile file = GitConflictFile.Parse(Text.Replace("\n", "\r\n"));
            GitMergeChoice choice = new(file.Segments[1]);
            choice.SetSide(GitMergeSide.A, true);
            Assert.AreEqual("top\r\na1\r\na2\r\nbottom\r\n", file.Build(new List<GitMergeChoice> { choice }));
        }
    }
}
