using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 충돌 중 Git 창을 잃을 수 있는 상황 알아보기(경고 줄·Unity 종료 확인).
    /// </summary>
    public sealed class GitConflictSafetyTests
    {
        private static GitStatusEntry Entry(string path, GitFileState state)
        {
            return new GitStatusEntry { RepoPath = path, State = state };
        }

        [TestCase("Packages/manifest.json", true)]
        [TestCase("Packages/packages-lock.json", true)]
        [TestCase("MyGame/Packages/manifest.json", true)]
        [TestCase("Assets/Packages/manifest.json.txt", false)]
        [TestCase("Assets/manifest.json", false)]
        [TestCase("", false)]
        public void Test_IsPackageManifest(string path, bool expected)
        {
            Assert.AreEqual(expected, GitConflictSafety.IsPackageManifest(path));
        }

        [Test]
        public void Test_Check_ManifestConflict_WinsOverCompileErrors()
        {
            List<GitStatusEntry> entries = new()
            {
                Entry("Assets/A.cs", GitFileState.Conflicted),
                Entry("Packages/manifest.json", GitFileState.Conflicted),
            };

            Assert.AreEqual(GitConflictRisk.PackageManifest, GitConflictSafety.Check(entries, true, true));
        }

        [Test]
        public void Test_Check_CompileErrorsDuringConflicts()
        {
            List<GitStatusEntry> entries = new() { Entry("Assets/A.cs", GitFileState.Conflicted) };

            Assert.AreEqual(GitConflictRisk.CompileErrors, GitConflictSafety.Check(entries, false, true));
        }

        [Test]
        public void Test_Check_CompileErrorsWhileOperationStopped()
        {
            Assert.AreEqual(GitConflictRisk.CompileErrors, GitConflictSafety.Check(new List<GitStatusEntry>(), true, true));
        }

        [Test]
        public void Test_Check_CompileErrorsWithoutConflicts_IsNotOurRisk()
        {
            List<GitStatusEntry> entries = new() { Entry("Assets/A.cs", GitFileState.Modified) };

            Assert.AreEqual(GitConflictRisk.None, GitConflictSafety.Check(entries, false, true));
        }

        [Test]
        public void Test_Check_ConflictsWithoutCompileErrors_IsSafe()
        {
            List<GitStatusEntry> entries = new() { Entry("Assets/A.cs", GitFileState.Conflicted) };

            Assert.AreEqual(GitConflictRisk.None, GitConflictSafety.Check(entries, true, false));
        }

        [Test]
        public void Test_ManifestFirst_MovesPackageListToTop()
        {
            List<GitStatusEntry> conflicts = new()
            {
                Entry("Assets/A.cs", GitFileState.Conflicted),
                Entry("Packages/manifest.json", GitFileState.Conflicted),
                Entry("Assets/B.cs", GitFileState.Conflicted),
            };

            CollectionAssert.AreEqual(new[] { "Packages/manifest.json", "Assets/A.cs", "Assets/B.cs" },
                GitConflictSafety.ManifestFirst(conflicts).Select(e => e.RepoPath).ToList());
        }
    }
}
