using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 남은 잠금 파일을 git 오류 글에서 찾는지, 저장소 git 폴더 밖 경로는 거르는지 확인한다.
    /// </summary>
    public sealed class GitLockFileTests
    {
        private const string Message = "fatal: Unable to create '/repo/.git/index.lock': File exists.\n\nAnother git process seems to be running in this repository";

        [Test]
        public void Test_TryFind_IndexLock_ReturnsPath()
        {
            Assert.IsTrue(GitLockFile.TryFind(Message, "/repo/.git", out string path));
            Assert.AreEqual(System.IO.Path.GetFullPath("/repo/.git/index.lock"), path);
        }

        [Test]
        public void Test_TryFind_OutsideGitDir_False()
        {
            Assert.IsFalse(GitLockFile.TryFind(Message, "/other/.git", out _));
        }

        [Test]
        public void Test_TryFind_OtherError_False()
        {
            Assert.IsFalse(GitLockFile.TryFind("error: pathspec 'x' did not match", "/repo/.git", out _));
        }
    }
}
