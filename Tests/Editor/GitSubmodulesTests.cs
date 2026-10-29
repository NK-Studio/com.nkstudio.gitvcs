using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 서브모듈 편집 서랍이 읽는 git 출력 해석(<see cref="GitSubmodules"/>).
    /// </summary>
    public sealed class GitSubmodulesTests
    {
        [Test]
        public void NameForPath_FindsNameEvenWhenPathWasMoved()
        {
            const string output = "submodule.com.nkstudio.unitytable.path Packages/com.nkstudio.unitytable\n"
                                  + "submodule.libs/other.path Libs/Other\r\n";
            Assert.AreEqual("com.nkstudio.unitytable", GitSubmodules.NameForPath(output, "Packages/com.nkstudio.unitytable"));
            Assert.AreEqual("libs/other", GitSubmodules.NameForPath(output, "Libs\\Other\\"));
        }

        [Test]
        public void NameForPath_ReturnsNullWhenMissing()
        {
            Assert.IsNull(GitSubmodules.NameForPath("submodule.a.path A\n", "B"));
            Assert.IsNull(GitSubmodules.NameForPath(string.Empty, "A"));
        }

        [Test]
        public void ParseHead_ReadsHashDateAndMultilineMessage()
        {
            const string output = "e717de830e15f8b8815eb38cb3c3bdfd9286f776\n2026-10-11T16:11:00+09:00\nLink package.json\n\n- body line\n";
            (string hash, System.DateTimeOffset? date, string message) = GitSubmodules.ParseHead(output);
            Assert.AreEqual("e717de830e15f8b8815eb38cb3c3bdfd9286f776", hash);
            Assert.IsTrue(date.HasValue);
            Assert.AreEqual(new System.DateTimeOffset(2026, 10, 11, 16, 11, 0, System.TimeSpan.FromHours(9)), date.Value);
            Assert.AreEqual("Link package.json\n\n- body line", message);
        }

        [Test]
        public void SetUrl_UsesSubmoduleSetUrl()
        {
            CollectionAssert.AreEqual(new[] { "submodule", "set-url", "--", "Packages/x", "https://e/x.git" }, GitSubmodules.SetUrl("Packages/x", "https://e/x.git"));
        }
    }
}
