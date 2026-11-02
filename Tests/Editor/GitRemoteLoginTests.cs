using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 로그인으로 풀 수 있는 실패를 알아보고 서버·계정을 뽑는지(<see cref="GitRemoteLogin"/>), 오류 해설이 서버에 맞는지(<see cref="GitErrorAdvice"/>).
    /// </summary>
    public sealed class GitRemoteLoginTests
    {
        [Test]
        public void Test_ParseFailure_NasPasswordServer_IsSshLogin()
        {
            RemoteLoginTarget? target = GitRemoteLogin.ParseFailure("tester@nas.local: Permission denied (publickey,password).\nfatal: Could not read from remote repository.");

            Assert.IsTrue(target.HasValue);
            Assert.AreEqual(RemoteLoginKind.Ssh, target.Value.Kind);
            Assert.AreEqual("nas.local", target.Value.Host);
            Assert.AreEqual("tester", target.Value.User);
        }

        [Test]
        public void Test_ParseFailure_KeyOnlyServer_IsNotLogin()
        {
            Assert.IsNull(GitRemoteLogin.ParseFailure("git@github.com: Permission denied (publickey).\nfatal: Could not read from remote repository."));
        }

        [Test]
        public void Test_ParseFailure_NewSshHost_NeedsRemoteUrl()
        {
            RemoteLoginTarget? target = GitRemoteLogin.ParseFailure("Host key verification failed.\nfatal: Could not read from remote repository.");

            Assert.IsTrue(target.HasValue);
            Assert.AreEqual(RemoteLoginKind.Ssh, target.Value.Kind);
            Assert.IsNull(target.Value.Host);
        }

        [Test]
        public void Test_ParseFailure_ChangedHostKey_IsNotLogin()
        {
            Assert.IsNull(GitRemoteLogin.ParseFailure("@@@ WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED! @@@\nHost key verification failed."));
        }

        [TestCase("fatal: could not read Username for 'https://gitlab.company.com': terminal prompts disabled", "gitlab.company.com")]
        [TestCase("remote: HTTP Basic: Access denied\nfatal: Authentication failed for 'https://git.company.com:8443/team/game.git/'", "git.company.com:8443")]
        public void Test_ParseFailure_HttpsCompanyServer_IsHttpsLogin(string message, string host)
        {
            RemoteLoginTarget? target = GitRemoteLogin.ParseFailure(message);

            Assert.IsTrue(target.HasValue);
            Assert.AreEqual(RemoteLoginKind.Https, target.Value.Kind);
            Assert.AreEqual(host, target.Value.Host);
        }

        [Test]
        public void Test_ParseFailure_HttpsGitHub_LeftToAccountDialog()
        {
            Assert.IsNull(GitRemoteLogin.ParseFailure("fatal: could not read Username for 'https://github.com': terminal prompts disabled"));
        }

        [TestCase("ssh://tester@localhost:2222/test.git", RemoteLoginKind.Ssh, "localhost", "tester")]
        [TestCase("tester@nas.local:volume1/git/game.git", RemoteLoginKind.Ssh, "nas.local", "tester")]
        [TestCase("ssh://nas.local/git/game.git", RemoteLoginKind.Ssh, "nas.local", null)]
        [TestCase("https://hong@gitlab.company.com/team/game.git", RemoteLoginKind.Https, "gitlab.company.com", "hong")]
        [TestCase("https://git.company.com:8443/team/game.git\n", RemoteLoginKind.Https, "git.company.com:8443", null)]
        public void Test_ParseUrl(string url, RemoteLoginKind kind, string host, string user)
        {
            RemoteLoginTarget? target = GitRemoteLogin.ParseUrl(url);

            Assert.IsTrue(target.HasValue);
            Assert.AreEqual(kind, target.Value.Kind);
            Assert.AreEqual(host, target.Value.Host);
            Assert.AreEqual(user, target.Value.User);
        }

        [TestCase("/Users/me/repos/game.git")]
        [TestCase("C:/repos/game.git")]
        [TestCase("")]
        public void Test_ParseUrl_LocalPath_IsNull(string url)
        {
            Assert.IsNull(GitRemoteLogin.ParseUrl(url));
        }

        [Test]
        public void Test_AskPassScript_AnswersOnlyRememberedHost()
        {
            StringAssert.Contains("*\"$GITVCS_SSH_HOST\"*assword*", GitRemoteLogin.AskPassScript);
            StringAssert.Contains("*\"$GITVCS_SSH_HOST\"*\"(yes/no\"*", GitRemoteLogin.AskPassScript);
            StringAssert.EndsWith("exit 1\n", GitRemoteLogin.AskPassScript);
        }

        [TestCase("tester@nas.local: Permission denied (publickey,password).", GitErrorKind.SshKey)]
        [TestCase("Host key verification failed.\nfatal: Could not read from remote repository.", GitErrorKind.SshHostKey)]
        [TestCase("@@@ WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED! @@@\nHost key verification failed.", GitErrorKind.SshHostChanged)]
        public void Test_Classify_SshFailures(string message, GitErrorKind expected)
        {
            Assert.AreEqual(expected, GitErrorAdvice.Classify(message));
        }

        [Test]
        public void Test_Explain_SshKey_NamesServerNotGitHub()
        {
            GitErrorAdvice nas = GitErrorAdvice.Explain("tester@nas.local: Permission denied (publickey,password).");
            GitErrorAdvice github = GitErrorAdvice.Explain("git@github.com: Permission denied (publickey).");

            StringAssert.Contains("nas.local", nas.Cause);
            Assert.AreNotEqual(github.Fix, nas.Fix);
        }
    }
}
