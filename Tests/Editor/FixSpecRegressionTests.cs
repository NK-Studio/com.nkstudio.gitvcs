using System;
using System.Collections.Generic;
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

        private const string PullDetailJson = "{\"number\":3,\"title\":\"Test B\",\"body\":\"line\\r\\nnext\",\"state\":\"open\",\"merged\":false,\"draft\":false,"
            + "\"mergeable\":true,\"mergeable_state\":\"clean\",\"html_url\":\"https://github.com/o/r/pull/3\",\"commits\":2,\"additions\":5,\"deletions\":1,"
            + "\"changed_files\":1,\"comments\":1,\"review_comments\":2,\"user\":{\"login\":\"me\"},\"requested_reviewers\":[{\"login\":\"rev\"}],"
            + "\"labels\":[{\"name\":\"bug\"}],\"head\":{\"ref\":\"feature/b\",\"sha\":\"abc\",\"repo\":{\"full_name\":\"o/r\"}},\"base\":{\"ref\":\"master\"}}";

        [Test]
        public void Test_PullRequestDetail_Parse()
        {
            GitPullRequestDetail pr = GitPullRequestDetails.Parse(PullDetailJson);
            Assert.AreEqual(3, pr.Number);
            Assert.AreEqual("line\nnext", pr.Body);
            Assert.AreEqual(true, pr.Mergeable);
            Assert.AreEqual(3, pr.Comments);
            Assert.AreEqual("feature/b", pr.HeadRef);
            Assert.AreEqual("abc", pr.HeadSha);
            CollectionAssert.AreEqual(new[] { "rev" }, pr.Reviewers);
            CollectionAssert.AreEqual(new[] { "bug" }, pr.Labels);
            Assert.AreEqual(GitPullRequestReadiness.Ready, GitPullRequestDetails.Readiness(pr));
        }

        [Test]
        public void Test_PullRequestDetail_MergeableNullIsChecking()
        {
            // JsonUtility 는 null 을 false 로 읽는다. 계산 중(null)을 충돌(false)로 잘못 보이면 안 된다.
            GitPullRequestDetail pr = GitPullRequestDetails.Parse(PullDetailJson.Replace("\"mergeable\":true", "\"mergeable\": null").Replace("clean", "unknown"));
            Assert.IsNull(pr.Mergeable);
            Assert.AreEqual(GitPullRequestReadiness.Checking, GitPullRequestDetails.Readiness(pr));
        }

        [TestCase("dirty", false, GitPullRequestReadiness.Conflicts)]
        [TestCase("blocked", true, GitPullRequestReadiness.Blocked)]
        [TestCase("behind", true, GitPullRequestReadiness.Behind)]
        [TestCase("unstable", true, GitPullRequestReadiness.Unstable)]
        [TestCase("clean", true, GitPullRequestReadiness.Ready)]
        public void Test_PullRequestDetail_Readiness(string state, bool mergeable, GitPullRequestReadiness expected)
        {
            GitPullRequestDetail pr = new() { IsOpen = true, Mergeable = mergeable, MergeableState = state };
            Assert.AreEqual(expected, GitPullRequestDetails.Readiness(pr));
            Assert.AreEqual(expected is GitPullRequestReadiness.Ready or GitPullRequestReadiness.Unstable, GitPullRequestDetails.CanMerge(expected));
        }

        [Test]
        public void Test_PullRequestDetail_ClosedDraftMerged()
        {
            Assert.AreEqual(GitPullRequestReadiness.Merged, GitPullRequestDetails.Readiness(new GitPullRequestDetail { IsMerged = true }));
            Assert.AreEqual(GitPullRequestReadiness.Closed, GitPullRequestDetails.Readiness(new GitPullRequestDetail { IsOpen = false }));
            Assert.AreEqual(GitPullRequestReadiness.Draft, GitPullRequestDetails.Readiness(new GitPullRequestDetail { IsOpen = true, IsDraft = true, Mergeable = true }));
        }

        [Test]
        public void Test_PullRequestDetail_MergeRequestJson()
        {
            Assert.AreEqual("{\"merge_method\":\"squash\",\"sha\":\"abc\"}", GitPullRequestDetails.MergeRequestJson(GitPullRequestMergeMethod.Squash, "abc"));
            Assert.AreEqual("{\"merge_method\":\"merge\"}", GitPullRequestDetails.MergeRequestJson(GitPullRequestMergeMethod.Merge, null));
        }

        [Test]
        public void Test_PullRequestDetail_ParseFiles()
        {
            List<GitPullRequestFile> files = GitPullRequestDetails.ParseFiles(
                "[{\"filename\":\"a.txt\",\"status\":\"added\",\"additions\":3,\"deletions\":0},{\"filename\":\"b.txt\",\"previous_filename\":\"old.txt\",\"status\":\"renamed\"}]");
            Assert.AreEqual(2, files.Count);
            Assert.AreEqual("added", files[0].Status);
            Assert.AreEqual(3, files[0].Additions);
            Assert.AreEqual("old.txt", files[1].PreviousPath);
            Assert.IsNull(files[0].PreviousPath);
        }

        [Test]
        public void Test_PullRequestTimeline_MergesAndSorts()
        {
            string comments = "[{\"body\":\"first\",\"created_at\":\"2026-10-29T01:00:00Z\",\"user\":{\"login\":\"a\"}}]";
            string reviews = "[{\"body\":\"\",\"state\":\"APPROVED\",\"submitted_at\":\"2026-10-29T03:00:00Z\",\"user\":{\"login\":\"b\"}},"
                + "{\"body\":\"\",\"state\":\"COMMENTED\",\"submitted_at\":\"2026-10-29T02:00:00Z\",\"user\":{\"login\":\"c\"}},"
                + "{\"body\":\"draft\",\"state\":\"PENDING\",\"user\":{\"login\":\"d\"}}]";
            string lines = "[{\"body\":\"nit\",\"path\":\"a.cs\",\"line\":0,\"original_line\":7,\"created_at\":\"2026-10-29T02:00:00Z\",\"user\":{\"login\":\"c\"}}]";

            List<GitPullRequestTimelineItem> items = GitPullRequestDetails.Timeline(comments, reviews, lines);

            // 본문 없는 COMMENTED 리뷰(줄 코멘트만 단 리뷰)와 아직 안 낸 PENDING 리뷰는 뺀다.
            Assert.AreEqual(3, items.Count);
            Assert.AreEqual(GitPullRequestTimelineKind.Comment, items[0].Kind);
            Assert.AreEqual(GitPullRequestTimelineKind.ReviewComment, items[1].Kind);
            Assert.AreEqual(7, items[1].Line);
            Assert.AreEqual("APPROVED", items[2].ReviewState);
        }

        [Test]
        public void Test_PullRequestRequests()
        {
            Assert.AreEqual("{\"body\":\"he said \\\"hi\\\"\"}", GitPullRequestDetails.CommentRequestJson("he said \"hi\""));
            Assert.AreEqual("{\"body\":\"\",\"event\":\"APPROVE\"}", GitPullRequestDetails.ReviewRequestJson(GitPullRequestReviewEvent.Approve, null));
            Assert.AreEqual("{\"body\":\"fix\",\"event\":\"REQUEST_CHANGES\"}", GitPullRequestDetails.ReviewRequestJson(GitPullRequestReviewEvent.RequestChanges, "fix"));
            Assert.IsFalse(GitPullRequestDetails.ReviewNeedsBody(GitPullRequestReviewEvent.Approve));
            Assert.IsTrue(GitPullRequestDetails.ReviewNeedsBody(GitPullRequestReviewEvent.RequestChanges));
            Assert.AreEqual("{\"state\":\"closed\"}", GitPullRequestDetails.StateRequestJson(false));
            StringAssert.Contains("convertPullRequestToDraft", GitPullRequestDetails.DraftMutationJson("PR_1", true));
            StringAssert.Contains("markPullRequestReadyForReview", GitPullRequestDetails.DraftMutationJson("PR_1", false));
            StringAssert.Contains("\"id\":\"PR_1\"", GitPullRequestDetails.DraftMutationJson("PR_1", false));
            Assert.AreEqual("bad", GitPullRequestDetails.GraphQlError("{\"data\":null,\"errors\":[{\"message\":\"bad\"}]}"));
            Assert.IsNull(GitPullRequestDetails.GraphQlError("{\"data\":{}}"));
            Assert.AreEqual("PR_kw", GitPullRequestDetails.Parse(PullDetailJson.Replace("{\"number\":3,", "{\"number\":3,\"node_id\":\"PR_kw\",")).NodeId);
        }

        [TestCase("NK wants to merge 1 commits into master", "NK wants to merge 1 commit into master")]
        [TestCase("1 files changed · +1 −0 · 1 comments", "1 file changed · +1 −0 · 1 comment")]
        [TestCase("11 files changed", "11 files changed")]
        [TestCase("2 commits, 1 branches, 1 entries", "2 commits, 1 branch, 1 entry")]
        [TestCase("Merge pull request #1 files", "Merge pull request #1 files")]
        [TestCase("v1 files and 0.1 lines", "v1 files and 0.1 lines")]
        [TestCase("PR 1 is open", "PR 1 is open")]
        public void Test_SingularizeOne(string text, string expected)
        {
            Assert.AreEqual(expected, GitText.SingularizeOne(text));
        }

        [Test]
        public void Test_ParseRepoConfig()
        {
            string output = "user.name\n김한용\0user.email\nme@example.com\0remote.origin.url\nhttps://github.com/o/r.git\0";
            Assert.AreEqual(("김한용", "me@example.com", true), GitRepository.ParseRepoConfig(output));
            Assert.AreEqual((string.Empty, string.Empty, false), GitRepository.ParseRepoConfig(string.Empty));
            Assert.AreEqual(("b", string.Empty, false), GitRepository.ParseRepoConfig("user.name\na\0user.name\nb\0"));
        }

        [Test]
        public void Test_FindRef_ExactNameOnly()
        {
            string refs = "refs/heads/main 111\nrefs/heads/main-old 222\nrefs/remotes/origin/main 333";
            Assert.AreEqual("111", GitRepository.FindRef(refs, "refs/heads/main"));
            Assert.AreEqual("333", GitRepository.FindRef(refs, "refs/remotes/origin/main"));
            Assert.IsNull(GitRepository.FindRef(refs, "refs/heads/ma"));
        }

        [Test]
        public void Test_FetchAgeText()
        {
            DateTime now = new(2026, 10, 29, 12, 0, 0);
            Assert.AreEqual(GitText.Get("actionbar.neverFetched"), GitToolbar.FetchAgeText(null, now));
            Assert.AreEqual(GitText.Get("actionbar.fetchedJustNow"), GitToolbar.FetchAgeText(now.AddSeconds(-20), now));
            Assert.AreEqual(GitText.Format("actionbar.fetchedMinutesAgo", 3), GitToolbar.FetchAgeText(now.AddMinutes(-3.5), now));
            Assert.AreEqual(GitText.Format("actionbar.fetchedHoursAgo", 2), GitToolbar.FetchAgeText(now.AddHours(-2.2), now));
            Assert.AreEqual(GitText.Format("actionbar.fetchedDaysAgo", 4), GitToolbar.FetchAgeText(now.AddDays(-4), now));
        }

        [TestCase("https://github.com/NK-Studio/www.git", true)]
        [TestCase("ssh://git@github.com/o/r.git", true)]
        [TestCase("file:///Users/me/repo.git", true)]
        [TestCase("git@github.com:NK-Studio/www.git", true)]
        [TestCase("host:repo", true)]
        [TestCase("/", false)]
        [TestCase(".", false)]
        [TestCase("https://", false)]
        [TestCase("not a url", false)]
        [TestCase("C:/repos/www", false)]
        [TestCase("/tmp/exists.git", true)]
        public void Test_IsPlausibleUrl(string url, bool expected)
        {
            Assert.AreEqual(expected, GitRemoteForm.IsPlausibleUrl(url, path => path == "/tmp/exists.git"));
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

        /// <summary>
        /// Git for Windows 가 시스템 설정에 써 둔 pull.rebase 는 사용자가 고른 값이 아니므로 묻는다(null).
        /// </summary>
        [TestCase("system\tfalse\n", null)]
        [TestCase("global\tfalse\n", GitPullMode.Merge)]
        [TestCase("local\ttrue\n", GitPullMode.Rebase)]
        [TestCase("", null)]
        public void Test_ParseScopedPullRebase(string output, GitPullMode? expected)
        {
            Assert.AreEqual(expected, GitOperations.ParseScopedPullRebase(output));
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
        public void Test_MissingMetaPaths_TrackedAssetWithUntrackedMeta()
        {
            // 예전에 .meta 없이 커밋된 파일을 고쳐 stage 했다. .meta 가 추적 중이면(목록에 없음) 경고하지 않는다.
            GitStatusEntry[] entries =
            {
                Entry("Assets/T2/b.txt", 'M', ' '),
                Entry("Assets/T2/b.txt.meta", '?', '?'),
                Entry("Assets/T2/lines.txt", 'M', ' '),
            };

            CollectionAssert.AreEquivalent(new[] { "Assets/T2/b.txt.meta" }, GitStaging.MissingMetaPaths(entries));
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

        /// <summary>
        /// fetch 하지 않은 채 Pull 하면 git 은 fetch 줄을 stderr 에, 충돌 안내를 stdout 에 쓴다. 둘 다 봐야 빨간 오류 대신 충돌 카드가 뜬다(POP-10, Windows 시험).
        /// </summary>
        [Test]
        public void Test_FailedPull_WithFetchOnStderr_IsConflict()
        {
            GitResult result = new(1,
                "Auto-merging Assets/Notes.txt\nCONFLICT (content): Merge conflict in Assets/Notes.txt\nAutomatic merge failed; fix conflicts and then commit the result.\n",
                "From E:\\remote\n   aff08ff..83dd91e  master     -> origin/master\n");

            GitErrorAdvice advice = GitErrorAdvice.Explain(result.Message);

            Assert.AreEqual(GitErrorKind.Conflict, advice.Kind);
            CollectionAssert.Contains(advice.Files, "Assets/Notes.txt");
        }
    }
}
