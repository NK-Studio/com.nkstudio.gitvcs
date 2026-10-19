using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 충돌로 멈춘 병합·cherry-pick·revert·rebase 를 임시 저장소에 실제로 만들어, Git 창이 쓰는 작업 코드 그대로 풀고 이어가거나 취소해 본다.
    /// <list type="bullet">
    /// <item>진행 중 작업 알아보기(<see cref="GitInProgress.Detect"/>)·이어가기를 막는 까닭(<see cref="GitInProgress.ContinueBlocker"/>)</item>
    /// <item>배너의 이어가기·건너뛰기·취소(<see cref="GitInProgress.ContinueAsync"/> 등)와 우클릭 해결(<see cref="GitInProgress.ResolveAsync"/>)</item>
    /// <item>멈춘 병합 중 커밋(<see cref="GitUndo.CommitAsync"/>·<see cref="GitUndo.CommitStagedAsync"/>) — 부분 커밋 거부 문제</item>
    /// <item>"rebase 충돌 시 멈추기"(<see cref="GitUndo.RebaseAsync"/> 의 stopOnConflict)</item>
    /// </list>
    /// 실제 프로젝트 저장소와 실행 취소 목록은 건드리지 않는다(<see cref="GitRepository.UseRepositoryForTests"/>).
    /// </summary>
    public sealed class GitInProgressScenarioTests
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
            _root = Path.Combine(Path.GetTempPath(), "GitVcsInProgressTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
            Git("init", "-q", "-b", "main");
            Git("config", "user.name", "Tester");
            Git("config", "user.email", "tester@example.com");
            Git("config", "commit.gpgsign", "false");
            Git("config", "core.autocrlf", "false");
            Git("config", "rebase.autoStash", "false");
            Git("config", "rebase.updateRefs", "false");
            Git("config", "merge.conflictStyle", "merge");
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

        // ───────────────────────── 알아보기 ─────────────────────────

        [Test]
        public void Test_Detect_NothingInProgress()
        {
            Commit("base", ("a.txt", "a\n"));
            Assert.AreEqual(GitOperationKind.None, GitRepository.InProgressOperation);
        }

        [Test]
        public void Test_Detect_MarkerFiles()
        {
            string gitDir = Path.Combine(_root, ".git");
            Commit("base", ("a.txt", "a\n"));
            string head = Rev("HEAD");

            File.WriteAllText(Path.Combine(gitDir, "REVERT_HEAD"), head);
            Assert.AreEqual(GitOperationKind.Revert, GitInProgress.Detect(gitDir));
            File.WriteAllText(Path.Combine(gitDir, "CHERRY_PICK_HEAD"), head);
            Assert.AreEqual(GitOperationKind.CherryPick, GitInProgress.Detect(gitDir));
            File.WriteAllText(Path.Combine(gitDir, "MERGE_HEAD"), head);
            Assert.AreEqual(GitOperationKind.Merge, GitInProgress.Detect(gitDir));
            Directory.CreateDirectory(Path.Combine(gitDir, "rebase-merge"));
            Assert.AreEqual(GitOperationKind.Rebase, GitInProgress.Detect(gitDir), "rebase 가 병합 표시보다 먼저다");
        }

        [Test]
        public void Test_Detect_SequencerWithoutHeadFile()
        {
            string gitDir = Path.Combine(_root, ".git");
            Directory.CreateDirectory(Path.Combine(gitDir, "sequencer"));
            File.WriteAllText(Path.Combine(gitDir, "sequencer", "todo"), "revert 0123456 old\n");
            Assert.AreEqual(GitOperationKind.Revert, GitInProgress.Detect(gitDir));
            File.WriteAllText(Path.Combine(gitDir, "sequencer", "todo"), "pick 0123456 old\n");
            Assert.AreEqual(GitOperationKind.CherryPick, GitInProgress.Detect(gitDir));
        }

        [Test]
        public void Test_Detect_GitAmIsNotARebase()
        {
            string apply = Path.Combine(_root, ".git", "rebase-apply");
            Directory.CreateDirectory(apply);
            File.WriteAllText(Path.Combine(apply, "applying"), string.Empty);
            Assert.AreEqual(GitOperationKind.None, GitInProgress.Detect(Path.Combine(_root, ".git")));
        }

        [Test]
        public void Test_HasConflictMarkers()
        {
            Write("marked.txt", "a\n<<<<<<< HEAD\nmine\n=======\ntheirs\n>>>>>>> other\nb\n");
            Write("clean.txt", "a\n=======\nlooks like a heading\n");
            Write("binary.bin", "<<<<<<< x\0\n>>>>>>> y\n");
            Assert.IsTrue(GitInProgress.HasConflictMarkers(Path.Combine(_root, "marked.txt")));
            Assert.IsFalse(GitInProgress.HasConflictMarkers(Path.Combine(_root, "clean.txt")));
            Assert.IsFalse(GitInProgress.HasConflictMarkers(Path.Combine(_root, "binary.bin")), "바이너리는 검사하지 않는다");
            Assert.IsFalse(GitInProgress.HasConflictMarkers(Path.Combine(_root, "missing.txt")));
        }

        // ───────────────────────── 병합 ─────────────────────────

        [UnityTest]
        public IEnumerator Test_Merge_ConflictStopsAndIsDetected()
        {
            MakeConflictingBranches();

            GitResult result = default;
            yield return Await(GitUndo.MoveHeadAsync("merge", GitCommitCommands.Merge("feature", GitMergeMode.Default)), r => result = r);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(GitOperationKind.Merge, GitRepository.InProgressOperation);
            StringAssert.Contains(GitInProgress.DisplayName(GitOperationKind.Merge), result.Message, "배너에서 풀라는 안내가 있어야 한다");
            Assert.AreEqual(1, Status().Count(e => e.State == GitFileState.Conflicted));
            Assert.IsNotNull(GitInProgress.ContinueBlocker(GitOperationKind.Merge, Status()), "충돌이 남으면 이어갈 수 없다");
            StringAssert.Contains("feature", GitInProgress.ReadPreparedMessage(Path.Combine(_root, ".git")), "git 이 준비한 병합 메시지를 읽는다");
            Assert.AreEqual(0, UndoCount(), "멈춘 동안은 기록하지 않는다");
        }

        [UnityTest]
        public IEnumerator Test_Merge_CommitSelectedFilesFinishesMerge()
        {
            // Unity VCS 형태(고른 파일만 커밋)는 git commit --only 를 쓰는데, 병합 중에는 git 이 부분 커밋을 거부한다.
            Commit("base", ("a.txt", "line\n"), ("local.txt", "original\n"));
            Branch("feature");
            Commit("main edit", ("a.txt", "main\n"));
            Switch("feature");
            Commit("feature edit", ("a.txt", "feature\n"), ("added.txt", "added\n"));
            Switch("main");
            string before = Head();
            yield return Await(GitUndo.MoveHeadAsync("merge", GitCommitCommands.Merge("feature", GitMergeMode.Default)), _ => { });
            Assume.That(GitRepository.InProgressOperation == GitOperationKind.Merge);

            Write("a.txt", "resolved\n");
            Write("local.txt", "not part of the merge\n");
            GitResult result = default;
            yield return Await(GitUndo.CommitAsync(Status().Where(e => e.RepoPath == "a.txt").ToList(), "Merge feature"), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual(2, Git("log", "-1", "--format=%P").Split(' ').Length, "병합 커밋이어야 한다");
            Assert.AreEqual("resolved\n", Git("show", "HEAD:a.txt") + "\n");
            Assert.AreEqual("added\n", Git("show", "HEAD:added.txt") + "\n", "git 이 자동으로 합친 파일도 들어가야 한다");
            Assert.AreEqual("original", Git("show", "HEAD:local.txt"), "고르지 않은 작업 트리 수정은 빠져야 한다");
            Assert.AreEqual("not part of the merge\n", Read("local.txt"));
            Assert.AreEqual(1, UndoCount());

            yield return Await(GitUndo.UndoAsync(), r => result = r);
            AssertSucceeded(result);
            Assert.AreEqual(before, Head(), "실행 취소로 병합 전 커밋으로 돌아간다");
        }

        [UnityTest]
        public IEnumerator Test_Merge_CommitRefusesFileWithConflictMarkers()
        {
            MakeConflictingBranches();
            yield return Await(GitUndo.MoveHeadAsync("merge", GitCommitCommands.Merge("feature", GitMergeMode.Default)), _ => { });
            string before = Head();

            GitResult result = default;
            yield return Await(GitUndo.CommitAsync(Status().Where(e => e.RepoPath == "a.txt").ToList(), "Merge"), r => result = r);

            Assert.IsFalse(result.Success, "충돌 표시가 남은 파일을 그대로 커밋하면 안 된다");
            StringAssert.Contains("a.txt", result.Message);
            Assert.AreEqual(before, Head());
            Assert.AreEqual(GitOperationKind.Merge, GitRepository.InProgressOperation);
            Assert.AreEqual(1, Status().Count(e => e.State == GitFileState.Conflicted), "해결됨으로 표시하지도 않는다");
        }

        [UnityTest]
        public IEnumerator Test_Merge_CommitRefusesWhileOtherConflictsRemain()
        {
            Commit("base", ("a.txt", "a\n"), ("b.txt", "b\n"));
            Branch("feature");
            Commit("main edit", ("a.txt", "a main\n"), ("b.txt", "b main\n"));
            Switch("feature");
            Commit("feature edit", ("a.txt", "a feature\n"), ("b.txt", "b feature\n"));
            Switch("main");
            yield return Await(GitUndo.MoveHeadAsync("merge", GitCommitCommands.Merge("feature", GitMergeMode.Default)), _ => { });
            Write("a.txt", "a resolved\n");

            GitResult result = default;
            yield return Await(GitUndo.CommitAsync(Status().Where(e => e.RepoPath == "a.txt").ToList(), "Merge"), r => result = r);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("b.txt", result.Message, "남은 충돌 파일을 알려야 한다");
            Assert.AreEqual(2, Status().Count(e => e.State == GitFileState.Conflicted), "커밋하지 못하면 인덱스를 건드리지 않는다");
        }

        [UnityTest]
        public IEnumerator Test_Merge_StagedCommitFinishesMerge()
        {
            MakeConflictingBranches();
            yield return Await(GitUndo.MoveHeadAsync("merge", GitCommitCommands.Merge("feature", GitMergeMode.Default)), _ => { });

            GitResult result = default;
            yield return Await(GitUndo.CommitStagedAsync("Merge", amend: false), r => result = r);
            Assert.IsFalse(result.Success, "충돌이 남으면 거부한다");
            StringAssert.Contains("a.txt", result.Message);

            Write("a.txt", "resolved\n");
            Git("add", "a.txt");
            yield return Await(GitUndo.CommitStagedAsync("Merge", amend: false), r => result = r);
            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual(2, Git("log", "-1", "--format=%P").Split(' ').Length);
        }

        [UnityTest]
        public IEnumerator Test_Merge_ResolveTheirsAndContinue()
        {
            MakeConflictingBranches();
            string before = Head();
            yield return Await(GitUndo.MoveHeadAsync("merge", GitCommitCommands.Merge("feature", GitMergeMode.Default)), _ => { });

            GitResult result = default;
            yield return Await(GitInProgress.ResolveAsync(Conflicted("a.txt"), GitConflictSide.Theirs), r => result = r);
            AssertSucceeded(result);
            Assert.IsNull(GitInProgress.ContinueBlocker(GitOperationKind.Merge, Status()), "다 풀었으면 이어갈 수 있다");

            yield return Await(GitInProgress.ContinueAsync(), r => result = r);
            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual("feature\n", Read("a.txt"));
            Assert.AreEqual(2, Git("log", "-1", "--format=%P").Split(' ').Length);
            StringAssert.Contains("feature", Git("log", "-1", "--format=%s"), "git 이 준비한 병합 메시지를 쓴다");
            AssertClean();
            Assert.AreEqual(1, UndoCount());
            Assert.AreEqual(before, _journal.NextUndo.HeadBefore);
        }

        [UnityTest]
        public IEnumerator Test_Merge_ResolveOurs()
        {
            MakeConflictingBranches();
            yield return Await(GitUndo.MoveHeadAsync("merge", GitCommitCommands.Merge("feature", GitMergeMode.Default)), _ => { });

            GitResult result = default;
            yield return Await(GitInProgress.ResolveAsync(Conflicted("a.txt"), GitConflictSide.Ours), r => result = r);
            AssertSucceeded(result);
            Assert.AreEqual("main\n", Read("a.txt"));
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);
            AssertSucceeded(result);
            AssertNoOperationInProgress();
        }

        [UnityTest]
        public IEnumerator Test_Merge_ContinueRefusedWhileConflicted()
        {
            MakeConflictingBranches();
            yield return Await(GitUndo.MoveHeadAsync("merge", GitCommitCommands.Merge("feature", GitMergeMode.Default)), _ => { });
            string before = Head();

            GitResult result = default;
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);

            Assert.IsFalse(result.Success);
            StringAssert.Contains("a.txt", result.Message);
            Assert.AreEqual(before, Head());
            Assert.AreEqual(GitOperationKind.Merge, GitRepository.InProgressOperation);
        }

        [UnityTest]
        public IEnumerator Test_Merge_AbortRestoresEverything()
        {
            MakeConflictingBranches();
            Write("untracked.txt", "keep me\n");
            string before = Head();
            yield return Await(GitUndo.MoveHeadAsync("merge", GitCommitCommands.Merge("feature", GitMergeMode.Default)), _ => { });

            GitResult result = default;
            yield return Await(GitInProgress.AbortAsync(), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual(before, Head());
            Assert.AreEqual("main\n", Read("a.txt"));
            Assert.AreEqual("keep me\n", Read("untracked.txt"));
            Assert.AreEqual(0, UndoCount());
        }

        [UnityTest]
        public IEnumerator Test_Merge_DeleteModifyConflictResolvedByDeleting()
        {
            Commit("base", ("a.txt", "a\n"), ("gone.txt", "original\n"));
            Branch("feature");
            Git("rm", "-q", "gone.txt");
            Git("commit", "-q", "-m", "delete on main");
            Switch("feature");
            Commit("edit on feature", ("gone.txt", "edited\n"));
            Switch("main");
            yield return Await(GitUndo.MoveHeadAsync("merge", GitCommitCommands.Merge("feature", GitMergeMode.Default)), _ => { });
            Assume.That(GitRepository.InProgressOperation == GitOperationKind.Merge);

            // 우리 쪽(main)이 지웠다: ours 로 풀면 파일이 사라져야 한다.
            GitResult result = default;
            yield return Await(GitInProgress.ResolveAsync(Conflicted("gone.txt"), GitConflictSide.Ours), r => result = r);
            AssertSucceeded(result);
            Assert.IsFalse(File.Exists(Path.Combine(_root, "gone.txt")));
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);
            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.IsEmpty(Git("ls-tree", "--name-only", "HEAD", "gone.txt"));
        }

        [UnityTest]
        public IEnumerator Test_Merge_BinaryConflictResolvedByTheirs()
        {
            Commit("base", ("tex.png", Binary(0)));
            Branch("feature");
            Commit("main texture", ("tex.png", Binary(1)));
            Switch("feature");
            Commit("feature texture", ("tex.png", Binary(2)));
            Switch("main");
            yield return Await(GitUndo.MoveHeadAsync("merge", GitCommitCommands.Merge("feature", GitMergeMode.Default)), _ => { });
            Assume.That(GitRepository.InProgressOperation == GitOperationKind.Merge);

            GitResult result = default;
            yield return Await(GitInProgress.ResolveAsync(Conflicted("tex.png"), GitConflictSide.Theirs), r => result = r);
            AssertSucceeded(result);
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);
            AssertSucceeded(result);
            Assert.AreEqual(Binary(2), Read("tex.png"));
        }

        // ───────────────────────── cherry-pick·revert ─────────────────────────

        [UnityTest]
        public IEnumerator Test_CherryPick_ConflictResolveAndContinueKeepsAuthorAndMessage()
        {
            MakeConflictingBranches();
            Switch("feature");
            Git("commit", "-q", "--amend", "--no-edit", "--author=Other Person <other@example.com>");
            string picked = Head();
            Switch("main");
            GitResult result = default;
            yield return Await(GitUndo.MoveHeadAsync("cherry-pick", GitCommitCommands.CherryPick(picked, isMerge: false)), r => result = r);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(GitOperationKind.CherryPick, GitRepository.InProgressOperation);

            Write("a.txt", "both\n");
            Git("add", "a.txt");
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual("feature edit", Git("log", "-1", "--format=%s"), "원래 커밋 메시지를 쓴다");
            Assert.AreEqual("Other Person", Git("log", "-1", "--format=%an"), "원래 작성자를 지킨다");
            Assert.AreEqual("both\n", Read("a.txt"));
            Assert.AreEqual(1, UndoCount());
        }

        [UnityTest]
        public IEnumerator Test_CherryPick_CommitSelectedFilesFinishes()
        {
            MakeConflictingBranches();
            yield return Await(GitUndo.MoveHeadAsync("cherry-pick", GitCommitCommands.CherryPick(Rev("feature"), isMerge: false)), _ => { });
            Assume.That(GitRepository.InProgressOperation == GitOperationKind.CherryPick);

            Write("a.txt", "resolved\n");
            GitResult result = default;
            yield return Await(GitUndo.CommitAsync(Status().Where(e => e.RepoPath == "a.txt").ToList(), "picked"), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual("picked", Git("log", "-1", "--format=%s"));
            Assert.AreEqual(1, Git("log", "-1", "--format=%P").Split(' ').Length, "cherry-pick 결과는 보통 커밋이다");
        }

        [UnityTest]
        public IEnumerator Test_CherryPick_Abort()
        {
            MakeConflictingBranches();
            string before = Head();
            yield return Await(GitUndo.MoveHeadAsync("cherry-pick", GitCommitCommands.CherryPick(Rev("feature"), isMerge: false)), _ => { });

            GitResult result = default;
            yield return Await(GitInProgress.AbortAsync(), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual(before, Head());
            AssertClean();
        }

        [UnityTest]
        public IEnumerator Test_Revert_ConflictResolveAndContinue()
        {
            Commit("base", ("a.txt", "one\n"));
            Commit("change", ("a.txt", "two\n"));
            string reverted = Head();
            Commit("later", ("a.txt", "three\n"));

            GitResult result = default;
            yield return Await(GitUndo.MoveHeadAsync("revert", GitCommitCommands.Revert(reverted, isMerge: false)), r => result = r);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(GitOperationKind.Revert, GitRepository.InProgressOperation);

            Write("a.txt", "one and three\n");
            Git("add", "a.txt");
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            StringAssert.StartsWith("Revert \"change\"", Git("log", "-1", "--format=%s"));
            Assert.AreEqual("one and three\n", Read("a.txt"));
        }

        [UnityTest]
        public IEnumerator Test_Revert_Abort()
        {
            Commit("base", ("a.txt", "one\n"));
            Commit("change", ("a.txt", "two\n"));
            string reverted = Head();
            Commit("later", ("a.txt", "three\n"));
            string before = Head();
            yield return Await(GitUndo.MoveHeadAsync("revert", GitCommitCommands.Revert(reverted, isMerge: false)), _ => { });

            GitResult result = default;
            yield return Await(GitInProgress.AbortAsync(), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual(before, Head());
            Assert.AreEqual("three\n", Read("a.txt"));
        }

        // ───────────────────────── rebase ─────────────────────────

        [UnityTest]
        public IEnumerator Test_Rebase_StopOnConflictLeavesItInProgress()
        {
            MakeConflictingBranches();
            Switch("feature");

            GitResult result = default;
            yield return Await(GitUndo.RebaseAsync("rebase", "main", stopOnConflict: true), r => result = r);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(GitOperationKind.Rebase, GitRepository.InProgressOperation, "설정을 켰으면 취소하지 않고 멈춰 둔다");
            Assert.IsTrue(GitInProgress.CanSkip(GitOperationKind.Rebase));
            Assert.AreEqual(1, Status().Count(e => e.State == GitFileState.Conflicted));
        }

        [UnityTest]
        public IEnumerator Test_Rebase_ResolveAndContinueFinishesOnBranch()
        {
            MakeConflictingBranches();
            Switch("feature");
            Commit("feature after", ("f.txt", "f\n"));
            string before = Head();
            yield return Await(GitUndo.RebaseAsync("rebase", "main", stopOnConflict: true), _ => { });

            GitResult result = default;
            yield return Await(GitInProgress.ResolveAsync(Conflicted("a.txt"), GitConflictSide.Theirs), r => result = r);
            AssertSucceeded(result);
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual("feature", CurrentBranch(), "분리된 HEAD 가 아니라 브랜치로 끝나야 한다");
            Assert.AreEqual(Rev("main"), Rev("HEAD~2"));
            CollectionAssert.AreEqual(new[] { "feature after", "feature edit" }, Subjects("main..HEAD"), "원래 커밋 메시지를 지킨다");
            Assert.AreEqual("feature\n", Read("a.txt"));
            AssertClean();

            Assert.AreEqual(1, UndoCount());
            Assert.AreEqual(before, _journal.NextUndo.HeadBefore, "되돌리면 rebase 전 브랜치 끝으로 돌아가야 한다");
            yield return Await(GitUndo.UndoAsync(), r => result = r);
            AssertSucceeded(result);
            Assert.AreEqual(before, Head());
        }

        [UnityTest]
        public IEnumerator Test_Rebase_ContinueStopsAgainOnNextConflict()
        {
            Commit("base", ("a.txt", "a\n"), ("b.txt", "b\n"));
            Branch("feature");
            Commit("main edit", ("a.txt", "a main\n"), ("b.txt", "b main\n"));
            Switch("feature");
            Commit("f1", ("a.txt", "a feature\n"));
            Commit("f2", ("b.txt", "b feature\n"));
            yield return Await(GitUndo.RebaseAsync("rebase", "main", stopOnConflict: true), _ => { });
            Assume.That(GitRepository.InProgressOperation == GitOperationKind.Rebase);

            Write("a.txt", "a both\n");
            Git("add", "a.txt");
            GitResult result = default;
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);

            Assert.IsFalse(result.Success, "다음 커밋에서 또 충돌했다");
            Assert.AreEqual(GitOperationKind.Rebase, GitRepository.InProgressOperation);
            Assert.AreEqual("b.txt", Status().Single(e => e.State == GitFileState.Conflicted).RepoPath);
            Assert.AreEqual(0, UndoCount(), "끝나기 전에는 기록하지 않는다");

            Write("b.txt", "b both\n");
            Git("add", "b.txt");
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);
            AssertSucceeded(result);
            AssertNoOperationInProgress();
            CollectionAssert.AreEqual(new[] { "f2", "f1" }, Subjects("main..HEAD"));
            Assert.AreEqual(1, UndoCount());
        }

        [UnityTest]
        public IEnumerator Test_Rebase_SkipDropsTheConflictingCommit()
        {
            MakeConflictingBranches();
            Switch("feature");
            Commit("feature after", ("f.txt", "f\n"));
            yield return Await(GitUndo.RebaseAsync("rebase", "main", stopOnConflict: true), _ => { });

            GitResult result = default;
            yield return Await(GitInProgress.SkipAsync(), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual("feature", CurrentBranch());
            CollectionAssert.AreEqual(new[] { "feature after" }, Subjects("main..HEAD"));
            Assert.AreEqual("main\n", Read("a.txt"));
        }

        [UnityTest]
        public IEnumerator Test_Rebase_AbortRestoresBranchAndLocalChanges()
        {
            Commit("base", ("a.txt", "line\n"), ("local.txt", "original\n"));
            Branch("feature");
            Commit("main edit", ("a.txt", "main\n"));
            Switch("feature");
            Commit("feature edit", ("a.txt", "feature\n"));
            Write("local.txt", "uncommitted\n");
            string before = Head();
            yield return Await(GitUndo.RebaseAsync("rebase", "main", stopOnConflict: true), _ => { });
            Assume.That(GitRepository.InProgressOperation == GitOperationKind.Rebase);

            GitResult result = default;
            yield return Await(GitInProgress.AbortAsync(), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual(before, Head());
            Assert.AreEqual("feature", CurrentBranch());
            Assert.AreEqual("uncommitted\n", Read("local.txt"), "autostash 한 변경도 돌아와야 한다");
            Assert.IsEmpty(Git("stash", "list"));
        }

        [UnityTest]
        public IEnumerator Test_Rebase_ContinueAppliesAutostashWhenFinished()
        {
            Commit("base", ("a.txt", "line\n"), ("local.txt", "original\n"));
            Branch("feature");
            Commit("main edit", ("a.txt", "main\n"));
            Switch("feature");
            Commit("feature edit", ("a.txt", "feature\n"));
            Write("local.txt", "uncommitted\n");
            yield return Await(GitUndo.RebaseAsync("rebase", "main", stopOnConflict: true), _ => { });

            Write("a.txt", "both\n");
            Git("add", "a.txt");
            GitResult result = default;
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);

            AssertSucceeded(result);
            Assert.IsEmpty(result.Error, "autostash 를 문제없이 되돌렸으면 따로 알릴 것이 없다");
            AssertNoOperationInProgress();
            Assert.AreEqual("uncommitted\n", Read("local.txt"));
            Assert.IsEmpty(Git("stash", "list"));
        }

        [UnityTest]
        public IEnumerator Test_Rebase_UnstagedChangeBlocksContinue()
        {
            Commit("base", ("a.txt", "line\n"), ("b.txt", "b\n"));
            Branch("feature");
            Commit("main edit", ("a.txt", "main\n"));
            Switch("feature");
            Commit("feature edit", ("a.txt", "feature\n"));
            yield return Await(GitUndo.RebaseAsync("rebase", "main", stopOnConflict: true), _ => { });
            Write("a.txt", "both\n");
            Git("add", "a.txt");
            Write("b.txt", "dirty\n");

            Assert.IsNotNull(GitInProgress.ContinueBlocker(GitOperationKind.Rebase, Status()), "git 이 엉뚱한 말로 거부하기 전에 막아야 한다");
            Git("add", "b.txt");
            Assert.IsNull(GitInProgress.ContinueBlocker(GitOperationKind.Rebase, Status()));
        }

        [UnityTest]
        public IEnumerator Test_Rebase_StartedInTerminalCanBeContinued()
        {
            MakeConflictingBranches();
            Switch("feature");
            string before = Head();
            GitCommand.Run(_git, _root, new[] { "rebase", "main" });
            Assume.That(GitRepository.InProgressOperation == GitOperationKind.Rebase, "준비: 터미널 rebase 가 충돌로 멈춰 있어야 한다");

            Write("a.txt", "both\n");
            Git("add", "a.txt");
            GitResult result = default;
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual("feature", CurrentBranch());
            Assert.AreEqual(before, _journal.NextUndo.HeadBefore);
        }

        [UnityTest]
        public IEnumerator Test_Rebase_DefaultStillAbortsOnConflict()
        {
            MakeConflictingBranches();
            Switch("feature");
            string before = Head();

            GitResult result = default;
            yield return Await(GitUndo.RebaseAsync("rebase", "main", stopOnConflict: false), r => result = r);

            Assert.IsFalse(result.Success);
            AssertNoOperationInProgress();
            Assert.AreEqual(before, Head());
        }

        [UnityTest]
        public IEnumerator Test_Drop_StopOnConflictThenAbort()
        {
            Commit("c1", ("a.txt", "one\n"));
            Commit("c2", ("a.txt", "two\n"));
            Commit("c3", ("a.txt", "three\n"));
            string before = Head();

            GitResult result = default;
            yield return Await(GitRewrite.RunAsync(GitRewriteKind.Drop, Rev("HEAD~1"), null, "drop", stopOnConflict: true), r => result = r);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(GitOperationKind.Rebase, GitRepository.InProgressOperation);

            yield return Await(GitInProgress.AbortAsync(), r => result = r);
            AssertSucceeded(result);
            AssertNoOperationInProgress();
            Assert.AreEqual(before, Head());
            Assert.AreEqual("main", CurrentBranch());
        }

        [UnityTest]
        public IEnumerator Test_Drop_StopOnConflictThenContinue()
        {
            Commit("c1", ("a.txt", "one\n"));
            Commit("c2", ("a.txt", "two\n"));
            Commit("c3", ("a.txt", "three\n"));
            string before = Head();
            yield return Await(GitRewrite.RunAsync(GitRewriteKind.Drop, Rev("HEAD~1"), null, "drop", stopOnConflict: true), _ => { });

            Write("a.txt", "three\n");
            Git("add", "a.txt");
            GitResult result = default;
            yield return Await(GitInProgress.ContinueAsync(), r => result = r);

            AssertSucceeded(result);
            AssertNoOperationInProgress();
            CollectionAssert.AreEqual(new[] { "c3", "c1" }, Subjects("HEAD"));
            Assert.AreEqual(before, _journal.NextUndo.HeadBefore);
        }

        // ───────────────────────── 도우미 ─────────────────────────

        /// <summary>main 과 feature 가 a.txt 의 같은 줄을 다르게 고친 상태. main 에 있다.</summary>
        private void MakeConflictingBranches()
        {
            Commit("base", ("a.txt", "line\n"));
            Branch("feature");
            Commit("main edit", ("a.txt", "main\n"));
            Switch("feature");
            Commit("feature edit", ("a.txt", "feature\n"));
            Switch("main");
        }

        /// <summary>Git 창이 읽는 것과 같은 status 항목.</summary>
        private List<GitStatusEntry> Status()
        {
            GitResult result = GitCommand.Run(_git, _root, new[] { "status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all" });
            Assert.IsTrue(result.Success, result.Message);
            return GitStatusParser.Parse(result.Output, string.Empty, out _);
        }

        private GitStatusEntry Conflicted(string path)
        {
            GitStatusEntry entry = Status().FirstOrDefault(e => e.RepoPath == path && e.State == GitFileState.Conflicted);
            Assert.IsNotNull(entry, path + " 가 충돌 상태여야 한다");
            return entry;
        }

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

        /// <summary>이 테스트들은 한 번에 하나까지만 쌓는다 — 목록을 바꾸지 않고 NextUndo 만 본다.</summary>
        private int UndoCount()
        {
            return _journal.NextUndo != null ? 1 : 0;
        }

        private static void AssertSucceeded(GitResult result)
        {
            Assert.IsTrue(result.Success, "실패: " + result.Message);
        }

        private void AssertNoOperationInProgress()
        {
            Assert.AreEqual(GitOperationKind.None, GitRepository.InProgressOperation, "작업이 진행 중인 채로 남았다");
            // REBASE_HEAD 는 보지 않는다 — git 은 rebase --continue 로 끝난 뒤에도 그 파일을 지우지 않는다(진행 표시는 rebase-merge 폴더다).
            foreach (string marker in new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD" })
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
