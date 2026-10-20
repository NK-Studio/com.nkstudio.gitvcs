using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 로컬 변경이 덮어써져 실패한 git 오류(영어·한국어)를 알아보는지, 다른 오류는 거르는지 확인한다.
    /// </summary>
    public sealed class GitLocalChangesTests
    {
        [Test]
        public void Test_WouldOverwrite_EnglishMerge_True()
        {
            Assert.IsTrue(GitLocalChanges.WouldOverwrite("error: Your local changes to the following files would be overwritten by merge:\n\ta.cs\nPlease commit your changes or stash them before you merge.\nAborting"));
        }

        [Test]
        public void Test_WouldOverwrite_EnglishUntrackedCheckout_True()
        {
            Assert.IsTrue(GitLocalChanges.WouldOverwrite("error: The following untracked working tree files would be overwritten by checkout:\n\tb.cs"));
        }

        [Test]
        public void Test_WouldOverwrite_Korean_True()
        {
            Assert.IsTrue(GitLocalChanges.WouldOverwrite("error: 다음 파일의 로컬 변경 사항을 병합 때문에 덮어 쓰게 됩니다:\n\tAssets/A.cs\n병합하기 전에 변경 사항을 커밋하거나 스태시하십시오."));
        }

        [Test]
        public void Test_WouldOverwrite_OtherError_False()
        {
            Assert.IsFalse(GitLocalChanges.WouldOverwrite("fatal: Not possible to fast-forward, aborting."));
            Assert.IsFalse(GitLocalChanges.WouldOverwrite(null));
        }
    }
}
