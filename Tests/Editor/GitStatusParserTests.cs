using System.Collections.Generic;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// <c>git status --porcelain=v1 -z --branch</c> 출력 문자열을 직접 넣어 파서만 검증한다.
    /// </summary>
    public sealed class GitStatusParserTests
    {
        [Test]
        public void Test_Parse_ReadsEntriesAndBranchHeader()
        {
            string output = "## main...origin/main [ahead 1, behind 2]\0 M Assets/a.cs\0A  Assets/b.cs\0?? Assets/c.cs\0 D Assets/d.cs\0";

            List<GitStatusEntry> entries = GitStatusParser.Parse(output, string.Empty, out GitBranchInfo branch);

            Assert.AreEqual("main", branch.Name);
            Assert.AreEqual("origin/main", branch.Upstream);
            Assert.AreEqual(1, branch.Ahead);
            Assert.AreEqual(2, branch.Behind);
            Assert.IsTrue(branch.HasCommits);
            Assert.IsFalse(branch.Detached);

            Assert.AreEqual(4, entries.Count);
            AssertEntry(entries[0], "Assets/a.cs", GitFileState.Modified, ' ', 'M');
            AssertEntry(entries[1], "Assets/b.cs", GitFileState.Added, 'A', ' ');
            AssertEntry(entries[2], "Assets/c.cs", GitFileState.Untracked, '?', '?');
            AssertEntry(entries[3], "Assets/d.cs", GitFileState.Deleted, ' ', 'D');
        }

        [Test]
        public void Test_Parse_RenameRecord_NewPathThenOriginalPath()
        {
            string output = "R  Assets/new.cs\0Assets/old.cs\0 M Assets/after.cs\0";

            List<GitStatusEntry> entries = GitStatusParser.Parse(output, string.Empty, out GitBranchInfo _);

            Assert.AreEqual(2, entries.Count);
            AssertEntry(entries[0], "Assets/new.cs", GitFileState.Renamed, 'R', ' ');
            Assert.AreEqual("Assets/old.cs", entries[0].OriginalRepoPath);
            // 원래 경로 레코드를 소비했으므로 다음 레코드가 새 항목으로 읽혀야 한다.
            AssertEntry(entries[1], "Assets/after.cs", GitFileState.Modified, ' ', 'M');
            Assert.IsNull(entries[1].OriginalRepoPath);
        }

        [Test]
        public void Test_Parse_CopyRecord_ReadsOriginalPath()
        {
            List<GitStatusEntry> entries = GitStatusParser.Parse("C  Assets/copy.cs\0Assets/source.cs\0", string.Empty, out GitBranchInfo _);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(GitFileState.Renamed, entries[0].State);
            Assert.AreEqual("Assets/copy.cs", entries[0].RepoPath);
            Assert.AreEqual("Assets/source.cs", entries[0].OriginalRepoPath);
        }

        [Test]
        public void Test_Parse_NoCommitsYet_ReadsBranchName()
        {
            GitStatusParser.Parse("## No commits yet on main\0?? Assets/a.cs\0", string.Empty, out GitBranchInfo branch);

            Assert.IsFalse(branch.HasCommits);
            Assert.AreEqual("main", branch.Name);
            Assert.IsNull(branch.Upstream);
        }

        [Test]
        public void Test_Parse_InitialCommitOn_ReadsBranchName()
        {
            GitStatusParser.Parse("## Initial commit on develop\0", string.Empty, out GitBranchInfo branch);

            Assert.IsFalse(branch.HasCommits);
            Assert.AreEqual("develop", branch.Name);
        }

        [Test]
        public void Test_Parse_DetachedHead()
        {
            GitStatusParser.Parse("## HEAD (no branch)\0", string.Empty, out GitBranchInfo branch);

            Assert.IsTrue(branch.Detached);
            Assert.AreEqual("HEAD", branch.Name);
        }

        [Test]
        public void Test_Parse_BranchWithoutUpstream()
        {
            GitStatusParser.Parse("## feature/x\0", string.Empty, out GitBranchInfo branch);

            Assert.AreEqual("feature/x", branch.Name);
            Assert.IsNull(branch.Upstream);
            Assert.AreEqual(0, branch.Ahead);
            Assert.AreEqual(0, branch.Behind);
        }

        [TestCase("## main...origin/main [ahead 3]", 3, 0)]
        [TestCase("## main...origin/main [behind 4]", 0, 4)]
        [TestCase("## main...origin/main [gone]", 0, 0)]
        [TestCase("## main...origin/main", 0, 0)]
        public void Test_Parse_Tracking_AheadBehind(string header, int ahead, int behind)
        {
            GitStatusParser.Parse(header + "\0", string.Empty, out GitBranchInfo branch);

            Assert.AreEqual("main", branch.Name);
            Assert.AreEqual("origin/main", branch.Upstream);
            Assert.AreEqual(ahead, branch.Ahead);
            Assert.AreEqual(behind, branch.Behind);
        }

        [Test]
        public void Test_Parse_EmptyOutput_ReturnsNoEntriesAndDefaultBranch()
        {
            List<GitStatusEntry> entries = GitStatusParser.Parse(string.Empty, string.Empty, out GitBranchInfo branch);

            Assert.AreEqual(0, entries.Count);
            Assert.IsNotNull(branch);
            Assert.IsTrue(branch.HasCommits);
        }

        [TestCase("DD")]
        [TestCase("AU")]
        [TestCase("UD")]
        [TestCase("UA")]
        [TestCase("DU")]
        [TestCase("AA")]
        [TestCase("UU")]
        public void Test_ToState_ConflictCodes(string code)
        {
            Assert.AreEqual(GitFileState.Conflicted, GitStatusParser.ToState(code[0], code[1]));
        }

        [TestCase("D ", GitFileState.Deleted)]
        [TestCase(" D", GitFileState.Deleted)]
        [TestCase("A ", GitFileState.Added)]
        [TestCase("AM", GitFileState.Added)]
        [TestCase("R ", GitFileState.Renamed)]
        [TestCase("RM", GitFileState.Renamed)]
        [TestCase("M ", GitFileState.Modified)]
        [TestCase("MM", GitFileState.Modified)]
        [TestCase("??", GitFileState.Untracked)]
        public void Test_ToState_NonConflictCodes(string code, GitFileState expected)
        {
            Assert.AreEqual(expected, GitStatusParser.ToState(code[0], code[1]));
        }

        [Test]
        public void Test_Parse_ConflictRecord_IsConflicted()
        {
            List<GitStatusEntry> entries = GitStatusParser.Parse("UU Assets/Scene.unity\0", string.Empty, out GitBranchInfo _);

            Assert.AreEqual(1, entries.Count);
            AssertEntry(entries[0], "Assets/Scene.unity", GitFileState.Conflicted, 'U', 'U');
        }

        [TestCase("Assets/한글 폴더/파일.png")]
        [TestCase("Assets/With Space/a b.txt")]
        [TestCase("Assets/Sprites/icon [1].png")]
        public void Test_Parse_SpecialPaths_KeptVerbatim(string path)
        {
            List<GitStatusEntry> entries = GitStatusParser.Parse($"?? {path}\0", string.Empty, out GitBranchInfo _);

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(path, entries[0].RepoPath);
            Assert.AreEqual(path, entries[0].ProjectPath);
        }

        [Test]
        public void Test_Parse_ProjectPrefix_StripsPrefixAndNullsOutsidePaths()
        {
            string output = " M client/unity/Assets/a.cs\0 M README.md\0R  client/unity/Assets/new.cs\0client/unity/Assets/old.cs\0";

            List<GitStatusEntry> entries = GitStatusParser.Parse(output, "client/unity/", out GitBranchInfo _);

            Assert.AreEqual(3, entries.Count);
            Assert.AreEqual("client/unity/Assets/a.cs", entries[0].RepoPath);
            Assert.AreEqual("Assets/a.cs", entries[0].ProjectPath);
            Assert.AreEqual("README.md", entries[1].RepoPath);
            Assert.IsNull(entries[1].ProjectPath);
            Assert.AreEqual("Assets/new.cs", entries[2].ProjectPath);
            Assert.AreEqual("client/unity/Assets/old.cs", entries[2].OriginalRepoPath);
        }

        [TestCase("Assets/a.cs", null, "Assets/a.cs")]
        [TestCase("Assets/a.cs", "", "Assets/a.cs")]
        [TestCase("sub/Assets/a.cs", "sub/", "Assets/a.cs")]
        [TestCase("other/Assets/a.cs", "sub/", null)]
        [TestCase("SUB/Assets/a.cs", "sub/", null)]
        public void Test_ToProjectPath(string repoPath, string prefix, string expected)
        {
            Assert.AreEqual(expected, GitStatusParser.ToProjectPath(repoPath, prefix));
        }

        // 서브모듈을 열었을 때: 저장소가 프로젝트 안의 하위 폴더라 그 경로를 앞에 붙인다. 프로젝트 밖이면(null) 프로젝트 경로가 없다.
        [TestCase("Editor/a.cs", "Packages/pkg/", "Packages/pkg/Editor/a.cs")]
        [TestCase("Editor/a.cs", "", "Editor/a.cs")]
        [TestCase("Editor/a.cs", null, null)]
        public void Test_ToProjectPath_RepoInProject(string repoPath, string repoInProject, string expected)
        {
            Assert.AreEqual(expected, GitStatusParser.ToProjectPath(repoPath, string.Empty, repoInProject));
        }

        [TestCase("E:/p", "E:/p/Packages/pkg", "Packages/pkg/")]
        [TestCase("E:\\p\\", "e:/P/Packages/pkg/", "Packages/pkg/")]
        [TestCase("E:/p", "E:/p", "")]
        [TestCase("E:/p", "E:/other/pkg", null)]
        [TestCase("E:/p", "E:/pkg", null)]
        public void Test_RepoInProjectOf(string projectRoot, string repoRoot, string expected)
        {
            Assert.AreEqual(expected, GitRepository.RepoInProjectOf(projectRoot, repoRoot));
        }

        private static void AssertEntry(GitStatusEntry entry, string repoPath, GitFileState state, char indexCode, char workTreeCode)
        {
            Assert.AreEqual(repoPath, entry.RepoPath);
            Assert.AreEqual(state, entry.State);
            Assert.AreEqual(indexCode, entry.IndexCode);
            Assert.AreEqual(workTreeCode, entry.WorkTreeCode);
        }
    }
}
