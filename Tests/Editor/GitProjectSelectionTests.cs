using System.IO;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// Project 창 우클릭 Git 메뉴가 고른 에셋·폴더를 어떤 변경 항목으로 보는지(<see cref="GitProjectSelection"/>).
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed class GitProjectSelectionTests
    {
        private static GitStatusEntry Entry(string repoPath, GitFileState state = GitFileState.Modified)
        {
            return new GitStatusEntry { RepoPath = repoPath, State = state };
        }

        private static readonly GitStatusEntry[] Entries =
        {
            Entry("Assets/A.png"),
            Entry("Assets/A.png.meta"),
            Entry("Assets/AB.png"),
            Entry("Assets/Art.meta", GitFileState.Untracked),
            Entry("Assets/Art/B.psd", GitFileState.Untracked),
            Entry("Assets/Art/B.psd.meta", GitFileState.Untracked),
            Entry("Assets/Artwork/C.png"),
            Entry("ProjectSettings/TagManager.asset"),
        };

        private static string[] Paths(params string[] picked)
        {
            return GitProjectSelection.EntriesUnder(picked, Entries).Select(e => e.RepoPath).ToArray();
        }

        [Test]
        public void Test_File_IncludesItsMeta_NotSimilarNames()
        {
            CollectionAssert.AreEquivalent(new[] { "Assets/A.png", "Assets/A.png.meta" }, Paths("Assets/A.png"));
        }

        [Test]
        public void Test_Folder_IncludesEverythingUnderAndItsMeta_NotSiblingWithSamePrefix()
        {
            CollectionAssert.AreEquivalent(new[] { "Assets/Art.meta", "Assets/Art/B.psd", "Assets/Art/B.psd.meta" }, Paths("Assets/Art"));
        }

        [Test]
        public void Test_FileInsideUntrackedFolderLine_IsCovered()
        {
            // status 가 새 폴더를 "Assets/New/" 한 줄로 줄 때 그 안의 파일을 고른 경우.
            GitStatusEntry folder = Entry("Assets/New/", GitFileState.Untracked);
            CollectionAssert.AreEqual(new[] { folder }, GitProjectSelection.EntriesUnder(new[] { "Assets/New/D.png" }, new[] { folder }));
        }

        [Test]
        public void Test_UnchangedSelection_IsEmpty()
        {
            Assert.IsEmpty(Paths("Assets/Scenes"));
        }

        [Test]
        public void Test_RepoPathOf_ProjectInsideRepo()
        {
            string repo = Path.Combine(Path.GetTempPath(), "GitVcsSelection");
            string project = Path.Combine(repo, "Game");

            Assert.AreEqual("Game/Assets/A.png", GitProjectSelection.RepoPathOf("Assets/A.png", project, repo));
            Assert.AreEqual("Game/Packages/com.x/B.cs", GitProjectSelection.RepoPathOf("Packages/com.x/B.cs", project, repo));
        }

        [Test]
        public void Test_RepoPathOf_OutsideRepo_IsNull()
        {
            string repo = Path.Combine(Path.GetTempPath(), "GitVcsSelection", "Game");

            Assert.IsNull(GitProjectSelection.RepoPathOf("../Other/A.png", repo, repo));
            Assert.IsNull(GitProjectSelection.RepoPathOf(string.Empty, repo, repo));
        }

        [Test]
        public void Test_IgnorePatterns_IncludeMeta()
        {
            CollectionAssert.AreEqual(new[] { "/Assets/A.psd", "/Assets/A.psd.meta" }, GitProjectSelection.IgnorePatternsOf("Assets/A.psd", isFolder: false));
            CollectionAssert.AreEqual(new[] { "/Assets/Art/", "/Assets/Art.meta" }, GitProjectSelection.IgnorePatternsOf("Assets/Art/", isFolder: true));
        }
    }
}
