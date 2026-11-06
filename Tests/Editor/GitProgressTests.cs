using System.IO;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 원격 작업의 진행(<see cref="GitProgress"/>): <c>--progress</c> 를 붙이는 규칙, 진행 줄 나누기, stderr 에서 진행 줄만 걸러 내기.
    /// </summary>
    public sealed class GitProgressTests
    {
        [TestCase("pull --ff-only", "pull --progress --ff-only")]
        [TestCase("push -u origin HEAD", "push --progress -u origin HEAD")]
        [TestCase("fetch --prune", "fetch --progress --prune")]
        [TestCase("-c http.sslVerify=false fetch --prune origin", "-c http.sslVerify=false fetch --progress --prune origin")]
        public void Test_WithProgress_NetworkCommands(string args, string expected)
        {
            Assert.AreEqual(expected, string.Join(" ", GitProgress.WithProgress(args.Split(' '))));
        }

        [TestCase("fetch --all --prune --quiet")]
        [TestCase("status --porcelain")]
        [TestCase("push --progress")]
        [TestCase("stash push --include-untracked")]
        public void Test_WithProgress_LeavesOthersAlone(string args)
        {
            Assert.AreEqual(args, string.Join(" ", GitProgress.WithProgress(args.Split(' '))));
        }

        [Test]
        public void Test_TryParse_ReceivingObjects()
        {
            Assert.IsTrue(GitProgress.TryParse("Receiving objects:  45% (1234/2700), 1.20 MiB | 2.00 MiB/s", out GitProgressLine line));
            Assert.IsFalse(line.Remote);
            Assert.AreEqual("Receiving objects", line.Phase);
            Assert.AreEqual(45, line.Percent);
            Assert.AreEqual(1234, line.Current);
            Assert.AreEqual(2700, line.Total);
            Assert.AreEqual("1.20 MiB | 2.00 MiB/s", line.Detail);
            Assert.IsFalse(line.Done);
        }

        [Test]
        public void Test_TryParse_RemoteDoneAndCountOnly()
        {
            Assert.IsTrue(GitProgress.TryParse("remote: Counting objects: 100% (12/12), done.", out GitProgressLine counting));
            Assert.IsTrue(counting.Remote);
            Assert.IsTrue(counting.Done);
            Assert.AreEqual(100, counting.Percent);
            Assert.IsTrue(GitProgress.TryParse("remote: Enumerating objects: 15, done.", out GitProgressLine enumerating));
            Assert.IsFalse(enumerating.HasPercent);
            Assert.AreEqual(15, enumerating.Current);
        }

        [TestCase("error: failed to push some refs to 'origin'")]
        [TestCase("To https://github.com/a/b.git")]
        [TestCase("fatal: repository 'x' not found")]
        [TestCase("Already up to date.")]
        [TestCase("From github.com:a/b")]
        public void Test_TryParse_RejectsMessages(string text)
        {
            Assert.IsFalse(GitProgress.TryParse(text, out _));
        }

        [Test]
        public void Test_Drain_KeepsMessagesAndPublishesProgress()
        {
            long before = GitProgress.Sequence;
            string raw = "remote: Counting objects:  50% (1/2)\rremote: Counting objects: 100% (2/2), done.\nTo origin\n   abc..def  main -> main\n";
            string kept = GitProgress.Drain(new StringReader(raw));
            Assert.AreEqual("To origin\n   abc..def  main -> main\n", kept);
            Assert.Greater(GitProgress.Sequence, before);
            Assert.IsTrue(GitProgress.TryGetLatest(out GitProgressLine last, out _));
            Assert.AreEqual(100, last.Percent);
        }
    }
}
