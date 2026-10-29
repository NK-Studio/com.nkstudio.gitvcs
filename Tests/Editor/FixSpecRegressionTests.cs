using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// docs/fix-spec.md 의 회귀 방지 검사: 운영체제 대화상자 금지(POP-13), 번역 키 중복(P1-01), Pull·Push 인자(P0-02·P1-02), Pop 을 막은 파일(P0-05).
    /// </summary>
    public sealed class FixSpecRegressionTests
    {
        private const string EditorFolder = "Packages/com.nkstudio.gitvcs/Editor";

        /// <summary>
        /// 묻기·알리기·입력은 Git 창 위 카드로만 한다. 파일·폴더를 고르는 운영체제 창(Open/Save*Panel)은 범위 밖이라 허용한다.
        /// </summary>
        [Test]
        public void Test_Sources_DoNotUseOsDialogs()
        {
            Regex forbidden = new(@"EditorUtility\.DisplayDialog|DisplayDialogComplex|\.ShowModalUtility\(|\.ShowModal\(");
            string[] offenders = Directory.GetFiles(EditorFolder, "*.cs", SearchOption.AllDirectories)
                .Where(path => forbidden.IsMatch(File.ReadAllText(path)))
                .ToArray();

            Assert.IsEmpty(offenders, "OS 대화상자를 쓰는 파일: " + string.Join(", ", offenders));
        }

        [Test]
        public void Test_GitStrings_HasNoDuplicateKeys()
        {
            string source = File.ReadAllText(Path.Combine(EditorFolder, "Core/GitStrings.cs"));
            string[] duplicates = Regex.Matches(source, "^\\s*\\[\"(?<key>[^\"]+)\"\\] = new", RegexOptions.Multiline)
                .Select(m => m.Groups["key"].Value)
                .GroupBy(key => key)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToArray();

            Assert.IsEmpty(duplicates, "중복 키: " + string.Join(", ", duplicates));
        }

        [Test]
        public void Test_CommitListMerge_KeepsSubject()
        {
            StringAssert.Contains("Merge pull request #1", GitText.Format("commitList.merge", "Merge pull request #1"));
        }

        [Test]
        public void Test_KeepsDefaultName_SavedCustomFilterIsNotDefault()
        {
            // 저장했다 읽은 직접 만든 묶음은 NameKey 가 "" 다. 이름을 그대로 두고 다시 편집해도 이름을 잃으면 안 된다.
            GitPullRequestFilter saved = new() { Name = "No Review", Query = "review:none", NameKey = string.Empty };
            Assert.IsFalse(GitPullRequestFilters.KeepsDefaultName(saved, "No Review"));
            GitPullRequestFilter builtIn = new() { NameKey = "prList.groupMine", Query = "author:@me" };
            Assert.IsTrue(GitPullRequestFilters.KeepsDefaultName(builtIn, builtIn.DisplayName));
            Assert.IsFalse(GitPullRequestFilters.KeepsDefaultName(builtIn, "Renamed"));
        }

        [TestCase("stash push --include-untracked", true)]
        [TestCase("-c credential.helper= stash push -m x --", true)]
        [TestCase("stash pop", false)]
        [TestCase("status", false)]
        public void Test_IsStashPush(string args, bool expected)
        {
            Assert.AreEqual(expected, GitCommand.IsStashPush(args.Split(' ')));
        }

        [Test]
        public void Test_LiteralStashPaths_WrapsOnlyPathsAfterSeparator()
        {
            string[] args = { "stash", "push", "--include-untracked", "-m", "msg", "--", "Assets/[Ll]ibrary.txt", ":(literal)Assets/a.txt" };
            Assert.AreEqual("stash push --include-untracked -m msg -- :(literal)Assets/[Ll]ibrary.txt :(literal)Assets/a.txt",
                string.Join(" ", GitCommand.LiteralStashPaths(args)));
            CollectionAssert.AreEqual(new[] { "stash", "push" }, GitCommand.LiteralStashPaths(new[] { "stash", "push" }));
        }

        [TestCase(GitPullMode.FastForwardOnly, "pull --ff-only")]
        [TestCase(GitPullMode.Merge, "pull --no-rebase")]
        [TestCase(GitPullMode.Rebase, "pull --rebase --autostash")]
        public void Test_PullArgs(GitPullMode mode, string expected)
        {
            Assert.AreEqual(expected, string.Join(" ", GitOperations.PullArgs(mode)));
        }

        [TestCase("true", GitPullMode.Rebase)]
        [TestCase("merges\n", GitPullMode.Rebase)]
        [TestCase("false", GitPullMode.Merge)]
        public void Test_ParsePullRebase_Configured(string value, GitPullMode expected)
        {
            Assert.AreEqual(expected, GitOperations.ParsePullRebase(value));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("nonsense")]
        public void Test_ParsePullRebase_Unset_IsNull(string value)
        {
            Assert.IsNull(GitOperations.ParsePullRebase(value));
        }

        [TestCase("1\t1\n", true, 1, 1)]
        [TestCase("0 3", true, 0, 3)]
        [TestCase("", false, 0, 0)]
        public void Test_ParseLeftRight(string output, bool ok, int ahead, int behind)
        {
            Assert.AreEqual(ok, GitOperations.ParseLeftRight(output, out int a, out int b));
            Assert.AreEqual(ahead, a);
            Assert.AreEqual(behind, b);
        }

        [Test]
        public void Test_PushArgs_NewBranch_UsesChosenRemote()
        {
            Assert.AreEqual("push -u github HEAD", string.Join(" ", GitOperations.PushArgs(new GitBranchInfo { Name = "a" }, "github")));
        }

        [Test]
        public void Test_PushArgs_TrackedBranch_PlainPush()
        {
            Assert.AreEqual("push", string.Join(" ", GitOperations.PushArgs(new GitBranchInfo { Name = "a", Upstream = "github/a" }, "origin")));
        }

        [Test]
        public void Test_OverwrittenPaths_FromStashPop()
        {
            const string message = "error: Your local changes to the following files would be overwritten by merge:\n\tAssets/Settings/URP-Balanced.asset\n\tAssets/Settings/URP-HighFidelity.asset\nPlease commit your changes or stash them before you merge.\nAborting";

            CollectionAssert.AreEqual(new[] { "Assets/Settings/URP-Balanced.asset", "Assets/Settings/URP-HighFidelity.asset" }, GitErrorAdvice.OverwrittenPaths(message));
        }

        [Test]
        public void Test_OverwrittenPaths_OtherError_Empty()
        {
            Assert.IsEmpty(GitErrorAdvice.OverwrittenPaths("fatal: something else"));
        }

        private static GitStatusEntry Entry(string repoPath, char index, char workTree)
        {
            return new GitStatusEntry
            {
                RepoPath = repoPath,
                ProjectPath = repoPath,
                IndexCode = index,
                WorkTreeCode = workTree,
                State = GitStatusParser.ToState(index, workTree),
            };
        }

        [Test]
        public void Test_MissingMetaPaths_NewAssetAndNewFolder()
        {
            GitStatusEntry[] entries =
            {
                Entry("Assets/New/a.txt", 'A', ' '),
                Entry("Assets/New.meta", '?', '?'),
                Entry("Assets/New/a.txt.meta", '?', '?'),
            };

            CollectionAssert.AreEquivalent(new[] { "Assets/New/a.txt.meta", "Assets/New.meta" }, GitStaging.MissingMetaPaths(entries));
        }

        [Test]
        public void Test_MissingMetaPaths_MetaStaged_Empty()
        {
            GitStatusEntry[] entries = { Entry("Assets/a.txt", 'A', ' '), Entry("Assets/a.txt.meta", 'A', ' ') };

            Assert.IsEmpty(GitStaging.MissingMetaPaths(entries));
        }

        [Test]
        public void Test_MissingMetaPaths_OutsideAssets_Ignored()
        {
            Assert.IsEmpty(GitStaging.MissingMetaPaths(new[] { Entry("README.md", 'A', ' ') }));
        }

        private static GitPullRequestInfo Pr(int number, string author, string[] assignees = null, string[] reviewers = null, bool draft = false, string[] labels = null)
        {
            return new GitPullRequestInfo
            {
                Number = number, Title = "PR " + number, Author = author, IsDraft = draft, HeadRef = "feature/" + number, BaseRef = "main",
                Assignees = assignees ?? System.Array.Empty<string>(), Reviewers = reviewers ?? System.Array.Empty<string>(), Labels = labels ?? System.Array.Empty<string>(),
            };
        }

        [TestCase("author:@me", true)]
        [TestCase("author:bob", false)]
        [TestCase("assignee:@me", true)]
        [TestCase("review-requested:carol", true)]
        [TestCase("involves:carol", true)]
        [TestCase("draft:true", true)]
        [TestCase("draft:false", false)]
        [TestCase("label:\"needs review\"", true)]
        [TestCase("base:main head:feature/1", true)]
        [TestCase("no:assignee", false)]
        [TestCase("author:@me draft:false", false)]
        [TestCase("PR", true)]
        public void Test_PullRequestFilter_Matches(string query, bool expected)
        {
            GitPullRequestInfo pr = Pr(1, "me", new[] { "Me" }, new[] { "carol" }, draft: true, labels: new[] { "needs review" });
            Assert.AreEqual(expected, GitPullRequestFilters.Matches(pr, query, "me"));
        }

        [TestCase("author:@me", false)]
        [TestCase("review:approved", true)]
        [TestCase("status:success author:@me", true)]
        [TestCase("no:milestone", true)]
        [TestCase("no:label", false)]
        public void Test_PullRequestFilter_NeedsSearch(string query, bool expected)
        {
            Assert.AreEqual(expected, GitPullRequestFilters.NeedsSearch(query));
        }

        [Test]
        public void Test_PullRequestFilter_SearchResultsLimitGroup()
        {
            GitPullRequestSet set = new() { Login = "me", All = { Pr(1, "me"), Pr(2, "me") } };
            set.SearchResults["review:approved"] = new System.Collections.Generic.HashSet<int> { 2 };

            CollectionAssert.AreEqual(new[] { 2 }, GitPullRequestFilters.InFilter(set, new GitPullRequestFilter { Name = "x", Query = "review:approved" }).Select(p => p.Number));
        }

        [Test]
        public void Test_ParseSearchNumbers()
        {
            CollectionAssert.AreEquivalent(new[] { 3, 9 }, GitPullRequestList.ParseSearchNumbers("{\"total_count\":2,\"items\":[{\"number\":3},{\"number\":9}]}"));
        }
    }
}
