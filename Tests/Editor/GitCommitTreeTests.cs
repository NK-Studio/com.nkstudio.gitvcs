using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// Fork 형태 브랜치 탐색기 아래 칸이 읽는 git 출력(<see cref="GitCommitTree"/>)과 파일 트리(<see cref="GitPathTree"/>).
    /// </summary>
    public sealed class GitCommitTreeTests
    {
        private const string F = "\x1f";

        [Test]
        public void Test_ParseCommitter()
        {
            GitCommitPerson person = GitCommitTree.ParseCommitter("GitHub" + F + "noreply@github.com" + F + "1760402740\n");

            Assert.AreEqual("GitHub", person.Name);
            Assert.AreEqual("noreply@github.com", person.Email);
            Assert.AreEqual(System.DateTimeOffset.FromUnixTimeSeconds(1760402740).LocalDateTime, person.Date);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("GitHub\u001fnoreply@github.com\u001fnot-a-number")]
        public void Test_ParseCommitter_Malformed_ReturnsNull(string output)
        {
            Assert.IsNull(GitCommitTree.ParseCommitter(output));
        }

        [Test]
        public void Test_ParseTreePaths_SplitsNulAndSkipsEmpty()
        {
            CollectionAssert.AreEqual(new[] { "README.md", "Assets/a b.txt", "Assets/Scenes/Main.unity" },
                GitCommitTree.ParseTreePaths("README.md\0Assets/a b.txt\0Assets/Scenes/Main.unity\0"));
            CollectionAssert.IsEmpty(GitCommitTree.ParseTreePaths(string.Empty));
        }

        [Test]
        public void Test_ToContent_DetectsBinaryAndTruncates()
        {
            Assert.IsTrue(GitCommitTree.ToContent("PNG\0\0data").IsBinary);

            GitFileContent text = GitCommitTree.ToContent("hello\nworld\n");
            Assert.IsFalse(text.IsBinary);
            Assert.IsFalse(text.IsTruncated);
            Assert.AreEqual("hello\nworld\n", text.Text);

            GitFileContent large = GitCommitTree.ToContent(new string('a', GitCommitTree.MaxPreviewChars + 10));
            Assert.IsTrue(large.IsTruncated);
            Assert.AreEqual(GitCommitTree.MaxPreviewChars, large.Text.Length);
        }

        [TestCase("a\nb\n", new[] { "a", "b" })]
        [TestCase("a\r\nb", new[] { "a", "b" })]
        [TestCase("a\n\nb\n", new[] { "a", "", "b" })]
        [TestCase("", new string[0])]
        public void Test_SplitLines(string text, string[] expected)
        {
            CollectionAssert.AreEqual(expected, GitCommitTree.SplitLines(text));
        }

        [Test]
        public void Test_PathTree_FoldersFirstThenFilesByName()
        {
            List<GitPathNode> roots = GitPathTree.Build(new[] { "README.md", "apps/prism/main.rs", ".gitignore", "apps/Cargo.toml", ".cargo/config.toml", "AGENTS.md" });

            CollectionAssert.AreEqual(new[] { ".cargo", "apps", ".gitignore", "AGENTS.md", "README.md" }, roots.Select(n => n.Name).ToArray());
            GitPathNode apps = roots[1];
            Assert.IsTrue(apps.IsFolder);
            Assert.AreEqual("apps", apps.Path);
            CollectionAssert.AreEqual(new[] { "prism", "Cargo.toml" }, apps.Children.Select(n => n.Name).ToArray());
            Assert.AreEqual("apps/prism/main.rs", apps.Children[0].Children.Single().Path);
            Assert.IsFalse(apps.Children[0].Children.Single().IsFolder);
        }
    }
}
