using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 되돌릴 수 있는 git 작업 종류. Push·Fetch·되돌리기(Discard)·Pop 은 원래대로 돌릴 정보가 없어 여기 없다.
    /// </summary>
    public enum GitUndoKind
    {
        Commit,
        Switch,
        CreateBranch,
        DeleteBranch,
        Stash,
        Pull,

        /// <summary>
        /// 현재 브랜치(또는 분리된 HEAD)를 다른 커밋으로 옮긴 작업: 병합·체리픽·revert·reset.
        /// </summary>
        MoveHead,
    }

    /// <summary>
    /// 작업 하나의 전후 상태. 되돌리기·다시 하기는 이 값만으로 git 명령을 만든다(<see cref="GitUndoPlanner"/>).
    /// </summary>
    [Serializable]
    public sealed class GitUndoEntry
    {
        public GitUndoKind Kind;

        /// <summary>
        /// 버튼 툴팁에 보일 설명("커밋 'Fix: …'").
        /// </summary>
        public string Title;

        public string HeadBefore;
        public string HeadAfter;

        /// <summary>
        /// 작업 전 브랜치. 분리된 HEAD 였으면 빈 문자열.
        /// </summary>
        public string BranchBefore;
        public string BranchAfter;

        /// <summary>
        /// 만들거나 지운 브랜치 이름.
        /// </summary>
        public string Name;

        /// <summary>
        /// 만들거나 지운 브랜치의 끝 커밋, 또는 Stash 가 만든 stash 커밋.
        /// </summary>
        public string Hash;

        /// <summary>
        /// 브랜치를 만들면서 전환까지 했는지.
        /// </summary>
        public bool Switched;

        /// <summary>
        /// 일부 파일만 stash 했을 때 그 경로. 비어 있으면 전체 stash.
        /// </summary>
        public List<string> Paths = new();

        /// <summary>
        /// <see cref="GitUndoKind.MoveHead"/> 를 되돌리고 다시 할 때 쓰는 reset 방식. 비어 있으면 <c>--keep</c>
        /// (로컬 수정과 겹치면 git 이 거부한다). soft reset 은 stage 된 차이를 지키려고 <c>--soft</c> 로 기록한다.
        /// </summary>
        public string ResetMode;
    }

    public enum GitUndoCheckKind
    {
        HeadIs,
        CurrentBranchIs,
        BranchTipIs,
        BranchMissing,
        StashTopIs,
    }

    /// <summary>
    /// 명령을 돌리기 전에 저장소가 기록한 상태 그대로인지 확인할 조건. 다르면 그 사이에 다른 작업이 있었다는 뜻이라 멈춘다.
    /// </summary>
    public readonly struct GitUndoCheck
    {
        public readonly GitUndoCheckKind Kind;
        public readonly string Name;
        public readonly string Value;

        public GitUndoCheck(GitUndoCheckKind kind, string name, string value)
        {
            Kind = kind;
            Name = name;
            Value = value;
        }
    }

    /// <summary>
    /// 되돌리기 또는 다시 하기 한 번: 확인할 조건과 차례로 돌릴 git 인자 목록.
    /// </summary>
    public sealed class GitUndoPlan
    {
        public readonly List<GitUndoCheck> Checks = new();
        public readonly List<string[]> Commands = new();
    }

    /// <summary>
    /// 기록(<see cref="GitUndoEntry"/>)을 git 명령으로 바꾸는 순수 규칙. 저장소를 건드리지 않아 테스트로 확인한다.
    ///
    /// 원칙: 사용자의 작업 내용을 잃지 않는다. 커밋 되돌리기는 <c>reset --soft</c>(변경은 그대로 남는다),
    /// Pull 되돌리기는 <c>reset --keep</c>(로컬 수정과 겹치면 git 이 거부한다), 브랜치 삭제는 끝 커밋이 기록과 같을 때만 한다.
    /// </summary>
    public static class GitUndoPlanner
    {
        public static GitUndoPlan Undo(GitUndoEntry entry)
        {
            GitUndoPlan plan = new();
            switch (entry.Kind)
            {
                case GitUndoKind.Commit:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.HeadIs, null, entry.HeadAfter));
                    plan.Commands.Add(new[] { "reset", "--soft", entry.HeadBefore });
                    break;
                case GitUndoKind.Switch:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.CurrentBranchIs, null, entry.BranchAfter));
                    plan.Commands.Add(SwitchBack(entry));
                    break;
                case GitUndoKind.CreateBranch:
                    if (entry.Switched)
                    {
                        plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.CurrentBranchIs, null, entry.Name));
                        plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.HeadIs, null, entry.Hash));
                        plan.Commands.Add(SwitchBack(entry));
                    }
                    else
                    {
                        plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.BranchTipIs, entry.Name, entry.Hash));
                    }
                    plan.Commands.Add(new[] { "branch", "-D", entry.Name });
                    break;
                case GitUndoKind.DeleteBranch:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.BranchMissing, entry.Name, null));
                    plan.Commands.Add(new[] { "branch", entry.Name, entry.Hash });
                    break;
                case GitUndoKind.Stash:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.StashTopIs, null, entry.Hash));
                    plan.Commands.Add(new[] { "stash", "pop" });
                    break;
                case GitUndoKind.Pull:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.HeadIs, null, entry.HeadAfter));
                    plan.Commands.Add(new[] { "reset", "--keep", entry.HeadBefore });
                    break;
                case GitUndoKind.MoveHead:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.HeadIs, null, entry.HeadAfter));
                    plan.Commands.Add(new[] { "reset", ResetModeOf(entry), entry.HeadBefore });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, null);
            }
            return plan;
        }

        public static GitUndoPlan Redo(GitUndoEntry entry)
        {
            GitUndoPlan plan = new();
            switch (entry.Kind)
            {
                case GitUndoKind.Commit:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.HeadIs, null, entry.HeadBefore));
                    plan.Commands.Add(new[] { "reset", "--soft", entry.HeadAfter });
                    break;
                case GitUndoKind.Switch:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.HeadIs, null, entry.HeadBefore));
                    // 커밋으로 전환(분리된 HEAD)했던 기록은 브랜치 이름이 없다.
                    plan.Commands.Add(string.IsNullOrEmpty(entry.BranchAfter)
                        ? new[] { "switch", "--detach", entry.HeadAfter }
                        : new[] { "switch", entry.BranchAfter });
                    break;
                case GitUndoKind.CreateBranch:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.BranchMissing, entry.Name, null));
                    if (entry.Switched)
                    {
                        plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.HeadIs, null, entry.HeadBefore));
                        plan.Commands.Add(new[] { "switch", "-c", entry.Name, entry.Hash });
                    }
                    else
                    {
                        plan.Commands.Add(new[] { "branch", entry.Name, entry.Hash });
                    }
                    break;
                case GitUndoKind.DeleteBranch:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.BranchTipIs, entry.Name, entry.Hash));
                    plan.Commands.Add(new[] { "branch", "-D", entry.Name });
                    break;
                case GitUndoKind.Stash:
                    // 다시 넣으면 stash 커밋이 새로 생기므로, 실행한 쪽이 끝난 뒤 Hash 를 새 값으로 바꾼다.
                    // 일부만 넣었던 기록은 같은 경로만 다시 넣는다(전체를 넣으면 다른 작업까지 쓸려 들어간다).
                    List<string> stash = new() { "stash", "push", "--include-untracked" };
                    if (entry.Paths != null && entry.Paths.Count > 0)
                    {
                        stash.Add("--");
                        stash.AddRange(entry.Paths);
                    }
                    plan.Commands.Add(stash.ToArray());
                    break;
                case GitUndoKind.Pull:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.HeadIs, null, entry.HeadBefore));
                    plan.Commands.Add(new[] { "merge", "--ff-only", entry.HeadAfter });
                    break;
                case GitUndoKind.MoveHead:
                    plan.Checks.Add(new GitUndoCheck(GitUndoCheckKind.HeadIs, null, entry.HeadBefore));
                    plan.Commands.Add(new[] { "reset", ResetModeOf(entry), entry.HeadAfter });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry), entry.Kind, null);
            }
            return plan;
        }

        private static string ResetModeOf(GitUndoEntry entry)
        {
            return string.IsNullOrEmpty(entry.ResetMode) ? "--keep" : entry.ResetMode;
        }

        private static string[] SwitchBack(GitUndoEntry entry)
        {
            return string.IsNullOrEmpty(entry.BranchBefore)
                ? new[] { "switch", "--detach", entry.HeadBefore }
                : new[] { "switch", entry.BranchBefore };
        }
    }

    /// <summary>
    /// 되돌리기·다시 하기 목록. 새 작업을 기록하면 다시 하기 목록은 비운다(편집기의 실행 취소와 같다).
    /// 스크립트를 다시 컴파일해도 남도록 에디터 세션(SessionState)에 둔다 — 에디터를 끄면 사라진다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed class GitUndoJournal
    {
        /// <summary>
        /// 기억할 최대 작업 수. 오래된 기록일수록 확인 조건에 걸려 쓸 수 없게 되므로 길게 둘 이유가 없다.
        /// </summary>
        public const int MaxEntries = 30;

        private const string SessionKey = "GitVersionControl.UndoJournal";

        [Serializable]
        private sealed class Stacks
        {
            public List<GitUndoEntry> Undo = new();
            public List<GitUndoEntry> Redo = new();
        }

        private static GitUndoJournal s_shared;

        private readonly Func<string> _load;
        private readonly Action<string> _save;
        private Stacks _stacks;

        /// <summary>
        /// 목록이 바뀌었을 때.
        /// </summary>
        public event Action Changed;

        /// <param name="load">저장해 둔 JSON 을 읽는다. null 이면 저장하지 않는다(테스트).</param>
        public GitUndoJournal(Func<string> load = null, Action<string> save = null)
        {
            _load = load;
            _save = save;
            string json = _load?.Invoke();
            _stacks = string.IsNullOrEmpty(json) ? new Stacks() : JsonUtility.FromJson<Stacks>(json) ?? new Stacks();
        }

        /// <summary>
        /// 에디터 세션에 저장되는 공용 목록.
        /// </summary>
        public static GitUndoJournal Shared => s_shared ??= new GitUndoJournal(
            () => SessionState.GetString(SessionKey, string.Empty),
            json => SessionState.SetString(SessionKey, json));

        public GitUndoEntry NextUndo => _stacks.Undo.Count > 0 ? _stacks.Undo[^1] : null;
        public GitUndoEntry NextRedo => _stacks.Redo.Count > 0 ? _stacks.Redo[^1] : null;

        public void Record(GitUndoEntry entry)
        {
            _stacks.Undo.Add(entry);
            if (_stacks.Undo.Count > MaxEntries)
            {
                _stacks.Undo.RemoveAt(0);
            }
            _stacks.Redo.Clear();
            Persist();
        }

        /// <summary>
        /// 되돌리기에 성공했을 때: 그 작업을 다시 하기 목록으로 옮긴다.
        /// </summary>
        public void MarkUndone()
        {
            Move(_stacks.Undo, _stacks.Redo);
        }

        /// <summary>
        /// 다시 하기에 성공했을 때: 그 작업을 되돌리기 목록으로 옮긴다.
        /// </summary>
        public void MarkRedone()
        {
            Move(_stacks.Redo, _stacks.Undo);
        }

        /// <summary>
        /// 확인 조건에 걸려 더는 쓸 수 없는 기록을 버린다(같은 버튼이 계속 실패하지 않게).
        /// </summary>
        public void DropNextUndo()
        {
            Drop(_stacks.Undo);
        }

        public void DropNextRedo()
        {
            Drop(_stacks.Redo);
        }

        private void Move(List<GitUndoEntry> from, List<GitUndoEntry> to)
        {
            if (from.Count == 0)
            {
                return;
            }
            to.Add(from[^1]);
            from.RemoveAt(from.Count - 1);
            Persist();
        }

        private void Drop(List<GitUndoEntry> stack)
        {
            if (stack.Count == 0)
            {
                return;
            }
            stack.RemoveAt(stack.Count - 1);
            Persist();
        }

        private void Persist()
        {
            _save?.Invoke(JsonUtility.ToJson(_stacks));
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// 되돌릴 수 있는 작업을 실행하며 기록하고(<c>*Async</c>), 기록을 되돌리거나 다시 한다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitUndo
    {
        private static GitUndoJournal s_journalOverride;

        public static GitUndoJournal Journal => s_journalOverride ?? GitUndoJournal.Shared;

        /// <summary>테스트가 기록을 따로 모을 때(<see cref="GitRepository.UseRepositoryForTests"/>). null 이면 공용 목록으로 돌아간다.</summary>
        internal static void OverrideJournal(GitUndoJournal journal)
        {
            s_journalOverride = journal;
        }

        public static async Task<GitResult> CommitAsync(IReadOnlyCollection<GitStatusEntry> entries, string message)
        {
            string before = await HeadAsync();
            GitResult result = await GitOperations.CommitAsync(entries, message);
            // 첫 커밋은 되돌릴 부모가 없다(HEAD 를 지우는 것은 위험해 지원하지 않는다).
            if (result.Success && string.IsNullOrEmpty(before) == false)
            {
                Journal.Record(new GitUndoEntry
                {
                    Kind = GitUndoKind.Commit,
                    Title = GitText.Format("undo.commit", GitDisplay.FirstLine(message)),
                    HeadBefore = before,
                    HeadAfter = await HeadAsync(),
                });
            }
            return result;
        }

        /// <summary>
        /// 인덱스 커밋(Fork·GitKraken 방식)도 같은 "커밋" 기록이다. 고쳐 쓰기(amend)를 되돌리면 고치기 전 커밋으로 돌아가고,
        /// 고친 차이는 stage 된 채로 남는다.
        /// </summary>
        public static async Task<GitResult> CommitStagedAsync(string message, bool amend)
        {
            string before = await HeadAsync();
            GitResult result = await GitOperations.CommitStagedAsync(message, amend);
            if (result.Success && string.IsNullOrEmpty(before) == false)
            {
                string subject = string.IsNullOrWhiteSpace(message) ? before.Substring(0, 7) : GitDisplay.FirstLine(message);
                Journal.Record(new GitUndoEntry
                {
                    Kind = GitUndoKind.Commit,
                    Title = GitText.Format(amend ? "undo.amend" : "undo.commit", subject),
                    HeadBefore = before,
                    HeadAfter = await HeadAsync(),
                });
            }
            return result;
        }

        public static Task<GitResult> SwitchAsync(GitBranchRef branch)
        {
            return RecordSwitchAsync(() => GitOperations.SwitchAsync(branch));
        }

        /// <summary>
        /// 커밋으로 전환(분리된 HEAD).
        /// </summary>
        public static Task<GitResult> SwitchDetachedAsync(string commit)
        {
            return RecordSwitchAsync(() => GitRepository.RunAsync("switch", "--detach", commit));
        }

        private static async Task<GitResult> RecordSwitchAsync(Func<Task<GitResult>> operation)
        {
            string headBefore = await HeadAsync();
            string branchBefore = await CurrentBranchAsync();
            GitResult result = await operation();
            if (result.Success && string.IsNullOrEmpty(headBefore) == false)
            {
                string headAfter = await HeadAsync();
                string branchAfter = await CurrentBranchAsync();
                Journal.Record(new GitUndoEntry
                {
                    Kind = GitUndoKind.Switch,
                    Title = GitText.Format("undo.switch", NameOrShortHash(branchAfter, headAfter), NameOrShortHash(branchBefore, headBefore)),
                    HeadBefore = headBefore,
                    HeadAfter = headAfter,
                    BranchBefore = branchBefore,
                    BranchAfter = branchAfter,
                });
            }
            return result;
        }

        /// <param name="startPoint">새 브랜치가 가리킬 커밋(전체 해시). null 이면 HEAD.</param>
        public static async Task<GitResult> CreateBranchAsync(string name, bool switchTo, string startPoint = null)
        {
            string headBefore = await HeadAsync();
            string branchBefore = await CurrentBranchAsync();
            GitResult result = await GitOperations.CreateBranchAsync(name, switchTo, startPoint);
            if (result.Success && string.IsNullOrEmpty(headBefore) == false)
            {
                Journal.Record(new GitUndoEntry
                {
                    Kind = GitUndoKind.CreateBranch,
                    Title = GitText.Format("undo.createBranch", name),
                    HeadBefore = headBefore,
                    BranchBefore = branchBefore,
                    Name = name,
                    Hash = startPoint ?? headBefore,
                    Switched = switchTo,
                });
            }
            return result;
        }

        /// <summary>
        /// HEAD 를 옮기는 작업(병합·체리픽·revert·reset)을 실행하고, HEAD 가 실제로 바뀌었으면 기록한다.
        /// 충돌로 멈추면 git 이 실패를 돌려주고 HEAD 는 그대로라 기록하지 않는다 — 진행 중인 채로 남고, Git 창 배너에서 풀어 이어가거나 취소한다.
        /// 그 기록은 이어가기가 끝날 때 남긴다(<see cref="GitInProgress.ContinueAsync"/>).
        /// </summary>
        /// <param name="resetMode">되돌리기에 쓸 reset 방식. null 이면 <c>--keep</c>.</param>
        public static async Task<GitResult> MoveHeadAsync(string title, string[] args, string resetMode = null)
        {
            string before = await HeadAsync();
            GitOperationKind kindBefore = GitRepository.InProgressOperation;
            GitResult result = await GitRepository.RunAsync(args);
            GitOperationKind kindAfter = GitRepository.InProgressOperation;
            if (result.Success == false && kindBefore == GitOperationKind.None && kindAfter != GitOperationKind.None)
            {
                return new GitResult(result.ExitCode, result.Output, GitText.Format("inprogress.stoppedOnConflict", GitInProgress.DisplayName(kindAfter), result.Message));
            }
            string after = await HeadAsync();
            if (result.Success && string.IsNullOrEmpty(before) == false && after != before)
            {
                Journal.Record(new GitUndoEntry
                {
                    Kind = GitUndoKind.MoveHead,
                    Title = title,
                    HeadBefore = before,
                    HeadAfter = after,
                    ResetMode = resetMode,
                });
            }
            return result;
        }

        /// <summary>
        /// 현재 브랜치를 <paramref name="revision"/> 위로 다시 쌓는다.
        ///
        /// 충돌로 멈추면 기본(<see cref="GitVcsSettings.StopOnRebaseConflict"/> 끔)은 바로 취소해 시작 전 상태(브랜치·커밋·로컬 변경)로 돌려놓는다 —
        /// 반쯤 옮겨진 분리된 HEAD 로 두는 것을 원하지 않는 사람이 많다. 설정을 켜면 멈춘 채 두고, Git 창 배너에서 풀어 이어가거나 취소한다(<see cref="GitInProgress"/>).
        /// 이미 진행 중인 rebase(터미널에서 시작한 것)가 있으면 손대지 않고 거부한다.
        /// 성공하면 <see cref="GitResult.Error"/> 는 비어 있고, 사용자가 꼭 알아야 할 일(<see cref="AutostashConflicted"/>)이 있을 때만 그 안내문을 담는다.
        /// </summary>
        /// <param name="stopOnConflict">충돌하면 멈춰 둘지. null 이면 설정을 따른다.</param>
        public static async Task<GitResult> RebaseAsync(string title, string revision, bool? stopOnConflict = null)
        {
            if (GitRepository.IsRebaseInProgress)
            {
                return new GitResult(1, string.Empty, GitText.Get("rebase.alreadyInProgress"));
            }
            string before = await HeadAsync();
            string stashBefore = await StashTopAsync();
            GitResult result = await GitCommand.RunAsync(GitRepository.RepoRoot, GitCommitCommands.Rebase(revision), NoEditor);
            if (result.Success == false)
            {
                if (GitRepository.IsRebaseInProgress)
                {
                    if (stopOnConflict ?? GitVcsSettings.instance.StopOnRebaseConflict)
                    {
                        return new GitResult(result.ExitCode, result.Output, GitText.Format("inprogress.stoppedOnConflict", GitInProgress.DisplayName(GitOperationKind.Rebase), result.Message));
                    }
                    // --abort 는 autostash 한 변경도 되돌린다.
                    await GitRepository.RunAsync("rebase", "--abort");
                    return new GitResult(result.ExitCode, result.Output, GitText.Format("rebase.conflictAborted", result.Message));
                }
                return result;
            }
            string after = await HeadAsync();
            if (string.IsNullOrEmpty(before) == false && after != before)
            {
                Journal.Record(new GitUndoEntry
                {
                    Kind = GitUndoKind.MoveHead,
                    Title = title,
                    HeadBefore = before,
                    HeadAfter = after,
                });
            }
            return await AutostashConflicted(stashBefore)
                ? new GitResult(0, result.Output, GitText.Get("rebase.autostashConflict"))
                : new GitResult(0, result.Output, string.Empty);
        }

        /// <summary>
        /// <c>--autostash</c> 로 끝난 rebase 가 커밋하지 않은 변경을 되돌리다 충돌했는지. 그때 git 은 성공(0)으로 끝내지만
        /// 파일에 충돌 표시를 남기고 원래 변경을 stash 에 둔다 — 새 stash 가 생긴 것으로 안다(git 의 안내문은 번역될 수 있어 읽지 않는다).
        /// </summary>
        internal static async Task<bool> AutostashConflicted(string stashBefore)
        {
            string stashAfter = await StashTopAsync();
            return string.IsNullOrEmpty(stashAfter) == false && stashAfter != stashBefore;
        }

        /// <summary>
        /// rebase 가 메시지 편집기를 띄우지 않게 한다. 백그라운드 프로세스라 편집기가 뜨면 아무도 닫지 못해 시간 초과까지 멈춘다.
        /// </summary>
        internal static readonly IReadOnlyDictionary<string, string> NoEditor = new Dictionary<string, string> { ["GIT_EDITOR"] = "true", ["GIT_SEQUENCE_EDITOR"] = "true" };

        private static string NameOrShortHash(string branch, string head)
        {
            return string.IsNullOrEmpty(branch) ? head.Substring(0, Math.Min(7, head.Length)) : branch;
        }

        public static async Task<GitResult> DeleteBranchAsync(string name, bool force)
        {
            string tip = await BranchTipAsync(name);
            GitResult result = await GitOperations.DeleteBranchAsync(name, force);
            if (result.Success && string.IsNullOrEmpty(tip) == false)
            {
                Journal.Record(new GitUndoEntry
                {
                    Kind = GitUndoKind.DeleteBranch,
                    Title = GitText.Format("undo.deleteBranch", name),
                    Name = name,
                    Hash = tip,
                });
            }
            return result;
        }

        public static async Task<GitResult> StashAsync()
        {
            string before = await StashTopAsync();
            GitResult result = await GitOperations.StashAsync();
            string after = await StashTopAsync();
            // 바꾼 것이 없으면 git 은 성공으로 끝내지만 stash 를 만들지 않는다.
            if (result.Success && string.IsNullOrEmpty(after) == false && after != before)
            {
                Journal.Record(new GitUndoEntry
                {
                    Kind = GitUndoKind.Stash,
                    Title = GitText.Get("undo.stash"),
                    Hash = after,
                });
            }
            return result;
        }

        /// <summary>
        /// 고른 파일·폴더만 stash. 전체 Stash 와 같은 기록(되돌리면 그 stash 를 pop)이다.
        /// </summary>
        public static async Task<GitResult> StashPathsAsync(IReadOnlyCollection<string> repoPaths, string message)
        {
            string before = await StashTopAsync();
            GitResult result = await GitFileOperations.StashPathsAsync(repoPaths, message);
            string after = await StashTopAsync();
            if (result.Success && string.IsNullOrEmpty(after) == false && after != before)
            {
                Journal.Record(new GitUndoEntry
                {
                    Kind = GitUndoKind.Stash,
                    Title = message,
                    Hash = after,
                    Paths = new List<string>(repoPaths),
                });
            }
            return result;
        }

        public static async Task<GitResult> PullAsync(GitPullMode mode)
        {
            string before = await HeadAsync();
            GitResult result = await GitOperations.PullAsync(mode);
            string after = await HeadAsync();
            if (result.Success && string.IsNullOrEmpty(before) == false && after != before)
            {
                Journal.Record(new GitUndoEntry
                {
                    Kind = GitUndoKind.Pull,
                    Title = GitText.Format("undo.pull", before.Substring(0, 7), after.Substring(0, 7)),
                    HeadBefore = before,
                    HeadAfter = after,
                });
            }
            return result;
        }

        /// <summary>
        /// 가장 최근 작업을 되돌린다. 저장소가 기록과 달라졌으면 아무것도 하지 않고 그 기록을 버린다.
        /// </summary>
        public static async Task<GitResult> UndoAsync()
        {
            GitUndoEntry entry = Journal.NextUndo;
            if (entry == null)
            {
                return new GitResult(-1, string.Empty, GitText.Get("undo.nothing"));
            }
            GitResult result = await RunPlanAsync(GitUndoPlanner.Undo(entry));
            if (result.ExitCode == StaleExitCode)
            {
                Journal.DropNextUndo();
            }
            else if (result.Success)
            {
                Journal.MarkUndone();
            }
            return result;
        }

        public static async Task<GitResult> RedoAsync()
        {
            GitUndoEntry entry = Journal.NextRedo;
            if (entry == null)
            {
                return new GitResult(-1, string.Empty, GitText.Get("undo.nothing"));
            }
            GitResult result = await RunPlanAsync(GitUndoPlanner.Redo(entry));
            if (result.ExitCode == StaleExitCode)
            {
                Journal.DropNextRedo();
            }
            else if (result.Success)
            {
                if (entry.Kind == GitUndoKind.Stash)
                {
                    entry.Hash = await StashTopAsync();
                }
                Journal.MarkRedone();
            }
            return result;
        }

        /// <summary>
        /// 확인 조건에 걸렸다는 표시. git 종료 코드(0~255)와 겹치지 않는 값.
        /// </summary>
        private const int StaleExitCode = -2;

        private static async Task<GitResult> RunPlanAsync(GitUndoPlan plan)
        {
            foreach (GitUndoCheck check in plan.Checks)
            {
                if (await IsSatisfiedAsync(check) == false)
                {
                    return new GitResult(StaleExitCode, string.Empty, GitText.Get("undo.stale"));
                }
            }
            GitResult result = new(0, string.Empty, string.Empty);
            foreach (string[] args in plan.Commands)
            {
                result = await GitRepository.RunAsync(args);
                if (result.Success == false)
                {
                    return result;
                }
            }
            return result;
        }

        private static async Task<bool> IsSatisfiedAsync(GitUndoCheck check)
        {
            switch (check.Kind)
            {
                case GitUndoCheckKind.HeadIs:
                    return await HeadAsync() == check.Value;
                case GitUndoCheckKind.CurrentBranchIs:
                    return await CurrentBranchAsync() == (check.Value ?? string.Empty);
                case GitUndoCheckKind.BranchTipIs:
                    return await BranchTipAsync(check.Name) == check.Value;
                case GitUndoCheckKind.BranchMissing:
                    return string.IsNullOrEmpty(await BranchTipAsync(check.Name));
                case GitUndoCheckKind.StashTopIs:
                    return await StashTopAsync() == check.Value;
                default:
                    throw new ArgumentOutOfRangeException(nameof(check), check.Kind, null);
            }
        }

        private static Task<string> HeadAsync() => RevParseAsync("HEAD");

        internal static Task<string> StashTopAsync() => RevParseAsync("refs/stash");

        private static Task<string> BranchTipAsync(string name) => RevParseAsync("refs/heads/" + name);

        /// <summary>
        /// 리비전의 전체 해시. 없으면 빈 문자열.
        /// </summary>
        private static async Task<string> RevParseAsync(string revision)
        {
            GitResult result = await GitRepository.RunAsync("rev-parse", "--verify", "--quiet", revision);
            return result.Success ? result.Output.Trim() : string.Empty;
        }

        /// <summary>
        /// 현재 브랜치 이름. 분리된 HEAD 면 빈 문자열.
        /// </summary>
        private static async Task<string> CurrentBranchAsync()
        {
            GitResult result = await GitRepository.RunAsync("symbolic-ref", "--short", "--quiet", "HEAD");
            return result.Success ? result.Output.Trim() : string.Empty;
        }
    }
}
