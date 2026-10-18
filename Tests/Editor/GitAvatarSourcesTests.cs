using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 아바타를 어디서 받을지 정하는 순수 규칙: GitHub 원격 해석, noreply 이메일, git 자격 증명 출력 읽기.
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

        [TestCase("NK-Studio", "https://github.com/NK-Studio.png?size=64")]
        [TestCase(" @kim ", "https://github.com/kim.png?size=64")]
        [TestCase("", null)]
        [TestCase("bad name", null)]
        public void Test_GitHubProfileAvatarUrl_ValidatesLogin(string login, string expected)
        {
            Assert.AreEqual(expected, GitAvatarSources.GitHubProfileAvatarUrl(login));
        }

        [TestCase("Kim@Example.com ", "kim@example.com", true)]
        [TestCase("a@b.com", "c@d.com", false)]
        [TestCase("", "", false)]
        public void Test_IsSameEmail_IgnoresCaseAndSpaces(string a, string b, bool expected)
        {
            Assert.AreEqual(expected, GitAvatarSources.IsSameEmail(a, b));
        }

        [Test]
        public void Test_ParseCredentialPassword_ReadsPasswordLine()
        {
            string output = "protocol=https\r\nhost=github.com\r\nusername=kim\r\npassword=gho_abc\r\npassword_expiry_utc=1\r\n";

            Assert.AreEqual("gho_abc", GitAvatarSources.ParseCredentialPassword(output));
        }

        [Test]
        public void Test_ParseCredentialPassword_NoPassword_ReturnsNull()
        {
            Assert.IsNull(GitAvatarSources.ParseCredentialPassword("protocol=https\nhost=github.com\n"));
        }
    }
}
