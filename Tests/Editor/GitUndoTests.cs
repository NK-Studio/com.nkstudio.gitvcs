using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 실행 취소·다시 실행 기록이 어떤 확인 조건과 git 명령으로 바뀌는지, 목록이 어떻게 움직이는지 확인한다.
    /// </summary>
    public sealed class GitUndoTests
    {
        private static string Joined(GitUndoPlan plan)
        {
            return string.Join(" ; ", plan.Commands.Select(args => string.Join(" ", args)));
        }

        [Test]
        public void Test_Undo_Commit_SoftResetsToParentWhenHeadUnchanged()
        {
            GitUndoEntry entry = new() { Kind = GitUndoKind.Commit, HeadBefore = "p", HeadAfter = "c" };

            GitUndoPlan plan = GitUndoPlanner.Undo(entry);

            Assert.AreEqual(GitUndoCheckKind.HeadIs, plan.Checks.Single().Kind);
            Assert.AreEqual("c", plan.Checks.Single().Value);
            Assert.AreEqual("reset --soft p", Joined(plan));
        }

        [Test]
        public void Test_Redo_Commit_SoftResetsBackToCommit()
        {
            GitUndoEntry entry = new() { Kind = GitUndoKind.Commit, HeadBefore = "p", HeadAfter = "c" };

            GitUndoPlan plan = GitUndoPlanner.Redo(entry);

            Assert.AreEqual("p", plan.Checks.Single().Value);
            Assert.AreEqual("reset --soft c", Joined(plan));
        }

        [Test]
        public void Test_Undo_Switch_FromDetachedHead_DetachesAtOldCommit()
        {
            GitUndoEntry entry = new() { Kind = GitUndoKind.Switch, HeadBefore = "h", BranchBefore = string.Empty, BranchAfter = "main" };

            Assert.AreEqual("switch --detach h", Joined(GitUndoPlanner.Undo(entry)));
        }

        [Test]
        public void Test_Undo_Switch_SwitchesToPreviousBranch()
        {
            GitUndoEntry entry = new() { Kind = GitUndoKind.Switch, HeadBefore = "h", BranchBefore = "dev", BranchAfter = "main" };

            GitUndoPlan plan = GitUndoPlanner.Undo(entry);

            Assert.AreEqual(GitUndoCheckKind.CurrentBranchIs, plan.Checks.Single().Kind);
            Assert.AreEqual("main", plan.Checks.Single().Value);
            Assert.AreEqual("switch dev", Joined(plan));
        }

        [Test]
        public void Test_Undo_CreateBranchWithSwitch_SwitchesBackThenDeletes()
        {
            GitUndoEntry entry = new() { Kind = GitUndoKind.CreateBranch, Name = "feat", Hash = "h", HeadBefore = "h", BranchBefore = "main", Switched = true };

            GitUndoPlan plan = GitUndoPlanner.Undo(entry);

            Assert.AreEqual("switch main ; branch -D feat", Joined(plan));
            Assert.IsTrue(plan.Checks.Any(check => check.Kind == GitUndoCheckKind.HeadIs && check.Value == "h"));
        }

        [Test]
        public void Test_Undo_CreateBranch_DeletesOnlyWhenTipUnchanged()
        {
            GitUndoEntry entry = new() { Kind = GitUndoKind.CreateBranch, Name = "feat", Hash = "h" };

            GitUndoPlan plan = GitUndoPlanner.Undo(entry);

            GitUndoCheck check = plan.Checks.Single();
            Assert.AreEqual(GitUndoCheckKind.BranchTipIs, check.Kind);
            Assert.AreEqual("feat", check.Name);
            Assert.AreEqual("h", check.Value);
            Assert.AreEqual("branch -D feat", Joined(plan));
        }

        [Test]
        public void Test_Undo_DeleteBranch_RecreatesAtRecordedTip()
        {
            GitUndoEntry entry = new() { Kind = GitUndoKind.DeleteBranch, Name = "old", Hash = "t" };

            GitUndoPlan plan = GitUndoPlanner.Undo(entry);

            Assert.AreEqual(GitUndoCheckKind.BranchMissing, plan.Checks.Single().Kind);
            Assert.AreEqual("branch old t", Joined(plan));
        }

        [Test]
        public void Test_Undo_Pull_UsesResetKeepToProtectLocalChanges()
        {
            GitUndoEntry entry = new() { Kind = GitUndoKind.Pull, HeadBefore = "a", HeadAfter = "b" };

            Assert.AreEqual("reset --keep a", Joined(GitUndoPlanner.Undo(entry)));
            Assert.AreEqual("merge --ff-only b", Joined(GitUndoPlanner.Redo(entry)));
        }

        [Test]
        public void Test_Undo_Stash_PopsOnlyTheRecordedStash()
        {
            GitUndoEntry entry = new() { Kind = GitUndoKind.Stash, Hash = "s" };

            GitUndoPlan plan = GitUndoPlanner.Undo(entry);

            Assert.AreEqual(GitUndoCheckKind.StashTopIs, plan.Checks.Single().Kind);
            Assert.AreEqual("s", plan.Checks.Single().Value);
            Assert.AreEqual("stash pop", Joined(plan));
        }

        [Test]
        public void Test_Journal_RecordAfterUndo_ClearsRedo()
        {
            GitUndoJournal journal = new();
            journal.Record(new GitUndoEntry { Title = "a" });
            journal.MarkUndone();
            Assert.AreEqual("a", journal.NextRedo.Title);

            journal.Record(new GitUndoEntry { Title = "b" });

            Assert.IsNull(journal.NextRedo);
            Assert.AreEqual("b", journal.NextUndo.Title);
        }

        [Test]
        public void Test_Journal_UndoThenRedo_MovesEntryBack()
        {
            GitUndoJournal journal = new();
            journal.Record(new GitUndoEntry { Title = "a" });

            journal.MarkUndone();
            journal.MarkRedone();

            Assert.AreEqual("a", journal.NextUndo.Title);
            Assert.IsNull(journal.NextRedo);
        }

        [Test]
        public void Test_Journal_KeepsAtMostMaxEntries()
        {
            GitUndoJournal journal = new();
            for (int i = 0; i < GitUndoJournal.MaxEntries + 5; i++)
            {
                journal.Record(new GitUndoEntry { Title = i.ToString() });
            }

            int count = 0;
            while (journal.NextUndo != null)
            {
                journal.DropNextUndo();
                count++;
            }
            Assert.AreEqual(GitUndoJournal.MaxEntries, count);
        }

        [Test]
        public void Test_Journal_SurvivesSaveAndLoad()
        {
            string saved = null;
            GitUndoJournal first = new(() => saved, json => saved = json);
            first.Record(new GitUndoEntry { Kind = GitUndoKind.Commit, Title = "a", HeadBefore = "p", HeadAfter = "c" });

            GitUndoJournal second = new(() => saved, json => saved = json);

            Assert.AreEqual("a", second.NextUndo.Title);
            Assert.AreEqual("c", second.NextUndo.HeadAfter);
        }
    }
}
