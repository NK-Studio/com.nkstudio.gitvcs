using System;
using System.IO;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 자동 fetch(<see cref="GitAutoFetch"/>): 언제 fetch 할지(간격·실패 뒤 늘리기)와,
    /// 임시 bare 원격에 다른 클론이 푸시한 커밋을 실제로 받아 오는지.
    /// </summary>
    public sealed class GitAutoFetchTests
    {
        private static readonly DateTime Now = new(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        private static readonly TimeSpan FiveMinutes = TimeSpan.FromMinutes(5);

        [TestCase(0, 5)]
        [TestCase(1, 10)]
        [TestCase(2, 20)]
        [TestCase(3, 40)]
        [TestCase(4, 60)] // 80분 → 상한 1시간
        [TestCase(30, 60)]
        [TestCase(-1, 5)]
        public void Test_GetDelay_DoublesOnFailureUpToOneHour(int failures, int expectedMinutes)
        {
            Assert.AreEqual(TimeSpan.FromMinutes(expectedMinutes), GitAutoFetch.GetDelay(FiveMinutes, failures));
        }

        [Test]
        public void Test_GetDelay_LongIntervalIsNeverShortenedByCap()
        {
            TimeSpan twoHours = TimeSpan.FromMinutes(120);
            Assert.AreEqual(twoHours, GitAutoFetch.GetDelay(twoHours, 0));
            Assert.AreEqual(twoHours, GitAutoFetch.GetDelay(twoHours, 3));
        }

        [Test]
        public void Test_IsDue_NeverFetchedIsDueNow()
        {
            Assert.IsTrue(GitAutoFetch.IsDue(Now, null, null, FiveMinutes));
        }

        [TestCase(4, false)]
        [TestCase(5, true)]
        [TestCase(30, true)]
        public void Test_IsDue_CountsFromLastFetch(int minutesAgo, bool expected)
        {
            Assert.AreEqual(expected, GitAutoFetch.IsDue(Now, Now.AddMinutes(-minutesAgo), null, FiveMinutes));
        }

        [Test]
        public void Test_IsDue_RecentFailedAttemptDefersEvenIfLastFetchIsOld()
        {
            // 실패하면 FETCH_HEAD 가 그대로라, 시도 시각이 없으면 매번 곧바로 다시 하게 된다.
            Assert.IsFalse(GitAutoFetch.IsDue(Now, Now.AddHours(-3), Now.AddMinutes(-1), FiveMinutes));
            Assert.IsTrue(GitAutoFetch.IsDue(Now, Now.AddHours(-3), Now.AddMinutes(-6), FiveMinutes));
        }

        [Test]
        public void Test_IsDue_ManualFetchAfterAttemptRestartsCount()
        {
            // 자동 시도는 오래전, 사용자가 방금 손으로 Fetch 했다(FETCH_HEAD 가 새것).
            Assert.IsFalse(GitAutoFetch.IsDue(Now, Now.AddMinutes(-1), Now.AddMinutes(-20), FiveMinutes));
        }

        [Test]
        public void Test_IsDue_FutureTimestampDoesNotBlockForever()
        {
            // 시계를 되돌렸거나 다른 PC 와 시각이 어긋났다.
            Assert.IsTrue(GitAutoFetch.IsDue(Now, Now.AddDays(1), null, FiveMinutes));
        }

        // ── Run ── 임시 폴더에 bare 원격과 클론 둘을 만들어 실제 git 으로 확인한다.

        private string _root;
        private string _git;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "GitVcsAutoFetchTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            _git = GitVcsSettings.instance.GitExecutable;
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTree(_root);
        }

        private void RequireGit()
        {
            if (GitLocator.Resolve(_git).Found == false)
            {
                Assert.Ignore("git 실행 파일이 없다.");
            }
        }

        private string Git(string directory, params string[] args)
        {
            GitResult result = GitCommand.Run(_git, directory, args);
            Assert.IsTrue(result.Success, $"git {string.Join(" ", args)} 실패: {result.Message}");
            return result.Output.Trim();
        }

        private void Commit(string clone, string message)
        {
            Git(clone, "-c", "user.name=Tester", "-c", "user.email=tester@example.com", "commit", "--allow-empty", "-q", "-m", message);
        }

        /// <summary>원격(remote.git)·내 클론(mine)·다른 클론(other)을 만들고, 둘 다 같은 커밋에서 시작한다.</summary>
        private (string Mine, string Other) CreateRemoteWithTwoClones()
        {
            string remote = Path.Combine(_root, "remote.git");
            string mine = Path.Combine(_root, "mine");
            string other = Path.Combine(_root, "other");
            Git(_root, "init", "-q", "--bare", "-b", "main", remote);
            Git(_root, "clone", "-q", remote, mine);
            Commit(mine, "first");
            Git(mine, "push", "-q", "-u", "origin", "HEAD:main");
            Git(_root, "clone", "-q", remote, other);
            return (mine, other);
        }

        [Test]
        public void Test_Run_ReceivesCommitPushedFromAnotherClone()
        {
            RequireGit();
            (string mine, string other) = CreateRemoteWithTwoClones();
            Commit(other, "pushed elsewhere");
            Git(other, "push", "-q", "origin", "HEAD:main");
            Assert.AreEqual("0", Git(mine, "rev-list", "--count", "HEAD..@{u}"), "fetch 전에는 몰라야 한다");

            GitAutoFetchOutcome outcome = GitAutoFetch.Run(_git, mine, out string message);

            Assert.AreEqual(GitAutoFetchOutcome.Fetched, outcome, message);
            Assert.AreEqual("1", Git(mine, "rev-list", "--count", "HEAD..@{u}"), "behind 1 이어야 한다");
            Assert.AreEqual(Git(other, "rev-parse", "HEAD"), Git(mine, "rev-parse", "@{u}"));
            // fetch 는 작업 트리·현재 브랜치를 건드리지 않는다.
            Assert.AreEqual("first", Git(mine, "log", "-1", "--format=%s"));
            Assert.IsTrue(File.Exists(Path.Combine(mine, ".git", "FETCH_HEAD")), "LastFetchTime 이 FETCH_HEAD 로 갱신돼야 한다");
        }

        [Test]
        public void Test_Run_PrunesBranchDeletedOnRemote()
        {
            RequireGit();
            (string mine, string other) = CreateRemoteWithTwoClones();
            Git(other, "push", "-q", "origin", "HEAD:refs/heads/feature");
            Assert.AreEqual(GitAutoFetchOutcome.Fetched, GitAutoFetch.Run(_git, mine, out _));
            StringAssert.Contains("origin/feature", Git(mine, "branch", "-r"));

            Git(other, "push", "-q", "origin", "--delete", "feature");
            Assert.AreEqual(GitAutoFetchOutcome.Fetched, GitAutoFetch.Run(_git, mine, out _));
            StringAssert.DoesNotContain("origin/feature", Git(mine, "branch", "-r"));
        }

        [Test]
        public void Test_Run_FetchesEveryRemote()
        {
            RequireGit();
            (string mine, string other) = CreateRemoteWithTwoClones();
            string second = Path.Combine(_root, "second.git");
            Git(_root, "init", "-q", "--bare", "-b", "main", second);
            Git(other, "push", "-q", second, "HEAD:refs/heads/main");
            Git(mine, "remote", "add", "second", second);

            Assert.AreEqual(GitAutoFetchOutcome.Fetched, GitAutoFetch.Run(_git, mine, out string message), message);
            StringAssert.Contains("second/main", Git(mine, "branch", "-r"));
        }

        [Test]
        public void Test_Run_NoRemoteDoesNothing()
        {
            RequireGit();
            string repo = Path.Combine(_root, "local");
            Git(_root, "init", "-q", repo);

            Assert.AreEqual(GitAutoFetchOutcome.NoRemote, GitAutoFetch.Run(_git, repo, out _));
            Assert.IsFalse(File.Exists(Path.Combine(repo, ".git", "FETCH_HEAD")));
        }

        [Test]
        public void Test_Run_UnreachableRemoteFailsWithMessage()
        {
            RequireGit();
            string repo = Path.Combine(_root, "local");
            Git(_root, "init", "-q", repo);
            Git(repo, "remote", "add", "origin", Path.Combine(_root, "does-not-exist.git"));

            GitAutoFetchOutcome outcome = GitAutoFetch.Run(_git, repo, out string message);

            Assert.AreEqual(GitAutoFetchOutcome.Failed, outcome);
            Assert.IsNotEmpty(message);
        }

        [Test]
        public void Test_Run_NotARepositoryFails()
        {
            RequireGit();
            string plain = Path.Combine(_root, "plain");
            Directory.CreateDirectory(plain);
            // 임시 폴더 위쪽에 우연히 저장소가 있으면 결과가 달라진다.
            if (GitCommand.Run(_git, plain, new[] { "rev-parse", "--git-dir" }).Success)
            {
                Assert.Ignore("임시 폴더가 다른 git 저장소 안에 있다.");
            }

            Assert.AreEqual(GitAutoFetchOutcome.Failed, GitAutoFetch.Run(_git, plain, out _));
        }

        /// <summary>git 이 객체 파일을 읽기 전용으로 만들어 두므로 속성을 풀고 지운다.</summary>
        private static void DeleteTree(string path)
        {
            if (Directory.Exists(path) == false)
            {
                return;
            }
            try
            {
                foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }
                Directory.Delete(path, true);
            }
            catch (IOException)
            {
                // 임시 폴더라 남아도 괜찮다.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
