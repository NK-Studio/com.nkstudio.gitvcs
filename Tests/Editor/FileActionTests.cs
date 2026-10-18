using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 파일·폴더 우클릭 메뉴의 순수 규칙: .gitignore 패턴, blame 해석, 이름 변경 따라가기, 폴더 경로 맞추기, 일부 stash 다시 하기.
    /// </summary>
    public sealed class FileActionTests
    {
        [Test]
        public void Test_GitIgnorePatterns_FileExtensionFolder()
        {
            Assert.AreEqual("/Assets/Art/a.psd", GitIgnorePatterns.ForFile("Assets/Art/a.psd"));
            Assert.AreEqual("*.psd", GitIgnorePatterns.ForExtension("Assets/Art/a.psd"));
            Assert.IsNull(GitIgnorePatterns.ForExtension("Makefile"));
            Assert.IsNull(GitIgnorePatterns.ForExtension(".idea/.gitignore"));
            Assert.AreEqual("*.meta", GitIgnorePatterns.ForExtension("Assets/a.cs.meta"));
            Assert.AreEqual("/Assets/Art/", GitIgnorePatterns.ForFolder("Assets/Art"));
        }

        [Test]
        public void Test_GitIgnorePatterns_Append_AddsNewlineAndSkipsDuplicate()
        {
            string once = GitIgnorePatterns.Append("Library/", "*.psd");

            Assert.AreEqual("Library/\n*.psd\n", once);
            Assert.AreEqual(once, GitIgnorePatterns.Append(once, "*.psd"));
        }

        [Test]
        public void Test_GitIgnorePatterns_Append_KeepsCrLf()
        {
            Assert.AreEqual("Library/\r\n*.psd\r\n", GitIgnorePatterns.Append("Library/\r\n", "*.psd"));
        }

        [Test]
        public void Test_BlameParser_ReadsHeaderAndContent()
        {
            string hash = new string('a', 40);
            string output =
                $"{hash} 1 1 2\nauthor Kim\nauthor-mail <k@x>\nauthor-time 1700000000\nauthor-tz +0900\nsummary Fix: a\nfilename A.cs\n\tline one\n" +
                $"{hash} 2 2\nauthor Kim\nauthor-time 1700000000\nsummary Fix: a\nfilename A.cs\n\tline two\n" +
                $"{new string('0', 40)} 3 3 1\nauthor Not Committed Yet\nauthor-time 1700000100\nsummary Version of A.cs from A.cs\nfilename A.cs\n\t\tindented\n";

            List<GitBlameLine> lines = GitBlameParser.Parse(output);

            Assert.AreEqual(3, lines.Count);
            Assert.AreEqual("Kim", lines[0].AuthorName);
            Assert.AreEqual("Fix: a", lines[0].Summary);
            Assert.AreEqual(2, lines[1].LineNumber);
            Assert.AreEqual("line two", lines[1].Text);
            Assert.IsTrue(lines[2].IsUncommitted);
            Assert.AreEqual("\tindented", lines[2].Text);
        }

        [Test]
        public void Test_ParseFollowPaths_MapsEachCommitToItsPath()
        {
            string output = "\x1e" + "bbb\n\nAssets/New.cs\n" + "\x1e" + "aaa\n\nAssets/Old.cs\n";

            Dictionary<string, string> paths = GitFileOperations.ParseFollowPaths(output);

            Assert.AreEqual("Assets/New.cs", paths["bbb"]);
            Assert.AreEqual("Assets/Old.cs", paths["aaa"]);
        }

        [Test]
        public void Test_RepoFolderOf_ProjectInsideRepoSubfolder()
        {
            FileListItem file = new() { RepoPath = "Game/Assets/Art/a.png", ProjectPath = "Assets/Art/a.png" };

            Assert.AreEqual("Game/Assets/Art", FileActionMenu.RepoFolderOf("Assets/Art", file));
        }

        [Test]
        public void Test_RepoFolderOf_ProjectAtRepoRoot()
        {
            FileListItem file = new() { RepoPath = "Assets/Art/a.png", ProjectPath = "Assets/Art/a.png" };

            Assert.AreEqual("Assets/Art", FileActionMenu.RepoFolderOf("Assets/Art", file));
        }

        [Test]
        public void Test_UndoPlanner_RedoPartialStash_StashesOnlyThosePaths()
        {
            GitUndoEntry entry = new() { Kind = GitUndoKind.Stash, Hash = "s", Paths = new List<string> { "Assets/A.cs" } };

            string[] command = GitUndoPlanner.Redo(entry).Commands.Single();

            CollectionAssert.AreEqual(new[] { "stash", "push", "--include-untracked", "--", "Assets/A.cs" }, command);
        }
    }
}
