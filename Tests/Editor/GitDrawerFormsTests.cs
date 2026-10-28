using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 사이드바 서랍(워크트리 · 서브모듈 · Pull Request)의 규칙과 git 인자.
    /// </summary>
    public sealed class GitDrawerFormsTests
    {
        private static string Joined(string[] args)
        {
            return string.Join(" ", args);
        }

        private static List<GitBranchRef> Branches()
        {
            return new List<GitBranchRef>
            {
                new() { Name = "main", IsCurrent = true, Upstream = "origin/main", Hash = "a1" },
                new() { Name = "feature/login", Upstream = "origin/feature/login", Hash = "b2" },
                new() { Name = "origin/HEAD", IsRemote = true, Hash = "c3" },
                new() { Name = "origin/develop", IsRemote = true, Hash = "c3" },
                new() { Name = "origin/main", IsRemote = true, Hash = "a1" },
                new() { Name = "origin/feature/login", IsRemote = true, Hash = "b2" },
                new() { Name = "upstream/main", IsRemote = true, Hash = "d4" },
            };
        }

        [Test]
        public void Test_Commands()
        {
            Assert.AreEqual("worktree add -- E:/Game-x main", Joined(GitCommitCommands.AddWorktree("E:/Game-x", "main", null)));
            Assert.AreEqual("worktree add -b x -- E:/Game-x origin/x", Joined(GitCommitCommands.AddWorktree("E:/Game-x", "origin/x", "x")));
            Assert.AreEqual("submodule add -- https://github.com/a/Core.git Packages/Core", Joined(GitCommitCommands.AddSubmodule("https://github.com/a/Core.git", "Packages/Core")));
        }

        [Test]
        public void Test_WorktreeChoices_LocalRemoteTagWithoutHead()
        {
            List<GitRefChoice> choices = GitWorktreeForm.Choices(Branches(), new[] { new GitTagRef { Name = "v1" } });
            Assert.AreEqual(new[] { "feature/login", "main", "origin/develop", "origin/feature/login", "origin/main", "upstream/main", "v1" }, choices.Select(c => c.Name).ToArray());
            Assert.AreEqual(GitRefChoice.RefKind.Tag, choices.Last().Kind);
        }

        [Test]
        public void Test_WorktreeSuggestBranch()
        {
            HashSet<string> locals = new() { "main" };
            Assert.AreEqual("feature/a", GitWorktreeForm.SuggestBranch(new GitRefChoice { Name = "origin/feature/a", Kind = GitRefChoice.RefKind.Remote }, locals));
            Assert.IsNull(GitWorktreeForm.SuggestBranch(new GitRefChoice { Name = "origin/main", Kind = GitRefChoice.RefKind.Remote }, locals));
            Assert.IsNull(GitWorktreeForm.SuggestBranch(new GitRefChoice { Name = "main", Kind = GitRefChoice.RefKind.Local }, locals));
        }

        [TestCase("E:/Projects/Game", "feature/a b", "E:/Projects/Game-feature-a-b")]
        [TestCase("/Users/me/Game/", "main", "/Users/me/Game-main")]
        [TestCase("E:/Projects/Game", "", "")]
        public void Test_WorktreeSuggestPath(string root, string branch, string expected)
        {
            Assert.AreEqual(expected, GitWorktreeForm.SuggestPath(root, branch));
        }

        [Test]
        public void Test_WorktreeCheckedOutIn()
        {
            GitWorktreeRef[] worktrees = { new() { Path = "E:/Game", Branch = "main" } };
            GitRefChoice main = new() { Name = "main", Kind = GitRefChoice.RefKind.Local };
            Assert.AreEqual("E:/Game", GitWorktreeForm.CheckedOutIn(main, null, worktrees));
            Assert.IsNull(GitWorktreeForm.CheckedOutIn(main, "hotfix", worktrees));
            Assert.IsNull(GitWorktreeForm.CheckedOutIn(new GitRefChoice { Name = "dev", Kind = GitRefChoice.RefKind.Local }, null, worktrees));
        }

        [TestCase("https://github.com/a/Core.git", "Core")]
        [TestCase("git@github.com:a/Tools", "Tools")]
        [TestCase("git@host:Lib.git/", "Lib")]
        [TestCase("", "")]
        public void Test_SubmodulePathFromUrl(string url, string expected)
        {
            Assert.AreEqual(expected, GitSubmoduleForm.PathFromUrl(url));
        }

        [TestCase("Packages/Core", GitSubmoduleForm.PathProblem.None)]
        [TestCase(" ", GitSubmoduleForm.PathProblem.Empty)]
        [TestCase("/abs/path", GitSubmoduleForm.PathProblem.Absolute)]
        [TestCase("C:\\Libs", GitSubmoduleForm.PathProblem.Absolute)]
        [TestCase("Libs/../../x", GitSubmoduleForm.PathProblem.OutsideRepository)]
        [TestCase("Taken", GitSubmoduleForm.PathProblem.Exists)]
        public void Test_SubmoduleCheckPath(string path, GitSubmoduleForm.PathProblem expected)
        {
            Assert.AreEqual(expected, GitSubmoduleForm.CheckPath(path, p => p == "Taken"));
        }

        [Test]
        public void Test_GitHubRemotes_OriginFirstAndOnlyGitHub()
        {
            Dictionary<string, string> urls = new()
            {
                ["upstream"] = "https://github.com/Org/Game.git",
                ["gitlab"] = "https://gitlab.com/me/Game.git",
                ["origin"] = "git@github.com:me/Game.git",
            };
            List<GitHubRemoteRepo> repos = GitPullRequestForm.GitHubRemotes(urls);
            Assert.AreEqual(new[] { "origin", "upstream" }, repos.Select(r => r.Remote).ToArray());
            Assert.AreEqual("me/Game", repos[0].FullName);
        }

        [Test]
        public void Test_RemoteBranchesAndDefaultBase()
        {
            List<GitBranchRef> branches = Branches();
            Assert.AreEqual(new[] { "develop", "feature/login", "main" }, GitPullRequestForm.RemoteBranches(branches, "origin").ToArray());
            // origin/HEAD 가 develop 과 같은 커밋을 가리킨다.
            Assert.AreEqual("develop", GitPullRequestForm.DefaultBase(branches, "origin"));
            Assert.AreEqual("main", GitPullRequestForm.DefaultBase(branches, "upstream"));
            Assert.IsNull(GitPullRequestForm.DefaultBase(branches, "none"));
        }

        [Test]
        public void Test_HeadSpec()
        {
            GitHubRemoteRepo mine = new() { Remote = "origin", Owner = "me", Repository = "Game" };
            GitHubRemoteRepo theirs = new() { Remote = "upstream", Owner = "Org", Repository = "Game" };
            Assert.AreEqual("feature", GitPullRequestForm.HeadSpec(mine, mine, "feature"));
            Assert.AreEqual("me:feature", GitPullRequestForm.HeadSpec(mine, theirs, "feature"));
        }

        [Test]
        public void Test_PullRequestJson()
        {
            string json = GitPullRequestForm.RequestJson("Fix", null, "me:fix", "main", true);
            StringAssert.Contains("\"base\":\"main\"", json);
            StringAssert.Contains("\"head\":\"me:fix\"", json);
            StringAssert.Contains("\"draft\":true", json);
            StringAssert.Contains("\"body\":\"\"", json);
            Assert.AreEqual((12, "https://github.com/a/b/pull/12"), GitPullRequestForm.ParseCreated("{\"number\":12,\"html_url\":\"https://github.com/a/b/pull/12\"}"));
            Assert.AreEqual("No commits between main and fix",
                GitPullRequestForm.ParseError("{\"message\":\"Validation Failed\",\"errors\":[{\"resource\":\"PullRequest\",\"message\":\"No commits between main and fix\"}]}"));
            Assert.AreEqual("Not Found", GitPullRequestForm.ParseError("{\"message\":\"Not Found\"}"));
            Assert.IsNull(GitPullRequestForm.ParseError("<html>"));
        }

        [Test]
        public void Test_ParsePullRequestsAndGroups()
        {
            const string json = "[{\"number\":7,\"title\":\"Login\",\"html_url\":\"https://github.com/o/r/pull/7\",\"draft\":true,"
                                + "\"user\":{\"login\":\"Me\"},\"assignees\":[],\"requested_reviewers\":[{\"login\":\"bob\"}],"
                                + "\"head\":{\"ref\":\"feature/login\",\"sha\":\"abc\",\"repo\":{\"full_name\":\"o/r\"}},\"base\":{\"ref\":\"main\"}},"
                                + "{\"number\":8,\"title\":\"Fix\",\"user\":{\"login\":\"bob\"},\"assignees\":[{\"login\":\"me\"}],"
                                + "\"requested_reviewers\":[{\"login\":\"ME\"}],\"head\":{\"ref\":\"fix\",\"repo\":null},\"base\":{\"ref\":\"main\"}}]";
            List<GitPullRequestInfo> pulls = GitPullRequestList.Parse(json);
            Assert.AreEqual(2, pulls.Count);
            Assert.IsTrue(pulls[0].IsDraft);
            Assert.AreEqual("feature/login", pulls[0].HeadRef);
            Assert.AreEqual("o/r", pulls[0].HeadRepository);
            Assert.AreEqual("main", pulls[0].BaseRef);
            Assert.IsNull(pulls[1].HeadRepository);

            GitPullRequestSet set = new() { Login = "me", All = pulls };
            Assert.AreEqual(new[] { 7 }, GitPullRequestList.InGroup(set, GitPullRequestList.Group.Mine).Select(p => p.Number).ToArray());
            Assert.AreEqual(new[] { 8 }, GitPullRequestList.InGroup(set, GitPullRequestList.Group.AssignedToMe).Select(p => p.Number).ToArray());
            Assert.AreEqual(new[] { 8 }, GitPullRequestList.InGroup(set, GitPullRequestList.Group.AwaitingMyReview).Select(p => p.Number).ToArray());
            Assert.AreEqual(2, GitPullRequestList.InGroup(set, GitPullRequestList.Group.All).Count());
        }

        [Test]
        public void Test_BuildPullRequestSection()
        {
            GitPullRequestSet set = new()
            {
                Login = "me",
                All = { new GitPullRequestInfo { Number = 7, Title = "Login", Author = "me" }, new GitPullRequestInfo { Number = 8, Title = "Fix", Author = "bob" } },
            };
            RefNode section = RefTreeBuilder.Build(new GitRefSet { PullRequests = set }, null, s => s.ToString()).Single(r => r.Section == RefSection.PullRequests);
            Assert.AreEqual("2", section.Badge);
            Assert.AreEqual(4, section.Children.Count);
            Assert.AreEqual("1", section.Children[0].Badge);
            Assert.AreEqual("#7 Login", section.Children[0].Children[0].Label);

            // 검색하면 걸린 PR 이 있는 묶음만 남는다.
            RefNode filtered = RefTreeBuilder.Build(new GitRefSet { PullRequests = set }, "#8", s => s.ToString()).Single(r => r.Section == RefSection.PullRequests);
            Assert.AreEqual(new[] { "prList.groupAll" }, filtered.Children.Select(c => c.Label).ToArray());

            // 목록이 없으면 안내 한 줄, 개수는 0.
            RefNode signIn = RefTreeBuilder.Build(new GitRefSet { PullRequests = new GitPullRequestSet { NeedsSignIn = true } }, null, s => s.ToString())
                .Single(r => r.Section == RefSection.PullRequests);
            Assert.AreEqual("0", signIn.Badge);
            Assert.AreEqual(RefNodeKind.Info, signIn.Children.Single().Kind);
        }
    }
}
