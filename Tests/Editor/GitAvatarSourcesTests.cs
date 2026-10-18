using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 아바타를 어디서 받을지 정하는 순수 규칙: GitHub 원격 해석, noreply 이메일, 내 이메일 판단.
    /// </summary>
    public sealed class GitAvatarSourcesTests
    {
        [TestCase("https://github.com/nkstudio/arrows.git", "nkstudio", "arrows")]
        [TestCase("git@github.com:nkstudio/arrows.git", "nkstudio", "arrows")]
        [TestCase("ssh://git@github.com/nkstudio/arrows", "nkstudio", "arrows")]
        public void Test_TryParseGitHubRemote_ReadsOwnerAndRepository(string url, string owner, string repository)
        {
            Assert.IsTrue(GitAvatarSources.TryParseGitHubRemote(url, out string parsedOwner, out string parsedRepository));
            Assert.AreEqual(owner, parsedOwner);
            Assert.AreEqual(repository, parsedRepository);
        }

        [Test]
        public void Test_TryParseGitHubRemote_OtherHost_ReturnsFalse()
        {
            Assert.IsFalse(GitAvatarSources.TryParseGitHubRemote("https://gitlab.com/a/b.git", out _, out _));
        }

        [Test]
        public void Test_NoReplyAvatarUrl_UsesAccountId()
        {
            StringAssert.StartsWith("https://avatars.githubusercontent.com/u/123", GitAvatarSources.NoReplyAvatarUrl("123+kim@users.noreply.github.com"));
        }

        [TestCase("Kim@Example.com ", "kim@example.com", true)]
        [TestCase("a@b.com", "c@d.com", false)]
        [TestCase("", "", false)]
        public void Test_IsSameEmail_IgnoresCaseAndSpaces(string a, string b, bool expected)
        {
            Assert.AreEqual(expected, GitAvatarSources.IsSameEmail(a, b));
        }
    }
}
