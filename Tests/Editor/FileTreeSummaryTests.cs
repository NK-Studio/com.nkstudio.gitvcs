using System.Collections.Generic;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    public class FileTreeSummaryTests
    {
        private static FileListItem File(string path, GitFileState state)
        {
            return new FileListItem { RepoPath = path, State = state };
        }

        [Test]
        public void Test_StateSummary_OrdersModifiedAddedDeletedAndSkipsZero()
        {
            List<(GitFileState, int)> summary = FileTreeBuilder.StateSummary(new[]
            {
                File("a/Deleted.cs", GitFileState.Deleted),
                File("a/Added.cs", GitFileState.Added),
                File("a/Edited.cs", GitFileState.Modified),
                File("a/Edited2.cs", GitFileState.Modified),
            });

            CollectionAssert.AreEqual(new[]
            {
                (GitFileState.Modified, 2),
                (GitFileState.Added, 1),
                (GitFileState.Deleted, 1),
            }, summary);
        }

        [Test]
        public void Test_StateSummary_UntrackedCountsAsAdded()
        {
            List<(GitFileState, int)> summary = FileTreeBuilder.StateSummary(new[]
            {
                File("a/New.cs", GitFileState.Untracked),
                File("a/Staged.cs", GitFileState.Added),
            });

            CollectionAssert.AreEqual(new[] { (GitFileState.Added, 2) }, summary);
        }

        [Test]
        public void Test_Build_KeepsEachFolderLevelOnItsOwnRow()
        {
            List<FileTreeNode> roots = FileTreeBuilder.Build(new[]
            {
                File("StatusTest/Scripts/Modify.txt", GitFileState.Modified),
            });

            Assert.AreEqual("StatusTest", roots[0].Name);
            Assert.AreEqual("StatusTest", roots[0].Path);
            Assert.AreEqual(1, roots[0].Children.Count);
            Assert.AreEqual("Scripts", roots[0].Children[0].Name);
            Assert.AreEqual("StatusTest/Scripts", roots[0].Children[0].Path);
            Assert.AreEqual("Modify.txt", roots[0].Children[0].Children[0].Name);
        }

        [Test]
        public void Test_StateSummary_FolderCountsNestedFiles()
        {
            List<FileTreeNode> roots = FileTreeBuilder.Build(new[]
            {
                File("Assets/Plugins/Blur/Scripts/Editor/A.cs", GitFileState.Modified),
                File("Assets/Plugins/Blur/Scripts/B.cs", GitFileState.Added),
                File("Assets/Plugins/Blur/Scripts/C.cs", GitFileState.Deleted),
            });

            CollectionAssert.AreEqual(new[]
            {
                (GitFileState.Modified, 1),
                (GitFileState.Added, 1),
                (GitFileState.Deleted, 1),
            }, FileTreeBuilder.StateSummary(roots[0].Files()));
        }
    }
}
