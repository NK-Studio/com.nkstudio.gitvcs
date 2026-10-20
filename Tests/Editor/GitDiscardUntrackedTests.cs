using System;
using System.IO;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 새 폴더의 .meta 를 버리면 폴더도 같이 지우는지 확인한다 — 폴더가 남으면 Unity 가 .meta 를 곧바로 다시 만든다.
    /// 운영체제 파일(.DS_Store 등)만 있는 폴더는 빈 것으로 치고, 다른 파일이 있으면 남긴다.
    /// </summary>
    public sealed class GitDiscardUntrackedTests
    {
        private string _root;
        private IDisposable _scope;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "GitVcsDiscardTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, ".git"));
            _scope = GitRepository.UseRepositoryForTests(_root, Path.Combine(_root, ".git"), new GitUndoJournal());
        }

        [TearDown]
        public void TearDown()
        {
            _scope?.Dispose();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        private string Make(string relativePath)
        {
            string path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "x");
            return path;
        }

        private static GitResult Discard(params string[] repoPaths)
        {
            GitStatusEntry[] entries = Array.ConvertAll(repoPaths, p => new GitStatusEntry { RepoPath = p, State = GitFileState.Untracked });
            // 새 파일만이면 git 을 부르지 않고 바로 끝난다.
            return GitOperations.DiscardWorkTreeAsync(entries).GetAwaiter().GetResult();
        }

        [Test]
        public void Test_IsEffectivelyEmpty_OnlyOsJunk_True()
        {
            Make("A/.DS_Store");
            Make("A/B/Thumbs.db");

            Assert.IsTrue(GitOperations.IsEffectivelyEmpty(Path.Combine(_root, "A")));
        }

        [Test]
        public void Test_IsEffectivelyEmpty_RealFile_False()
        {
            Make("A/.DS_Store");
            Make("A/B/image.png");

            Assert.IsFalse(GitOperations.IsEffectivelyEmpty(Path.Combine(_root, "A")));
        }

        [Test]
        public void Test_DiscardFolderMeta_DeletesJunkOnlyFolder()
        {
            Make("Assets/Keep.txt");
            Make("Assets/New.meta");
            Make("Assets/New/.DS_Store");
            Make("Assets/New/Sub.meta");
            Make("Assets/New/Sub/.DS_Store");

            GitResult result = Discard("Assets/New/Sub.meta", "Assets/New.meta");

            Assert.IsTrue(result.Success, result.Message);
            Assert.IsFalse(Directory.Exists(Path.Combine(_root, "Assets/New")));
            Assert.IsFalse(File.Exists(Path.Combine(_root, "Assets/New.meta")));
            Assert.IsTrue(File.Exists(Path.Combine(_root, "Assets/Keep.txt")));
        }

        [Test]
        public void Test_DiscardFolderMeta_KeepsFolderWithFiles()
        {
            Make("Assets/New.meta");
            Make("Assets/New/ignored.bin");

            Discard("Assets/New.meta");

            Assert.IsTrue(File.Exists(Path.Combine(_root, "Assets/New/ignored.bin")));
        }
    }
}
