using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 인증 실패 알아보기와 계정 도구(gh·Git Credential Manager·git credential) 출력 읽기(<see cref="GitAuth"/>).
    /// </summary>
    public sealed class GitAuthTests
    {
        [TestCase("remote: Repository not found.\nfatal: repository 'https://github.com/o/r.git/' not found")]
        [TestCase("remote: Permission to o/r.git denied to someone.\nfatal: unable to access 'https://github.com/o/r.git/': The requested URL returned error: 403")]
        [TestCase("fatal: could not read Username for 'https://github.com': terminal prompts disabled")]
        [TestCase("remote: Invalid username or password.\nfatal: Authentication failed for 'https://github.com/o/r.git/'")]
        public void Test_IsAuthFailure_AuthErrors_True(string message)
        {
            Assert.IsTrue(GitAuth.IsAuthFailure(message));
        }

        [TestCase("fatal: Not possible to fast-forward, aborting.")]
        [TestCase("error: Your local changes to the following files would be overwritten by merge")]
        [TestCase("")]
        public void Test_IsAuthFailure_OtherErrors_False(string message)
        {
            Assert.IsFalse(GitAuth.IsAuthFailure(message));
        }

        [Test]
        public void Test_ParseGhAccounts_NewAndOldFormats()
        {
            const string status = "github.com\n  ✓ Logged in to github.com account NK-Studio (keyring)\n  - Active account: true\n"
                                  + "  ✓ Logged in to github.com account hykim-pixel (keyring)\n"
                                  + "ghe.example.com\n  ✓ Logged in to ghe.example.com account other (keyring)\n"
                                  + "  ✓ Logged in to github.com as old-style (oauth_token)\n";

            CollectionAssert.AreEqual(new[] { "NK-Studio", "hykim-pixel", "old-style" }, GitAuth.ParseGhAccounts(status));
        }

        [Test]
        public void Test_ParseGcmAccounts_OnePerLine()
        {
            CollectionAssert.AreEqual(new[] { "alice", "bob" }, GitAuth.ParseGcmAccounts("alice\r\nbob\n\nalice\n"));
        }

        [Test]
        public void Test_ParseCredential_ReadsUsernameAndPassword()
        {
            (string username, string password) = GitAuth.ParseCredential("protocol=https\nhost=github.com\nusername=hykim-nk\npassword=secret\n");

            Assert.AreEqual("hykim-nk", username);
            Assert.AreEqual("secret", password);
        }

        [Test]
        public void Test_CredentialRequest_WithUsername()
        {
            Assert.AreEqual("protocol=https\nhost=github.com\npath=o/r.git\nusername=me\n\n", GitAuth.CredentialRequest("o", "r", "me"));
            Assert.AreEqual("protocol=https\nhost=github.com\npath=o/r.git\n\n", GitAuth.CredentialRequest("o", "r", null));
        }
    }
}
