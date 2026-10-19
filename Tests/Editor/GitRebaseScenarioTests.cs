using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// rebase 상황을 임시 저장소에 실제로 만들어, Git 창이 쓰는 작업 코드 그대로 돌려 본다.
    /// <list type="bullet">
    /// <item>브랜치 메뉴의 "현재 브랜치를 이 위로 rebase"(<see cref="GitUndo.RebaseAsync"/>)</item>
    /// <item>커밋 메뉴의 Interactive Rebase 빠른 작업(<see cref="GitRewrite.RunAsync"/>)</item>
    /// </list>
    /// 성공하면 결과 히스토리·파일이 맞는지, 실패하면 저장소가 시작 전 그대로인지(rebase 도중에 멈춰 있지 않은지, 로컬 변경을 잃지 않았는지)를 본다.
    /// 실제 프로젝트 저장소와 실행 취소 목록은 건드리지 않는다(<see cref="GitRepository.UseRepositoryForTests"/>).
    /// </summary>
    public sealed class GitRebaseScenarioTests
    {
        private const int TimeoutMs = 60_000;

        private string _root;
        private string _git;
        private GitUndoJournal _journal;
        private IDisposable _scope;

        [SetUp]
        public void SetUp()
        {
            _git = GitVcsSettings.instance.GitExecutable;
            if (GitLocator.Resolve(_git).Found == false)
            {
                Assert.Ignore("git 실행 파일이 없다.");
            }
            _root = Path.Combine(Path.GetTempPath(), "GitVcsRebaseTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Git("init", "-q", "-b", "main");
            // 사용자 전역 설정(서명·autostash·줄바꿈 변환)에 결과가 흔들리지 않게 고정한다.
            Git("config", "user.name", "Tester");
            Git("config", "user.email", "tester@example.com");
            Git("config", "commit.gpgsign", "false");
            Git("config", "core.autocrlf", "false");
            Git("config", "rebase.autoStash", "false");
            Git("config", "rebase.updateRefs", "false");
            _journal = new GitUndoJournal();
            _scope = GitRepository.UseRepositoryForTests(_root, Path.Combine(_root, ".git"), _journal);
        }

        [TearDown]
        public void TearDown()
        {
            _scope?.Dispose();
            _scope = null;
            if (_root != null && Directory.Exists(_root))
            {
                // 진행 중인 rebase 가 남아도 지울 수 있게 읽기 전용 속성을 푼다.
                try
                {
                    foreach (string file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
                    {
                        File.SetAttributes(file, FileAttributes.Normal);
                    }
                    Directory.Delete(_root, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        // ───────────────────────── 브랜치 rebase ─────────────────────────

        [UnityTest]
        public IEnumerator Test_Rebase_DivergedBranchesBecomeLinear()
        {
            Commit("base", ("base.txt", "base\n"));
            Branch("feature");
            Commit("m1", ("m1.txt", "m1\n"));
            Commit("m2", ("m2.txt", "m2\n"));
            Switch("feature");
            Commit("f1", ("f1.txt", "f1\n"));
            Commit("f2", ("f2.txt", "f2\n"));
            Commit("f3", ("f3.txt", "f3\n"));
            string before = Head();

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual("feature", CurrentBranch());
            Assert.AreEqual(Rev("main"), Rev("HEAD~3"), "feature 의 세 커밋이 main 바로 위에 쌓여야 한다");
            CollectionAssert.AreEqual(new[] { "f3", "f2", "f1" }, Subjects("main..HEAD"));
            foreach (string file in new[] { "base.txt", "m1.txt", "m2.txt", "f1.txt", "f2.txt", "f3.txt" })
            {
                Assert.IsTrue(File.Exists(Path.Combine(_root, file)), file);
            }
            AssertClean();
            Assert.AreEqual(1, UndoCount(), "실행 취소 목록에 한 번 남아야 한다");
            Assert.AreEqual(before, _journal.NextUndo.HeadBefore);
        }

        [UnityTest]
        public IEnumerator Test_Rebase_AlreadyUpToDateChangesNothing()
        {
            Commit("base", ("a.txt", "a\n"));
            Branch("feature");
            Switch("feature");
            Commit("f1", ("f.txt", "f\n"));
            string before = Head();

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertSucceeded(result);
            Assert.AreEqual(before, Head());
            Assert.AreEqual(0, UndoCount(), "바뀐 것이 없으면 기록하지 않는다");
        }

        [UnityTest]
        public IEnumerator Test_Rebase_BranchWithoutOwnCommitsFastForwards()
        {
            Commit("base", ("a.txt", "a\n"));
            Branch("feature");
            Commit("m1", ("m.txt", "m\n"));

            Switch("feature");
            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertSucceeded(result);
            Assert.AreEqual(Rev("main"), Head());
            Assert.AreEqual("feature", CurrentBranch());
        }

        [UnityTest]
        public IEnumerator Test_Rebase_ConflictRestoresEverything()
        {
            Commit("base", ("a.txt", "line\n"));
            Branch("feature");
            Commit("main edit", ("a.txt", "main\n"));
            Switch("feature");
            Commit("feature edit", ("a.txt", "feature\n"));
            string before = Head();

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            Assert.IsFalse(result.Success, "충돌하면 실패로 알려야 한다");
            Assert.IsNotEmpty(result.Message);
            AssertNoOperationInProgress();
            Assert.AreEqual(before, Head(), "시작 전 커밋으로 돌아가야 한다");
            Assert.AreEqual("feature", CurrentBranch(), "분리된 HEAD 로 남으면 안 된다");
            Assert.AreEqual("feature\n", Read("a.txt"));
            AssertClean();
            Assert.AreEqual(0, UndoCount());
        }

        [UnityTest]
        public IEnumerator Test_Rebase_ConflictInLaterCommitRestoresWholeBranch()
        {
            Commit("base", ("a.txt", "line\n"), ("b.txt", "b\n"));
            Branch("feature");
            Commit("main edit", ("a.txt", "main\n"));
            Switch("feature");
            Commit("f1 ok", ("f1.txt", "f1\n"));
            Commit("f2 ok", ("f2.txt", "f2\n"));
            Commit("f3 conflict", ("a.txt", "feature\n"));
            Commit("f4 after", ("f4.txt", "f4\n"));
            string before = Head();

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            Assert.IsFalse(result.Success);
            AssertNoOperationInProgress();
            Assert.AreEqual(before, Head(), "앞의 커밋 두 개만 옮겨진 반쯤 된 상태로 남으면 안 된다");
            Assert.AreEqual("feature", CurrentBranch());
            CollectionAssert.AreEqual(new[] { "f4 after", "f3 conflict", "f2 ok", "f1 ok", "base" }, Subjects("HEAD"));
            AssertClean();
        }

        [UnityTest]
        public IEnumerator Test_Rebase_KeepsUnrelatedLocalChanges()
        {
            Commit("base", ("a.txt", "a\n"), ("local.txt", "original\n"));
            Branch("feature");
            Commit("m1", ("m.txt", "m\n"));
            Switch("feature");
            Commit("f1", ("f.txt", "f\n"));
            Write("local.txt", "edited but not committed\n");
            Write("untracked.txt", "new file\n");

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual(Rev("main"), Rev("HEAD~1"));
            Assert.AreEqual("edited but not committed\n", Read("local.txt"), "커밋하지 않은 수정이 남아야 한다");
            Assert.AreEqual("new file\n", Read("untracked.txt"));
            Assert.IsEmpty(Git("stash", "list"), "임시 stash 가 남으면 안 된다");
        }

        [UnityTest]
        public IEnumerator Test_Rebase_KeepsStagedLocalChangeContent()
        {
            Commit("base", ("a.txt", "a\n"), ("staged.txt", "original\n"));
            Branch("feature");
            Commit("m1", ("m.txt", "m\n"));
            Switch("feature");
            Commit("f1", ("f.txt", "f\n"));
            Write("staged.txt", "staged change\n");
            Git("add", "staged.txt");

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertSucceeded(result);
            Assert.AreEqual("staged change\n", Read("staged.txt"));
            Assert.IsEmpty(Git("stash", "list"));
        }

        [UnityTest]
        public IEnumerator Test_Rebase_LocalChangeOverlappingUpstreamIsNeverLost()
        {
            Commit("base", ("shared.txt", "one\n"));
            Branch("feature");
            Commit("main edits shared", ("shared.txt", "main\n"));
            Switch("feature");
            Commit("f1", ("f.txt", "f\n"));
            Write("shared.txt", "my uncommitted work\n");

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertNoOperationInProgress();
            bool inTree = Read("shared.txt").Contains("my uncommitted work");
            bool inStash = Git("stash", "list").Length > 0 && Git("stash", "show", "-p", "stash@{0}").Contains("my uncommitted work");
            Assert.IsTrue(inTree || inStash, "커밋하지 않은 작업이 작업 트리나 stash 어딘가에는 있어야 한다. 결과: " + result.Message);
            // git 은 성공으로 끝내지만 파일에 충돌 표시를 남긴다 — 사용자에게 알려야 한다.
            AssertSucceeded(result);
            Assert.AreEqual(GitText.Get("rebase.autostashConflict"), result.Error, "autostash 충돌을 안내해야 한다");
            Assert.IsTrue(inStash, "원래 변경은 stash 에 보관돼야 한다");
        }

        [UnityTest]
        public IEnumerator Test_Rebase_NormalSuccessHasNoNotice()
        {
            Commit("base", ("a.txt", "a\n"), ("local.txt", "x\n"));
            Branch("feature");
            Commit("m1", ("m.txt", "m\n"));
            Switch("feature");
            Commit("f1", ("f.txt", "f\n"));
            Write("local.txt", "edited\n");

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertSucceeded(result);
            Assert.IsEmpty(result.Error, "평범한 성공에는 안내가 없어야 한다(git 진행 메시지를 경고로 띄우면 안 된다)");
        }

        [UnityTest]
        public IEnumerator Test_Drop_LocalChangeOnDroppedFileIsReported()
        {
            // 빼는 커밋이 바꾼 파일을 사용자가 또 고치고 있다 — 빼면 그 파일이 예전 내용으로 돌아가 되돌릴 때 겹친다.
            Commit("c1", ("a.txt", "one\n"));
            Commit("c2 drop me", ("a.txt", "two\n"));
            Commit("c3", ("other.txt", "3\n"));
            Write("a.txt", "my work\n");

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Drop, "HEAD~1", null, r => result = r);

            AssertNoOperationInProgress();
            AssertSucceeded(result);
            CollectionAssert.AreEqual(new[] { "c3", "c1" }, Subjects("HEAD"));
            Assert.AreEqual(GitText.Get("rebase.autostashConflict"), result.Error, "autostash 충돌을 안내해야 한다");
            Assert.IsTrue(Git("stash", "show", "-p", "stash@{0}").Contains("my work"), "원래 변경은 stash 에 보관돼야 한다");
        }

        [UnityTest]
        public IEnumerator Test_Rebase_BranchContainingMergeKeepsAllChanges()
        {
            Commit("base", ("a.txt", "a\n"));
            Branch("feature");
            Commit("m1", ("m.txt", "m\n"));
            Switch("feature");
            Branch("side");
            Commit("f1", ("f.txt", "f\n"));
            Switch("side");
            Commit("s1", ("s.txt", "s\n"));
            Switch("feature");
            Git("merge", "-q", "--no-ff", "--no-edit", "side");
            Commit("f2", ("f2.txt", "f2\n"));

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual("0", Git("rev-list", "--count", "HEAD..main"), "main 위에 있어야 한다");
            foreach (string file in new[] { "a.txt", "m.txt", "f.txt", "s.txt", "f2.txt" })
            {
                Assert.IsTrue(File.Exists(Path.Combine(_root, file)), file + " 이 사라졌다");
            }
            AssertClean();
        }

        [UnityTest]
        public IEnumerator Test_Rebase_SkipsCommitAlreadyUpstream()
        {
            Commit("base", ("a.txt", "a\n"));
            Branch("feature");
            Switch("feature");
            Commit("fix typo", ("typo.txt", "fixed\n"));
            Commit("feature work", ("f.txt", "f\n"));
            string fix = Rev("HEAD~1");
            Switch("main");
            Git("cherry-pick", fix);
            Commit("m1", ("m.txt", "m\n"));
            Switch("feature");

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertSucceeded(result);
            CollectionAssert.AreEqual(new[] { "feature work" }, Subjects("main..HEAD"), "이미 main 에 있는 변경은 빠져야 한다");
        }

        [UnityTest]
        public IEnumerator Test_Rebase_DoesNotAbortRebaseStartedInTerminal()
        {
            Commit("base", ("a.txt", "line\n"));
            Branch("feature");
            Commit("main edit", ("a.txt", "main\n"));
            Switch("feature");
            Commit("feature edit", ("a.txt", "feature\n"));
            // 사용자가 터미널에서 시작해 충돌로 멈춘 rebase.
            GitCommand.Run(_git, _root, new[] { "rebase", "main" });
            Assume.That(RebaseInProgress(), "준비: 터미널 rebase 가 충돌로 멈춰 있어야 한다");
            string conflicted = Read("a.txt");

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            Assert.IsFalse(result.Success, "진행 중인 rebase 가 있으면 시작하지 않아야 한다");
            Assert.IsTrue(RebaseInProgress(), "사용자의 rebase 를 멋대로 취소하면 안 된다");
            Assert.AreEqual(conflicted, Read("a.txt"), "충돌 해결 중인 파일을 건드리면 안 된다");
        }

        [UnityTest]
        public IEnumerator Test_Rebase_UndoAndRedo()
        {
            Commit("base", ("a.txt", "a\n"));
            Branch("feature");
            Commit("m1", ("m.txt", "m\n"));
            Switch("feature");
            Commit("f1", ("f.txt", "f\n"));
            string before = Head();

            GitResult result = default;
            yield return Rebase("main", r => result = r);
            AssertSucceeded(result);
            string after = Head();

            yield return Await(GitUndo.UndoAsync(), r => result = r);
            AssertSucceeded(result);
            Assert.AreEqual(before, Head(), "실행 취소하면 rebase 전 커밋으로 돌아가야 한다");
            Assert.IsFalse(File.Exists(Path.Combine(_root, "m.txt")));

            yield return Await(GitUndo.RedoAsync(), r => result = r);
            AssertSucceeded(result);
            Assert.AreEqual(after, Head());
        }

        [UnityTest]
        public IEnumerator Test_Rebase_OntoBranchNameWithSlash()
        {
            Commit("base", ("a.txt", "a\n"));
            Branch("feature");
            Branch("release/1.0");
            Switch("release/1.0");
            Commit("r1", ("r.txt", "r\n"));
            Switch("feature");
            Commit("f1", ("f.txt", "f\n"));

            GitResult result = default;
            yield return Rebase("release/1.0", r => result = r);

            AssertSucceeded(result);
            Assert.AreEqual(Rev("release/1.0"), Rev("HEAD~1"));
        }

        [UnityTest]
        public IEnumerator Test_Rebase_FollowsRenameOnUpstream()
        {
            Commit("base", ("Assets/Old.cs", "class A\n{\n    int x;\n    int y;\n    int z;\n}\n"));
            Branch("feature");
            Git("mv", "Assets/Old.cs", "Assets/New.cs");
            Git("commit", "-q", "-m", "rename");
            Switch("feature");
            Commit("edit", ("Assets/Old.cs", "class A\n{\n    int x;\n    int y;\n    int z;\n    int w;\n}\n"));

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertSucceeded(result);
            Assert.IsFalse(File.Exists(Path.Combine(_root, "Assets/Old.cs")));
            StringAssert.Contains("int w;", Read("Assets/New.cs"), "이름이 바뀐 파일에 수정이 따라가야 한다");
        }

        [UnityTest]
        public IEnumerator Test_Rebase_KoreanPathsAndMessages()
        {
            Commit("기본", ("에셋/한글 파일.txt", "하나\n"));
            Branch("기능/한글");
            Commit("메인 변경", ("에셋/메인.txt", "메인\n"));
            Switch("기능/한글");
            Commit("기능: 한글 커밋 메시지", ("에셋/기능.txt", "기능\n"));

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertSucceeded(result);
            CollectionAssert.AreEqual(new[] { "기능: 한글 커밋 메시지" }, Subjects("main..HEAD"));
            Assert.AreEqual("기능\n", Read("에셋/기능.txt"));
        }

        [UnityTest]
        public IEnumerator Test_Rebase_BinaryConflictRestores()
        {
            Commit("base", ("tex.png", Binary(1)));
            Branch("feature");
            Commit("main tex", ("tex.png", Binary(2)));
            Switch("feature");
            Commit("feature tex", ("tex.png", Binary(3)));
            string before = Head();

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            Assert.IsFalse(result.Success);
            AssertNoOperationInProgress();
            Assert.AreEqual(before, Head());
            Assert.AreEqual(Binary(3), Read("tex.png"));
        }

        [UnityTest]
        public IEnumerator Test_Rebase_DetachedHeadStaysDetached()
        {
            Commit("base", ("a.txt", "a\n"));
            Branch("feature");
            Commit("m1", ("m.txt", "m\n"));
            Switch("feature");
            Commit("f1", ("f.txt", "f\n"));
            Git("switch", "-q", "--detach", "HEAD");

            GitResult result = default;
            yield return Rebase("main", r => result = r);

            AssertSucceeded(result);
            Assert.AreEqual(string.Empty, CurrentBranch());
            Assert.AreEqual(Rev("main"), Rev("HEAD~1"));
            Assert.AreNotEqual(Rev("feature"), Head(), "브랜치 feature 는 그대로다");
        }

        // ───────────────────── Interactive Rebase 빠른 작업 ─────────────────────

        [UnityTest]
        public IEnumerator Test_Reword_MiddleCommitKeepsEverythingElse()
        {
            Commit("c1", ("1.txt", "1\n"));
            Git("commit", "-q", "--allow-empty", "-m", "c2", "--author", "Other <other@example.com>", "--date", "2020-01-02T03:04:05+09:00");
            Commit("c3", ("3.txt", "3\n"));
            Commit("c4", ("4.txt", "4\n"));
            string[] treesBefore = Trees("HEAD");
            string authorBefore = Git("log", "-1", "--format=%an <%ae> %ad", "--date=raw", "HEAD~2");
            Write("4.txt", "unstaged\n");
            Write("staged.txt", "staged\n");
            Git("add", "staged.txt");
            string before = Head();

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Reword, "HEAD~2", "c2 고친 메시지\n\n본문 줄", r => result = r);

            AssertSucceeded(result);
            CollectionAssert.AreEqual(new[] { "c4", "c3", "c2 고친 메시지", "c1" }, Subjects("HEAD"));
            Assert.AreEqual("c2 고친 메시지\n\n본문 줄", Git("log", "-1", "--format=%B", "HEAD~2").Trim());
            CollectionAssert.AreEqual(treesBefore, Trees("HEAD"), "모든 커밋의 파일 내용은 그대로여야 한다");
            Assert.AreEqual(authorBefore, Git("log", "-1", "--format=%an <%ae> %ad", "--date=raw", "HEAD~2"), "작성자·날짜를 지켜야 한다");
            Assert.AreEqual("unstaged\n", Read("4.txt"));
            StringAssert.Contains("A  staged.txt", Git("status", "--porcelain"), "stage 한 변경은 stage 된 채로 남아야 한다");
            Assert.AreEqual(1, UndoCount());

            yield return Await(GitUndo.UndoAsync(), r => result = r);
            AssertSucceeded(result);
            Assert.AreEqual(before, Head(), "실행 취소하면 원래 커밋 해시로 돌아가야 한다");
            StringAssert.Contains("A  staged.txt", Git("status", "--porcelain"));
        }

        [UnityTest]
        public IEnumerator Test_Reword_HeadAndRoot()
        {
            Commit("root", ("1.txt", "1\n"));
            Commit("second", ("2.txt", "2\n"));

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Reword, "HEAD", "second reworded", r => result = r);
            AssertSucceeded(result);
            yield return Rewrite(GitRewriteKind.Reword, "HEAD~1", "root reworded", r => result = r);
            AssertSucceeded(result);

            CollectionAssert.AreEqual(new[] { "second reworded", "root reworded" }, Subjects("HEAD"));
            Assert.AreEqual(string.Empty, Git("log", "-1", "--format=%P", "HEAD~1"), "루트는 부모 없이 남아야 한다");
        }

        [UnityTest]
        public IEnumerator Test_Squash_MiddleCommitWithMessage()
        {
            Commit("c1", ("1.txt", "1\n"));
            Commit("c2", ("2.txt", "2\n"));
            Commit("c3", ("3.txt", "3\n"));
            Commit("c4", ("4.txt", "4\n"));
            string headTree = Rev("HEAD^{tree}");
            string c3Tree = Rev("HEAD~1^{tree}");

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Squash, "HEAD~1", "c2+c3 합침", r => result = r);

            AssertSucceeded(result);
            CollectionAssert.AreEqual(new[] { "c4", "c2+c3 합침", "c1" }, Subjects("HEAD"));
            Assert.AreEqual(headTree, Rev("HEAD^{tree}"));
            Assert.AreEqual(c3Tree, Rev("HEAD~1^{tree}"), "합친 커밋은 두 변경을 모두 담아야 한다");
            AssertClean();
        }

        [UnityTest]
        public IEnumerator Test_Squash_DefaultMessageAndFixup()
        {
            Commit("c1", ("1.txt", "1\n"));
            Commit("feature", ("2.txt", "2\n"));
            Commit("more feature", ("3.txt", "3\n"));
            Commit("fix", ("4.txt", "4\n"));

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Squash, "HEAD~1", null, r => result = r);
            AssertSucceeded(result);
            Assert.AreEqual("feature\n\nmore feature", Git("log", "-1", "--format=%B", "HEAD~1").Trim());

            yield return Rewrite(GitRewriteKind.Fixup, "HEAD", null, r => result = r);
            AssertSucceeded(result);
            CollectionAssert.AreEqual(new[] { "feature", "c1" }, Subjects("HEAD"));
            Assert.AreEqual("feature\n\nmore feature", Git("log", "-1", "--format=%B", "HEAD").Trim(), "fixup 은 부모 메시지만 남긴다");
            Assert.AreEqual("4\n", Read("4.txt"));
        }

        [UnityTest]
        public IEnumerator Test_Squash_IntoRootCommit()
        {
            Commit("root", ("1.txt", "1\n"));
            Commit("second", ("2.txt", "2\n"));
            Commit("third", ("3.txt", "3\n"));

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Squash, "HEAD~1", "root+second", r => result = r);

            AssertSucceeded(result);
            CollectionAssert.AreEqual(new[] { "third", "root+second" }, Subjects("HEAD"));
            Assert.AreEqual(string.Empty, Git("log", "-1", "--format=%P", "HEAD~1"));
        }

        [UnityTest]
        public IEnumerator Test_Reword_BeforeMergeKeepsMergeShape()
        {
            // 곁가지는 a 에서 갈라진다 — b 를 고쳐도 곁가지 커밋은 b 뒤가 아니라서 그대로여야 한다.
            Commit("a", ("a.txt", "a\n"));
            Branch("side");
            Commit("b", ("b.txt", "b\n"));
            Switch("side");
            Commit("s1", ("s.txt", "s\n"));
            string side = Head();
            Switch("main");
            Commit("c", ("c.txt", "c\n"));
            Git("merge", "-q", "--no-ff", "--no-edit", "side");
            Commit("d", ("d.txt", "d\n"));
            string[] treesBefore = Trees("HEAD");

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Reword, BySubject("b"), "b reworded", r => result = r);

            AssertSucceeded(result);
            Assert.AreEqual(2, Git("log", "-1", "--format=%P", "HEAD~1").Split(' ').Length, "병합 커밋은 부모 둘을 지켜야 한다");
            Assert.AreEqual(side, Rev("HEAD~1^2"), "고친 커밋 뒤가 아닌 쪽 부모는 그대로");
            CollectionAssert.AreEqual(treesBefore, Trees("HEAD"));
            StringAssert.Contains("b reworded", Git("log", "--format=%s", "--first-parent", "HEAD"));
        }

        [UnityTest]
        public IEnumerator Test_Reword_CommitOnMergedSideBranch()
        {
            Commit("a", ("a.txt", "a\n"));
            Branch("side");
            Switch("side");
            Commit("s1", ("s1.txt", "s1\n"));
            Commit("s2", ("s2.txt", "s2\n"));
            Switch("main");
            Commit("m", ("m.txt", "m\n"));
            string firstParent = Head();
            Git("merge", "-q", "--no-ff", "--no-edit", "side");

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Reword, "side~1", "s1 reworded", r => result = r);

            AssertSucceeded(result);
            Assert.AreEqual(firstParent, Rev("HEAD^1"), "첫째 부모(main 쪽)는 그대로");
            Assert.AreEqual("s1 reworded", Git("log", "-1", "--format=%s", "HEAD^2~1"));
            Assert.AreEqual("s2", Git("log", "-1", "--format=%s", "HEAD^2"));
        }

        [UnityTest]
        public IEnumerator Test_Squash_RefusesMergeCommitAndMergeParent()
        {
            Commit("a", ("a.txt", "a\n"));
            Branch("side");
            Switch("side");
            Commit("s1", ("s.txt", "s\n"));
            Switch("main");
            Commit("m", ("m.txt", "m\n"));
            Git("merge", "-q", "--no-ff", "--no-edit", "side");
            Commit("after merge", ("x.txt", "x\n"));
            string before = Head();

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Squash, "HEAD~1", null, r => result = r);
            Assert.IsFalse(result.Success, "병합 커밋은 합칠 수 없다");
            yield return Rewrite(GitRewriteKind.Squash, "HEAD", null, r => result = r);
            Assert.IsFalse(result.Success, "부모가 병합 커밋이면 합칠 수 없다");
            Assert.AreEqual(before, Head());
            Assert.AreEqual(0, UndoCount());
        }

        [UnityTest]
        public IEnumerator Test_Rewrite_CommitNotInHistoryIsRefused()
        {
            Commit("a", ("a.txt", "a\n"));
            Branch("other");
            Switch("other");
            Commit("elsewhere", ("o.txt", "o\n"));
            Switch("main");
            Commit("b", ("b.txt", "b\n"));
            string before = Head();

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Reword, "other", "nope", r => result = r);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(before, Head());
        }

        [UnityTest]
        public IEnumerator Test_Drop_MiddleCommit()
        {
            Commit("c1", ("1.txt", "1\n"));
            Commit("c2 drop me", ("2.txt", "2\n"));
            Commit("c3", ("3.txt", "3\n"));
            string before = Head();

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Drop, "HEAD~1", null, r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            CollectionAssert.AreEqual(new[] { "c3", "c1" }, Subjects("HEAD"));
            Assert.IsFalse(File.Exists(Path.Combine(_root, "2.txt")));
            Assert.AreEqual("3\n", Read("3.txt"));
            Assert.AreEqual("main", CurrentBranch());

            yield return Await(GitUndo.UndoAsync(), r => result = r);
            AssertSucceeded(result);
            Assert.AreEqual(before, Head());
        }

        [UnityTest]
        public IEnumerator Test_Drop_ConflictRestoresEverything()
        {
            Commit("c1", ("a.txt", "one\n"));
            Commit("c2 adds line", ("a.txt", "one\ntwo\n"));
            Commit("c3 edits that line", ("a.txt", "one\nTWO\n"));
            Write("local.txt", "uncommitted\n");
            string before = Head();

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Drop, "HEAD~1", null, r => result = r);

            Assert.IsFalse(result.Success);
            AssertNoOperationInProgress();
            Assert.AreEqual(before, Head());
            Assert.AreEqual("main", CurrentBranch());
            Assert.AreEqual("one\nTWO\n", Read("a.txt"));
            Assert.AreEqual("uncommitted\n", Read("local.txt"), "autostash 한 변경도 돌아와야 한다");
            Assert.IsEmpty(Git("stash", "list"));
        }

        [UnityTest]
        public IEnumerator Test_Drop_KeepsUnrelatedLocalChanges()
        {
            Commit("c1", ("1.txt", "1\n"), ("local.txt", "original\n"));
            Commit("c2 drop me", ("2.txt", "2\n"));
            Commit("c3", ("3.txt", "3\n"));
            Write("local.txt", "edited\n");

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Drop, "HEAD~1", null, r => result = r);

            AssertSucceeded(result);
            Assert.AreEqual("edited\n", Read("local.txt"));
            Assert.IsEmpty(Git("stash", "list"));
        }

        [UnityTest]
        public IEnumerator Test_Drop_HeadCommitRefusesToClobberLocalEdit()
        {
            Commit("c1", ("a.txt", "1\n"));
            Commit("c2", ("a.txt", "2\n"));
            Write("a.txt", "my edit\n");
            string before = Head();

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Drop, "HEAD", null, r => result = r);

            Assert.IsFalse(result.Success, "맨 위 커밋을 빼면 로컬 수정이 덮이므로 거부해야 한다");
            Assert.AreEqual(before, Head());
            Assert.AreEqual("my edit\n", Read("a.txt"));
        }

        [UnityTest]
        public IEnumerator Test_Drop_HeadCommit()
        {
            Commit("c1", ("1.txt", "1\n"));
            Commit("c2", ("2.txt", "2\n"));

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Drop, "HEAD", null, r => result = r);

            AssertSucceeded(result);
            CollectionAssert.AreEqual(new[] { "c1" }, Subjects("HEAD"));
            Assert.IsFalse(File.Exists(Path.Combine(_root, "2.txt")));
        }

        [UnityTest]
        public IEnumerator Test_Drop_BeforeMergeKeepsMergeShape()
        {
            Commit("a", ("a.txt", "a\n"));
            Commit("drop me", ("drop.txt", "x\n"));
            Branch("side");
            Switch("side");
            Commit("s1", ("s.txt", "s\n"));
            Switch("main");
            Commit("c", ("c.txt", "c\n"));
            Git("merge", "-q", "--no-ff", "--no-edit", "side");
            Commit("d", ("d.txt", "d\n"));

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Drop, BySubject("drop me"), null, r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual(2, Git("log", "-1", "--format=%P", "HEAD~1").Split(' ').Length, "병합 모양이 남아야 한다");
            Assert.IsFalse(File.Exists(Path.Combine(_root, "drop.txt")));
            foreach (string file in new[] { "a.txt", "s.txt", "c.txt", "d.txt" })
            {
                Assert.IsTrue(File.Exists(Path.Combine(_root, file)), file);
            }
        }

        [UnityTest]
        public IEnumerator Test_Drop_DoesNotAbortRebaseStartedInTerminal()
        {
            Commit("base", ("a.txt", "line\n"));
            Branch("feature");
            Commit("main edit", ("a.txt", "main\n"));
            Switch("feature");
            Commit("f1", ("f1.txt", "f1\n"));
            Commit("feature edit", ("a.txt", "feature\n"));
            GitCommand.Run(_git, _root, new[] { "rebase", "main" });
            Assume.That(RebaseInProgress(), "준비: 터미널 rebase 가 충돌로 멈춰 있어야 한다");

            // 멈춘 rebase 의 HEAD 히스토리에 있는 커밋을 골라야 "이미 진행 중" 확인까지 간다.
            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Drop, "HEAD~1", null, r => result = r);

            Assert.IsFalse(result.Success);
            Assert.IsTrue(RebaseInProgress(), "사용자의 rebase 를 멋대로 취소하면 안 된다");
        }

        [UnityTest]
        public IEnumerator Test_Reword_MultilineKoreanMessageWithCrLf()
        {
            Commit("c1", ("1.txt", "1\n"));
            Commit("c2", ("2.txt", "2\n"));

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Reword, "HEAD", "기능: 제목\r\n\r\n- 첫째 줄\r\n- 둘째 줄\r\n\r\n", r => result = r);

            AssertSucceeded(result);
            Assert.AreEqual("기능: 제목\n\n- 첫째 줄\n- 둘째 줄\n", Git("log", "-1", "--format=%B", "HEAD").Replace("\r", string.Empty).TrimEnd('\n') + "\n");
        }

        [UnityTest]
        public IEnumerator Test_Squash_UndoAndRedo()
        {
            Commit("c1", ("1.txt", "1\n"));
            Commit("c2", ("2.txt", "2\n"));
            Commit("c3", ("3.txt", "3\n"));
            string before = Head();

            GitResult result = default;
            yield return Rewrite(GitRewriteKind.Squash, "HEAD", "c2+c3", r => result = r);
            AssertSucceeded(result);
            string after = Head();

            yield return Await(GitUndo.UndoAsync(), r => result = r);
            AssertSucceeded(result);
            Assert.AreEqual(before, Head());
            yield return Await(GitUndo.RedoAsync(), r => result = r);
            AssertSucceeded(result);
            Assert.AreEqual(after, Head());
        }

        // ───────────────────────── 도우미 ─────────────────────────

        /// <summary>브랜치 메뉴의 rebase 와 같은 경로. 충돌하면 기본 설정대로 바로 취소한다(사용자 설정에 흔들리지 않게 고정).</summary>
        private IEnumerator Rebase(string onto, Action<GitResult> done)
        {
            return Await(GitUndo.RebaseAsync("rebase onto " + onto, onto, stopOnConflict: false), done);
        }

        private IEnumerator Rewrite(GitRewriteKind kind, string revision, string message, Action<GitResult> done)
        {
            return Await(GitRewrite.RunAsync(kind, Rev(revision), message, kind.ToString(), stopOnConflict: false), done);
        }

        /// <summary>메인 스레드에서 에디터 프레임을 넘기며 끝나기를 기다린다 — 실제 창에서 도는 것과 같은 동기화 컨텍스트다.</summary>
        private static IEnumerator Await(Task<GitResult> task, Action<GitResult> done)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(TimeoutMs);
            while (task.IsCompleted == false)
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail("작업이 시간 안에 끝나지 않았다(편집기를 기다리며 멈췄을 수 있다).");
                }
                yield return null;
            }
            if (task.IsFaulted)
            {
                throw task.Exception!.InnerException ?? task.Exception;
            }
            done(task.Result);
        }

        private string Git(params string[] args)
        {
            GitResult result = GitCommand.Run(_git, _root, args);
            Assert.IsTrue(result.Success, $"git {string.Join(" ", args)} 실패: {result.Message}");
            return result.Output.Trim();
        }

        private void Write(string path, string content)
        {
            string full = Path.Combine(_root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content, new UTF8Encoding(false));
        }

        private string Read(string path)
        {
            return File.ReadAllText(Path.Combine(_root, path), new UTF8Encoding(false));
        }

        private void Commit(string message, params (string Path, string Content)[] files)
        {
            foreach ((string path, string content) in files)
            {
                Write(path, content);
            }
            Git("add", "-A");
            Git("commit", "-q", "--allow-empty", "-m", message);
        }

        private void Branch(string name)
        {
            Git("branch", name);
        }

        private void Switch(string name)
        {
            Git("switch", "-q", name);
        }

        private string Rev(string revision)
        {
            return Git("rev-parse", "--verify", revision);
        }

        /// <summary>제목이 정확히 <paramref name="subject"/> 인 커밋(모든 브랜치에서 하나).</summary>
        private string BySubject(string subject)
        {
            foreach (string line in Git("log", "--all", "--format=%H%x09%s").Replace("\r", string.Empty).Split('\n'))
            {
                string[] parts = line.Split('\t');
                if (parts.Length == 2 && parts[1] == subject)
                {
                    return parts[0];
                }
            }
            Assert.Fail("커밋을 찾지 못했다: " + subject);
            return null;
        }

        private string Head()
        {
            return Rev("HEAD");
        }

        private string CurrentBranch()
        {
            GitResult result = GitCommand.Run(_git, _root, new[] { "symbolic-ref", "--short", "--quiet", "HEAD" });
            return result.Success ? result.Output.Trim() : string.Empty;
        }

        private string[] Subjects(string range)
        {
            return Git("log", "--format=%s", range).Replace("\r", string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        /// <summary>첫째 부모를 따라간 커밋들의 tree 해시(새 것부터).</summary>
        private string[] Trees(string revision)
        {
            return Git("log", "--first-parent", "--format=%T", revision).Replace("\r", string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private bool RebaseInProgress()
        {
            return Directory.Exists(Path.Combine(_root, ".git", "rebase-merge")) || Directory.Exists(Path.Combine(_root, ".git", "rebase-apply"));
        }

        private int UndoCount()
        {
            int count = 0;
            GitUndoJournal probe = _journal;
            // 목록을 바꾸지 않고 세려고 NextUndo 만 본다 — 이 테스트들은 한 번에 하나까지만 쌓는다.
            if (probe.NextUndo != null)
            {
                count++;
            }
            return count;
        }

        private void AssertSucceeded(GitResult result)
        {
            Assert.IsTrue(result.Success, "실패: " + result.Message);
        }

        private void AssertNoOperationInProgress()
        {
            Assert.IsFalse(RebaseInProgress(), "rebase 가 진행 중인 채로 남았다");
            foreach (string marker in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "REBASE_HEAD" })
            {
                Assert.IsFalse(File.Exists(Path.Combine(_root, ".git", marker)), marker + " 가 남았다");
            }
        }

        private void AssertClean()
        {
            Assert.IsEmpty(Git("status", "--porcelain"), "작업 트리가 깨끗해야 한다");
        }

        /// <summary>텍스트로 읽어도 깨지지 않는 "바이너리" — NUL 이 있어 git 은 바이너리로 본다.</summary>
        private static string Binary(int seed)
        {
            return "\u0089PNG\0" + new string((char)('A' + seed), 64) + "\0";
        }
    }
}
