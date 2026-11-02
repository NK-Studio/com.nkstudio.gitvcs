using System.Collections.Generic;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// SSL 설정이 git 앞에 붙는 <c>-c</c> 인자로 바뀌는지(<see cref="GitSsl"/>, <see cref="GitCommand.WithGlobalOptions"/>).
    /// </summary>
    public sealed class GitSslTests
    {
        [Test]
        public void Test_BuildArguments_Defaults_AddsNothing()
        {
            CollectionAssert.IsEmpty(GitSsl.BuildArguments(true, string.Empty, false, false, windows: true));
        }

        [Test]
        public void Test_BuildArguments_NoVerify_OnlyDisablesVerification()
        {
            CollectionAssert.AreEqual(new[] { "-c", "http.sslVerify=false" }, GitSsl.BuildArguments(false, "C:/ca.pem", true, true, windows: true));
        }

        [Test]
        public void Test_BuildArguments_CaFile_UsesForwardSlashes()
        {
            CollectionAssert.AreEqual(new[] { "-c", "http.sslCAInfo=C:/Company Certs/root.pem" }, GitSsl.BuildArguments(true, @"C:\Company Certs\root.pem ", false, false, windows: true));
        }

        [Test]
        public void Test_BuildArguments_SystemStoreOnWindows_UsesSchannelAndIgnoresCaFile()
        {
            CollectionAssert.AreEqual(
                new[] { "-c", "http.sslBackend=schannel", "-c", "http.schannelCheckRevoke=false" },
                GitSsl.BuildArguments(true, "C:/ca.pem", true, true, windows: true));
        }

        [Test]
        public void Test_BuildArguments_SystemStoreOffWindows_FallsBackToCaFile()
        {
            CollectionAssert.AreEqual(new[] { "-c", "http.sslCAInfo=/etc/ca.pem" }, GitSsl.BuildArguments(true, "/etc/ca.pem", true, true, windows: false));
        }

        [TestCase("fatal: unable to access 'https://github.com/o/r.git/': SSL certificate problem: unable to get local issuer certificate", true)]
        [TestCase("fatal: unable to access 'https://github.com/o/r.git/': server certificate verification failed. CAfile: none CRLfile: none", true)]
        [TestCase("fatal: unable to access 'https://github.com/o/r.git/': error setting certificate verify locations:  CAfile: /nonexistent/ca.pem CApath: none", true)]
        [TestCase("fatal: unable to access 'https://github.com/o/r.git/': Could not resolve host: github.com", false)]
        [TestCase("", false)]
        public void Test_IsSslFailure(string message, bool expected)
        {
            Assert.AreEqual(expected, GitSsl.IsSslFailure(message));
        }

        [Test]
        public void Test_WithGlobalOptions_PutsSslBeforeTokenHelperAndCommand()
        {
            IReadOnlyList<string> args = GitCommand.WithGlobalOptions(new[] { "push" }, "token", new[] { "-c", "http.sslVerify=false" });

            Assert.AreEqual("-c", args[0]);
            Assert.AreEqual("http.sslVerify=false", args[1]);
            Assert.AreEqual("push", args[args.Count - 1]);
            Assert.Greater(args.Count, 3);
        }

        [Test]
        public void Test_WithGlobalOptions_NothingToAdd_ReturnsSameList()
        {
            string[] original = { "status" };

            Assert.AreSame(original, GitCommand.WithGlobalOptions(original, null, new string[0]));
        }

        [Test]
        public void Test_IsStashPush_SkipsSslOptions()
        {
            Assert.IsTrue(GitCommand.IsStashPush(GitCommand.WithGlobalOptions(new[] { "stash", "push", "-u" }, null, new[] { "-c", "http.sslVerify=false" })));
        }
    }
}
