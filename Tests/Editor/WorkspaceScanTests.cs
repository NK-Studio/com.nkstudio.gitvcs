using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 워크스페이스 탭(<see cref="GitWorkspaceIndex"/>)의 상태 분류: 추적·변경·비공개·무시, 폴더 묶음.
    /// </summary>
    public sealed class WorkspaceScanTests
    {
        private static GitWorkspaceIndex Sample()
        {
            const string tracked = "Assets/a.cs\0Assets/Art/b.png\0README.md\0";
            const string ignored = "Library/\0Temp/\0Assets/secret.txt\0";
            GitStatusEntry[] entries =
            {
                new() { RepoPath = "Assets/a.cs", State = GitFileState.Modified },
                new() { RepoPath = "Assets/New/c.cs", State = GitFileState.Untracked },
                new() { RepoPath = "Fresh/", State = GitFileState.Untracked },
            };
            return GitWorkspaceIndex.Build(tracked, ignored, entries);
        }

        [TestCase("README.md", GitWorkspaceState.Tracked)]
        [TestCase("Assets/Art/b.png", GitWorkspaceState.Tracked)]
        [TestCase("Assets/a.cs", GitWorkspaceState.Changed)]
        [TestCase("Assets/New/c.cs", GitWorkspaceState.Untracked)]
        [TestCase("Assets/secret.txt", GitWorkspaceState.Ignored)]
        [TestCase("Library/ArtifactDB", GitWorkspaceState.Ignored)]
        [TestCase("Library/Deep/x.bin", GitWorkspaceState.Ignored)]
        [TestCase("Somewhere/else.txt", GitWorkspaceState.Untracked)]
        public void Test_Classify_File(string path, GitWorkspaceState expected)
        {
            Assert.AreEqual(expected, Sample().Classify(path, isFolder: false, out _));
        }

        [TestCase("Library", GitWorkspaceState.Ignored)]
        [TestCase("Assets", GitWorkspaceState.Changed)]
        [TestCase("Assets/Art", GitWorkspaceState.Tracked)]
        [TestCase("Assets/New", GitWorkspaceState.Changed)]
        [TestCase("Fresh", GitWorkspaceState.Changed)]
        [TestCase("Empty", GitWorkspaceState.Untracked)]
        public void Test_Classify_Folder(string path, GitWorkspaceState expected)
        {
            Assert.AreEqual(expected, Sample().Classify(path, isFolder: true, out _));
        }

        [Test]
        public void Test_Classify_ReportsChangeKind()
        {
            Sample().Classify("Assets/a.cs", isFolder: false, out GitFileState? change);
            Assert.AreEqual(GitFileState.Modified, change);
        }

        [TestCase("Assets/a.cs", false, true)]
        [TestCase("Assets/Art/b.png", false, false)]
        [TestCase("Fresh/inside.txt", false, true)]
        [TestCase("Assets", true, true)]
        [TestCase("Assets/Art", true, false)]
        public void Test_HasPendingChange(string path, bool isFolder, bool expected)
        {
            Assert.AreEqual(expected, Sample().HasPendingChange(path, isFolder));
        }

        [Test]
        public void Test_Counts()
        {
            GitWorkspaceIndex index = Sample();
            Assert.AreEqual(3, index.TrackedCount);
            Assert.AreEqual(3, index.ChangedCount);
        }
    }
}
