using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// git 오류 글을 알아보고 해설에 쓸 값(원격·브랜치·파일·명령)을 뽑는지(<see cref="GitErrorAdvice"/>).
    /// </summary>
    public sealed class GitErrorAdviceTests
    {
        [TestCase("fatal: 'origin' does not appear to be a git repository\nfatal: Could not read from remote repository.\n\nPlease make sure you have the correct access rights\nand the repository exists.", GitErrorKind.NoRemote)]
        [TestCase("fatal: No configured push destination.\nEither specify the URL from the command-line or configure a remote repository using", GitErrorKind.NoRemote)]
        [TestCase("fatal: The current branch feature/a has no upstream branch.\nTo push the current branch and set the remote as upstream, use\n\n    git push --set-upstream origin feature/a\n", GitErrorKind.NoUpstream)]
        [TestCase("To https://github.com/o/r.git\n ! [rejected]        main -> main (fetch first)\nerror: failed to push some refs to 'https://github.com/o/r.git'", GitErrorKind.RemoteAhead)]
        [TestCase(" ! [rejected]        main -> main (non-fast-forward)\nerror: failed to push some refs", GitErrorKind.RemoteAhead)]
        [TestCase("fatal: unable to access 'https://github.com/o/r.git/': Could not resolve host: github.com", GitErrorKind.Network)]
        [TestCase("fatal: unable to access 'https://github.com/o/r.git/': Failed to connect to github.com port 443 after 21045 ms: Timed out", GitErrorKind.Network)]
        [TestCase("remote: Permission to o/r.git denied to someone.\nfatal: unable to access 'https://github.com/o/r.git/': The requested URL returned error: 403", GitErrorKind.Auth)]
        [TestCase("git@github.com: Permission denied (publickey).\nfatal: Could not read from remote repository.", GitErrorKind.SshKey)]
        [TestCase("error: Your local changes to the following files would be overwritten by merge:\n\tAssets/A.cs\nPlease commit your changes or stash them before you merge.\nAborting", GitErrorKind.LocalChanges)]
        [TestCase("error: The following untracked working tree files would be overwritten by checkout:\n\tAssets/B.png\nPlease move or remove them before you switch branches.", GitErrorKind.UntrackedFiles)]
        [TestCase("Auto-merging Assets/A.cs\nCONFLICT (content): Merge conflict in Assets/A.cs\nAutomatic merge failed; fix conflicts and then commit the result.", GitErrorKind.Conflict)]
        [TestCase("Author identity unknown\n\n*** Please tell me who you are.\n\nRun\n\n  git config --global user.email \"you@example.com\"", GitErrorKind.NoIdentity)]
        [TestCase("remote: error: File Assets/Big.fbx is 120.50 MB; this exceeds GitHub's file size limit of 100.00 MB\nremote: error: GH001: Large files detected.\n ! [remote rejected] main -> main (pre-receive hook declined)", GitErrorKind.FileTooLarge)]
        [TestCase("remote: error: GH006: Protected branch update failed for refs/heads/main.\n ! [remote rejected] main -> main (protected branch hook declined)", GitErrorKind.ProtectedBranch)]
        [TestCase("fatal: Unable to create 'E:/p/.git/index.lock': File exists.\n\nAnother git process seems to be running in this repository", GitErrorKind.LockFile)]
        [TestCase("fatal: something nobody has seen before", GitErrorKind.Unknown)]
        [TestCase("", GitErrorKind.Unknown)]
        public void Test_Classify_KnownMessages(string message, GitErrorKind expected)
        {
            Assert.AreEqual(expected, GitErrorAdvice.Classify(message));
        }

        [Test]
        public void Test_Explain_NoRemote_UsesRemoteName()
        {
            GitErrorAdvice advice = GitErrorAdvice.Explain("fatal: 'upstream' does not appear to be a git repository\nfatal: Could not read from remote repository.");

            Assert.AreEqual("upstream", advice.Remote);
            StringAssert.Contains("upstream", advice.Cause);
            Assert.IsFalse(string.IsNullOrEmpty(advice.Fix));
        }

        [Test]
        public void Test_Explain_NoUpstream_TakesRemoteAndBranchFromHint()
        {
            GitErrorAdvice advice = GitErrorAdvice.Explain("fatal: The current branch feature/a has no upstream branch.\n\n    git push --set-upstream origin feature/a\n", "ignored");

            Assert.AreEqual("origin", advice.Remote);
            Assert.AreEqual("feature/a", advice.Branch);
        }

        [Test]
        public void Test_Explain_LocalChanges_ListsFiles()
        {
            GitErrorAdvice advice = GitErrorAdvice.Explain("error: Your local changes to the following files would be overwritten by merge:\n\tAssets/A.cs\n\tAssets/한글 폴더/B.prefab\nPlease commit your changes or stash them before you merge.");

            CollectionAssert.AreEqual(new[] { "Assets/A.cs", "Assets/한글 폴더/B.prefab" }, advice.Files);
            StringAssert.Contains("Assets/한글 폴더/B.prefab", advice.Cause);
        }

        [Test]
        public void Test_Explain_FileTooLarge_NamesFile()
        {
            GitErrorAdvice advice = GitErrorAdvice.Explain("remote: error: File Assets/Big.fbx is 120.50 MB; this exceeds GitHub's file size limit of 100.00 MB\nremote: error: GH001: Large files detected.");

            CollectionAssert.AreEqual(new[] { "Assets/Big.fbx" }, advice.Files);
            StringAssert.Contains("Assets/Big.fbx", advice.Cause);
        }

        [Test]
        public void Test_Explain_EveryKind_HasCauseAndFix()
        {
            foreach (GitErrorKind kind in (GitErrorKind[])System.Enum.GetValues(typeof(GitErrorKind)))
            {
                Assert.IsTrue(GitText.Get($"error.{kind}.cause") != $"error.{kind}.cause", $"{kind} cause");
                Assert.IsTrue(GitText.Get($"error.{kind}.fix") != $"error.{kind}.fix", $"{kind} fix");
            }
        }
    }
}
