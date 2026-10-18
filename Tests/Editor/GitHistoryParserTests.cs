using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// <c>git log</c>, <c>diff --name-status -z</c>, <c>for-each-ref</c> 출력 문자열로 <see cref="GitHistory"/> 파서를 검증한다.
    /// 필드 구분은 0x1F, 레코드 구분은 0x1E.
    /// </summary>
    public sealed class GitHistoryParserTests
    {
        private const string F = "\x1f";
        private const string R = "\x1e";

        [Test]
        public void Test_ParseCommits_ReadsFieldsAndParents()
        {
            string output =
                "m111" + F + "m11" + F + "c222 f333" + F + "홍길동" + F + "hong@example.com" + F + "1700000000" + F + "HEAD -> main, origin/main, tag: v1.0" + F + "Merge branch 'feature/x'" + R + "\n" +
                "c222" + F + "c22" + F + "r444" + F + "Kim" + F + "kim@example.com" + F + "1690000000" + F + "" + F + "fix: a | b, c" + R + "\n" +
                "r444" + F + "r44" + F + "" + F + "Lee" + F + "lee@example.com" + F + "1680000000" + F + "" + F + "Initial commit" + R + "\n";

            List<GitCommitInfo> commits = GitHistory.ParseCommits(output);

            Assert.AreEqual(3, commits.Count);

            GitCommitInfo merge = commits[0];
            Assert.AreEqual("m111", merge.Hash);
            Assert.AreEqual("m11", merge.ShortHash);
            CollectionAssert.AreEqual(new[] { "c222", "f333" }, merge.Parents);
            Assert.IsTrue(merge.IsMerge);
            Assert.AreEqual("홍길동", merge.AuthorName);
            Assert.AreEqual("hong@example.com", merge.AuthorEmail);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1700000000).LocalDateTime, merge.Date);
            Assert.AreEqual("HEAD -> main, origin/main, tag: v1.0", merge.Refs);
            Assert.AreEqual("Merge branch 'feature/x'", merge.Subject);

            Assert.AreEqual(new[] { "r444" }, commits[1].Parents);
            Assert.IsFalse(commits[1].IsMerge);
            Assert.AreEqual(string.Empty, commits[1].Refs);
            Assert.AreEqual("fix: a | b, c", commits[1].Subject);

            Assert.AreEqual(0, commits[2].Parents.Length);
            Assert.IsFalse(commits[2].IsMerge);
        }

        [Test]
        public void Test_ParseCommits_SkipsShortRecordsAndBadDate()
        {
            string output =
                "broken" + F + "only" + R + "\n" +
                "a1" + F + "a" + F + "" + F + "N" + F + "e" + F + "not-a-number" + F + "" + F + "s" + R;

            List<GitCommitInfo> commits = GitHistory.ParseCommits(output);

            Assert.AreEqual(1, commits.Count);
            Assert.AreEqual("a1", commits[0].Hash);
            Assert.AreEqual(DateTime.MinValue, commits[0].Date);
        }

        [Test]
        public void Test_ParseCommits_EmptyOutput_ReturnsEmpty()
        {
            Assert.AreEqual(0, GitHistory.ParseCommits(string.Empty).Count);
            Assert.AreEqual(0, GitHistory.ParseCommits("\n").Count);
        }

        [Test]
        public void Test_ParseNameStatus_RenameIsOriginalThenNewPath()
        {
            string output = "M\0Assets/a.cs\0R100\0Assets/old name.cs\0Assets/새 이름.cs\0D\0Assets/d.cs\0A\0Assets/e.cs\0C075\0Assets/src.cs\0Assets/copy.cs\0";

            List<GitFileChange> changes = GitHistory.ParseNameStatus(output);

            Assert.AreEqual(5, changes.Count);

            Assert.AreEqual('M', changes[0].Status);
            Assert.AreEqual("Assets/a.cs", changes[0].RepoPath);
            Assert.IsNull(changes[0].OriginalRepoPath);
            Assert.AreEqual(GitFileState.Modified, changes[0].State);

            Assert.AreEqual('R', changes[1].Status);
            Assert.AreEqual("Assets/old name.cs", changes[1].OriginalRepoPath);
            Assert.AreEqual("Assets/새 이름.cs", changes[1].RepoPath);
            Assert.AreEqual(GitFileState.Renamed, changes[1].State);

            Assert.AreEqual(GitFileState.Deleted, changes[2].State);
            Assert.AreEqual("Assets/d.cs", changes[2].RepoPath);
            Assert.AreEqual(GitFileState.Added, changes[3].State);

            Assert.AreEqual('C', changes[4].Status);
            Assert.AreEqual("Assets/src.cs", changes[4].OriginalRepoPath);
            Assert.AreEqual("Assets/copy.cs", changes[4].RepoPath);
            Assert.AreEqual(GitFileState.Renamed, changes[4].State);

            foreach (GitFileChange change in changes)
            {
                Assert.AreEqual(GitStatusParser.ToProjectPath(change.RepoPath, GitRepository.ProjectPrefix), change.ProjectPath);
            }
        }

        [Test]
        public void Test_ParseNameStatus_TypeChangeCountsAsModified()
        {
            List<GitFileChange> changes = GitHistory.ParseNameStatus("T\0Assets/link\0");

            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual('T', changes[0].Status);
            Assert.AreEqual(GitFileState.Modified, changes[0].State);
        }

        [Test]
        public void Test_ParseNameStatus_EmptyOutput_ReturnsEmpty()
        {
            Assert.AreEqual(0, GitHistory.ParseNameStatus(string.Empty).Count);
        }

        [Test]
        public void Test_ParseBranches_ReadsFieldsAndSkipsOriginHead()
        {
            string output =
                BranchLine("refs/heads/main", "abc1234aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1700000000", "Kim", "first", "*") + "\n" +
                BranchLine("refs/remotes/origin/HEAD", "abc1234aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "1700000000", "Kim", "first", " ") + "\n" +
                BranchLine("refs/remotes/origin/feature/x", "def5678bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", "1690000000", "Lee", "feat", " ") + "\r\n";

            List<GitBranchRef> branches = GitHistory.ParseBranches(output);

            Assert.AreEqual(2, branches.Count);

            GitBranchRef main = branches[0];
            Assert.AreEqual("refs/heads/main", main.RefName);
            Assert.AreEqual("main", main.Name);
            Assert.IsFalse(main.IsRemote);
            Assert.IsTrue(main.IsCurrent);
            Assert.AreEqual("abc1234", main.ShortHash);
            Assert.AreEqual("abc1234aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", main.Hash);
            Assert.AreEqual(string.Empty, main.Upstream);
            Assert.AreEqual(string.Empty, main.Track);
            Assert.AreEqual(DateTimeOffset.FromUnixTimeSeconds(1700000000).LocalDateTime, main.Date);
            Assert.AreEqual("Kim", main.AuthorName);
            Assert.AreEqual("first", main.Subject);

            GitBranchRef remote = branches[1];
            Assert.AreEqual("origin/feature/x", remote.Name);
            Assert.IsTrue(remote.IsRemote);
            // 줄 끝 \r 은 지우고 읽는다.
            Assert.IsFalse(remote.IsCurrent);
            Assert.AreEqual("def5678bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", remote.Hash);
        }

        [Test]
        public void Test_ParseBranches_SkipsShortAndEmptyLines()
        {
            List<GitBranchRef> branches = GitHistory.ParseBranches("\n" + "refs/heads/x" + F + "x" + "\n");

            Assert.AreEqual(0, branches.Count);
        }

        [TestCase("refs/heads/feature/x", "feature/x")]
        [TestCase("refs/remotes/origin/main", "origin/main")]
        [TestCase("refs/tags/v1", "refs/tags/v1")]
        public void Test_ShortRefName_StripsBranchPrefix(string refName, string expected)
        {
            Assert.AreEqual(expected, GitHistory.ShortRefName(refName));
        }

        [Test]
        public void Test_TrackedBranchRefs_KeepsDotsInBranchName()
        {
            string output = "branch.main.merge\nrefs/heads/main\0branch.release.1.0.merge\nrefs/heads/release.1.0\0";

            List<string> refs = GitHistory.TrackedBranchRefs(output);

            CollectionAssert.AreEqual(new[] { "refs/heads/main", "refs/heads/release.1.0" }, refs);
        }

        [Test]
        public void Test_ApplyTracking_FillsOnlyListedBranches()
        {
            List<GitBranchRef> branches = GitHistory.ParseBranches(BranchLine("refs/heads/main", "abc1234aaaa", "1700000000", "Kim", "s", "*"));
            string tracking =
                BranchLine("refs/heads/main", "origin/main", "[ahead 1, behind 2]") + "\n" +
                BranchLine("refs/heads/main/sub", "origin/main/sub", "") + "\n";

            GitHistory.ApplyTracking(branches, tracking);

            Assert.AreEqual("origin/main", branches[0].Upstream);
            Assert.AreEqual("[ahead 1, behind 2]", branches[0].Track);
        }

        private static string BranchLine(params string[] fields)
        {
            return string.Join(F, fields);
        }
    }
}
