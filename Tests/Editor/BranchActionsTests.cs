using System.Collections.Generic;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    public sealed class BranchActionsTests
    {
        [TestCase("origin/feature/x", "feature/x")]
        [TestCase("origin/main", "main")]
        [TestCase("upstream/release/1.0", "release/1.0")]
        [TestCase("main", "main")]
        public void Test_LocalNameOf_StripsRemoteName(string remoteName, string expected)
        {
            Assert.AreEqual(expected, BranchActions.LocalNameOf(new GitBranchRef { Name = remoteName, IsRemote = true }));
        }

        [Test]
        public void Test_ResolveSwitchTarget_RemoteWithExistingLocal_UsesLocal()
        {
            GitBranchRef remote = new() { Name = "origin/feature/x", IsRemote = true };

            GitBranchRef target = BranchActions.ResolveSwitchTarget(remote, new HashSet<string> { "main", "feature/x" });

            Assert.AreNotSame(remote, target);
            Assert.AreEqual("feature/x", target.Name);
            Assert.IsFalse(target.IsRemote);
        }

        [Test]
        public void Test_ResolveSwitchTarget_RemoteWithoutLocal_KeepsRemote()
        {
            GitBranchRef remote = new() { Name = "origin/feature/y", IsRemote = true };

            GitBranchRef target = BranchActions.ResolveSwitchTarget(remote, new HashSet<string> { "main", "feature/x" });

            Assert.AreSame(remote, target);
        }

        [Test]
        public void Test_ResolveSwitchTarget_LocalBranch_KeepsLocal()
        {
            GitBranchRef local = new() { Name = "feature/x", IsRemote = false };

            GitBranchRef target = BranchActions.ResolveSwitchTarget(local, new List<string> { "feature/x" });

            Assert.AreSame(local, target);
        }
    }
}
