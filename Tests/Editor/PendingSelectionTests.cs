using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    public sealed class PendingSelectionTests
    {
        [Test]
        public void Test_SetRows_NewEntriesAreCheckedByDefault()
        {
            PendingChangesState state = new();
            PendingSelection selection = new(state);

            selection.SetRows(new[] { Entry("Assets/a.cs"), Entry("Assets/b.cs") });

            Assert.IsTrue(selection.Rows.All(selection.IsChecked));
            Assert.IsTrue(selection.AllChecked);
            Assert.IsFalse(selection.NoneChecked);
            Assert.AreEqual(2, selection.CheckedCount);
            Assert.AreEqual(0, state.ExcludedPaths.Count);
        }

        [Test]
        public void Test_SetRows_SortsByProjectPathIgnoringCase()
        {
            PendingSelection selection = new(new PendingChangesState());

            selection.SetRows(new[] { Entry("Assets/c.cs"), Entry("Assets/B.cs"), Entry("Assets/a.cs") });

            CollectionAssert.AreEqual(new[] { "Assets/a.cs", "Assets/B.cs", "Assets/c.cs" }, selection.Rows.Select(r => r.RepoPath).ToArray());
        }

        [Test]
        public void Test_SetChecked_AssetAndMetaToggleTogether()
        {
            PendingChangesState state = new();
            PendingSelection selection = new(state);
            GitStatusEntry asset = Entry("Assets/Tex.png");
            GitStatusEntry meta = Entry("Assets/Tex.png.meta");
            GitStatusEntry other = Entry("Assets/Other.cs");
            selection.SetRows(new[] { asset, meta, other });

            selection.SetChecked(asset, false);

            Assert.IsFalse(selection.IsChecked(asset));
            Assert.IsFalse(selection.IsChecked(meta));
            Assert.IsTrue(selection.IsChecked(other));
            CollectionAssert.AreEquivalent(new[] { "Assets/Tex.png", "Assets/Tex.png.meta" }, state.ExcludedPaths);

            // .meta 쪽에서 다시 체크해도 에셋이 같이 돌아온다.
            selection.SetChecked(meta, true);

            Assert.IsTrue(selection.IsChecked(asset));
            Assert.IsTrue(selection.IsChecked(meta));
            Assert.AreEqual(0, state.ExcludedPaths.Count);
        }

        [Test]
        public void Test_SetChecked_FolderMetaPairsWithFolderEntry()
        {
            PendingSelection selection = new(new PendingChangesState());
            GitStatusEntry folder = Entry("Assets/Folder");
            GitStatusEntry meta = Entry("Assets/Folder.meta");
            selection.SetRows(new[] { folder, meta });

            selection.SetChecked(meta, false);

            Assert.IsFalse(selection.IsChecked(folder));
            Assert.AreSame(folder, selection.FindMetaPair(meta));
            Assert.AreSame(meta, selection.FindMetaPair(folder));
        }

        [Test]
        public void Test_SetChecked_WithoutPair_OnlyChangesThatEntry()
        {
            PendingChangesState state = new();
            PendingSelection selection = new(state);
            GitStatusEntry lone = Entry("Assets/Lone.cs");
            GitStatusEntry other = Entry("Assets/Other.cs");
            selection.SetRows(new[] { lone, other });

            selection.SetChecked(lone, false);

            Assert.IsNull(selection.FindMetaPair(lone));
            Assert.IsFalse(selection.IsChecked(lone));
            Assert.IsTrue(selection.IsChecked(other));
            CollectionAssert.AreEqual(new[] { "Assets/Lone.cs" }, state.ExcludedPaths);
            CollectionAssert.AreEqual(new[] { other }, selection.CheckedEntries());
        }

        [Test]
        public void Test_SetAll_ChecksAndUnchecksEveryRow()
        {
            PendingChangesState state = new();
            PendingSelection selection = new(state);
            selection.SetRows(new[] { Entry("Assets/a.cs"), Entry("Assets/a.cs.meta"), Entry("Assets/b.cs") });

            selection.SetAll(false);

            Assert.IsTrue(selection.NoneChecked);
            Assert.IsFalse(selection.AllChecked);
            Assert.AreEqual(3, state.ExcludedPaths.Count);
            Assert.AreEqual(0, selection.CheckedEntries().Count);

            selection.SetAll(true);

            Assert.IsTrue(selection.AllChecked);
            Assert.AreEqual(0, state.ExcludedPaths.Count);
        }

        [Test]
        public void Test_SetRows_DropsExclusionsForVanishedPaths()
        {
            PendingChangesState state = new() { ExcludedPaths = new List<string> { "Assets/gone.cs", "Assets/kept.cs" } };
            PendingSelection selection = new(state);
            GitStatusEntry kept = Entry("Assets/kept.cs");
            GitStatusEntry fresh = Entry("Assets/fresh.cs");

            selection.SetRows(new[] { kept, fresh });

            Assert.IsFalse(selection.IsChecked(kept));
            Assert.IsTrue(selection.IsChecked(fresh));
            CollectionAssert.AreEqual(new[] { "Assets/kept.cs" }, state.ExcludedPaths);
        }

        [Test]
        public void Test_Constructor_RestoresExclusionsFromState()
        {
            PendingChangesState state = new() { ExcludedPaths = new List<string> { "Assets/a.cs" } };
            PendingSelection selection = new(state);
            GitStatusEntry a = Entry("Assets/a.cs");
            GitStatusEntry b = Entry("Assets/b.cs");

            selection.SetRows(new[] { a, b });

            Assert.IsFalse(selection.IsChecked(a));
            Assert.IsTrue(selection.IsChecked(b));
            Assert.AreEqual(1, selection.CheckedCount);
            Assert.IsFalse(selection.AllChecked);
            Assert.IsFalse(selection.NoneChecked);
        }

        [Test]
        public void Test_EmptyRows_AreNeitherAllNorPartlyChecked()
        {
            PendingSelection selection = new(new PendingChangesState());

            selection.SetRows(new GitStatusEntry[0]);

            Assert.IsFalse(selection.AllChecked);
            Assert.IsTrue(selection.NoneChecked);
        }

        private static GitStatusEntry Entry(string path)
        {
            return new GitStatusEntry
            {
                RepoPath = path,
                ProjectPath = path,
                State = GitFileState.Modified,
                IndexCode = ' ',
                WorkTreeCode = 'M',
            };
        }
    }
}
