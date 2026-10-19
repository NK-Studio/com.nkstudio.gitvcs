using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>저장소가 멈춰 있는 여러 단계 작업(충돌 등). 터미널에서 시작한 것도 같다.</summary>
    public enum GitOperationKind
    {
        None,
        Rebase,
        Merge,
        CherryPick,
        Revert,
    }

    /// <summary>충돌 파일 하나를 어느 쪽으로 해결할지(<see cref="GitInProgress.ResolveAsync"/>).</summary>
    public enum GitConflictSide
    {
        /// <summary>파일을 지금 내용 그대로 해결됨으로 표시한다(사용자가 직접 고친 뒤).</summary>
        AsIs,

        /// <summary>HEAD 쪽(<c>--ours</c>). rebase 에서는 다시 쌓는 바탕(upstream) 쪽이다.</summary>
        Ours,

        /// <summary>들어오는 쪽(<c>--theirs</c>). rebase 에서는 다시 쌓는 내 커밋 쪽이다.</summary>
        Theirs,
    }

    /// <summary>
    /// 충돌로 멈춘 rebase·merge·cherry-pick·revert 를 알아보고, 창 안에서 충돌을 풀고 이어가거나(--continue) 건너뛰거나(--skip) 취소(--abort)한다.
    ///
    /// 진행 상태는 git 폴더의 표시 파일로 안다: rebase-merge·rebase-apply 폴더, MERGE_HEAD·CHERRY_PICK_HEAD·REVERT_HEAD,
    /// 여러 커밋을 차례로 적용하는 cherry-pick·revert 의 sequencer 폴더. 파일만 보므로 매 화면 그리기에서 불러도 된다.
    /// 이어가기·건너뛰기는 편집기를 띄우지 않는다(<see cref="GitUndo.NoEditor"/>) — 커밋 메시지는 git 이 준비한 것(원래 커밋·MERGE_MSG)을 쓴다.
    /// </summary>
    public static class GitInProgress
    {
        /// <summary>충돌 표시를 찾을 파일 크기 상한. 넘는 파일(대개 바이너리 에셋)은 검사하지 않는다.</summary>
        private const long MarkerScanLimitBytes = 8 * 1024 * 1024;

        /// <summary>
        /// <paramref name="gitDir"/> 에서 진행 중인 작업. rebase 가 가장 먼저다 — 병합 커밋을 다시 쌓다 멈추면 MERGE_HEAD 도 함께 있다.
        /// <c>git am</c>(rebase-apply/applying)은 이 창이 다루지 않으므로 None 이다.
        /// </summary>
        public static GitOperationKind Detect(string gitDir)
        {
            if (string.IsNullOrEmpty(gitDir) || Directory.Exists(gitDir) == false)
            {
                return GitOperationKind.None;
            }
            if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")))
            {
                return GitOperationKind.Rebase;
            }
            string apply = Path.Combine(gitDir, "rebase-apply");
            if (Directory.Exists(apply))
            {
                return File.Exists(Path.Combine(apply, "applying")) ? GitOperationKind.None : GitOperationKind.Rebase;
            }
            if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD")))
            {
                return GitOperationKind.Merge;
            }
            if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")))
            {
                return GitOperationKind.CherryPick;
            }
            if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD")))
            {
                return GitOperationKind.Revert;
            }
            // 여러 커밋 cherry-pick·revert 에서 충돌한 커밋을 직접 커밋한 뒤: *_HEAD 는 없고 남은 목록(sequencer)만 있다.
            string todo = Path.Combine(gitDir, "sequencer", "todo");
            if (File.Exists(todo))
            {
                string first = ReadFirstLine(todo);
                return first.StartsWith("revert", StringComparison.Ordinal) ? GitOperationKind.Revert : GitOperationKind.CherryPick;
            }
            return GitOperationKind.None;
        }

        /// <summary>건너뛰기(--skip)는 rebase 에만 둔다. 커밋 하나짜리 cherry-pick·revert 를 건너뛰는 것은 취소와 다를 바 없어 헷갈린다.</summary>
        public static bool CanSkip(GitOperationKind kind)
        {
            return kind == GitOperationKind.Rebase;
        }

        /// <summary>
        /// 병합·cherry-pick·revert 가 멈춘 동안은 일부 파일만 커밋할 수 없다(git: "cannot do a partial commit during a merge").
        /// 그때 커밋은 인덱스 전체로 해야 하고, 그 커밋이 작업을 끝낸다. rebase 중 커밋은 git 이 허락한다(나눠 커밋하기 등).
        /// </summary>
        public static bool RequiresFullCommit(GitOperationKind kind)
        {
            return kind == GitOperationKind.Merge || kind == GitOperationKind.CherryPick || kind == GitOperationKind.Revert;
        }

        public static string[] ContinueCommand(GitOperationKind kind)
        {
            return new[] { Verb(kind), "--continue" };
        }

        public static string[] AbortCommand(GitOperationKind kind)
        {
            return new[] { Verb(kind), "--abort" };
        }

        public static string[] SkipCommand(GitOperationKind kind)
        {
            return new[] { Verb(kind), "--skip" };
        }

        private static string Verb(GitOperationKind kind)
        {
            switch (kind)
            {
                case GitOperationKind.Rebase:
                    return "rebase";
                case GitOperationKind.Merge:
                    return "merge";
                case GitOperationKind.CherryPick:
                    return "cherry-pick";
                case GitOperationKind.Revert:
                    return "revert";
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>배너·메시지에 쓰는 작업 이름("rebase", "병합"…).</summary>
        public static string DisplayName(GitOperationKind kind)
        {
            return GitText.Get("inprogress.kind." + kind);
        }

        /// <summary>
        /// 이어가기를 막는 까닭. 막을 것이 없으면 null.
        /// <list type="bullet">
        /// <item>충돌이 남은 파일이 있으면 아무 작업도 이어갈 수 없다.</item>
        /// <item>rebase 는 stage 하지 않은 tracked 파일 변경이 있으면 git 이 "충돌을 먼저 풀라" 는 엉뚱한 말로 거부한다 — 미리 제대로 알린다.</item>
        /// </list>
        /// </summary>
        public static string ContinueBlocker(GitOperationKind kind, IReadOnlyList<GitStatusEntry> entries)
        {
            if (kind == GitOperationKind.None)
            {
                return GitText.Get("inprogress.nothing");
            }
            int conflicts = entries.Count(e => e.State == GitFileState.Conflicted);
            if (conflicts > 0)
            {
                return GitText.Format("inprogress.blockedConflicts", conflicts);
            }
            if (kind == GitOperationKind.Rebase && entries.Any(HasUnstagedTrackedChange))
            {
                return GitText.Get("inprogress.blockedUnstaged");
            }
            return null;
        }

        private static bool HasUnstagedTrackedChange(GitStatusEntry entry)
        {
            return entry.State != GitFileState.Untracked && entry.WorkTreeCode != ' ' && entry.WorkTreeCode != '?' && entry.WorkTreeCode != '!';
        }

        /// <summary>
        /// 텍스트 파일에 해결하지 않은 충돌 표시(<c>&lt;&lt;&lt;&lt;&lt;&lt;&lt; </c> 로 시작하는 줄과 짝이 되는 <c>&gt;&gt;&gt;&gt;&gt;&gt;&gt; </c> 줄)가 남았는지.
        /// 없는 파일·큰 파일·바이너리(NUL 이 있는 파일)는 false — git 도 바이너리에는 표시를 넣지 않는다.
        /// </summary>
        public static bool HasConflictMarkers(string fullPath)
        {
            try
            {
                FileInfo info = new(fullPath);
                if (info.Exists == false || info.Length > MarkerScanLimitBytes)
                {
                    return false;
                }
                string text = File.ReadAllText(fullPath);
                if (text.IndexOf('\0') >= 0)
                {
                    return false;
                }
                bool open = false;
                foreach (string line in text.Split('\n'))
                {
                    if (line.StartsWith("<<<<<<<", StringComparison.Ordinal))
                    {
                        open = true;
                    }
                    else if (open && line.StartsWith(">>>>>>>", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                return false;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
        }

        /// <summary>고른 충돌 파일 중 충돌 표시가 남은 것(저장소 경로).</summary>
        public static List<string> WithConflictMarkers(IEnumerable<GitStatusEntry> entries, string repoRoot)
        {
            return entries.Where(e => e.State == GitFileState.Conflicted && HasConflictMarkers(Path.Combine(repoRoot, e.RepoPath)))
                .Select(e => e.RepoPath)
                .ToList();
        }

        /// <summary>인덱스에 아직 충돌(unmerged)로 남은 경로.</summary>
        public static async Task<List<string>> UnmergedPathsAsync()
        {
            GitResult result = await GitRepository.RunAsync("diff", "--name-only", "--diff-filter=U", "-z");
            if (result.Success == false)
            {
                return new List<string>();
            }
            return result.Output.Split(new[] { '\0' }, StringSplitOptions.RemoveEmptyEntries).Distinct().ToList();
        }

        /// <summary>목록 안내문용: 앞의 몇 개만 보이고 나머지는 개수로.</summary>
        public static string ListPaths(IReadOnlyList<string> paths)
        {
            const int Shown = 10;
            string list = string.Join("\n", paths.Take(Shown));
            return paths.Count > Shown ? list + "\n" + GitText.Format("inprogress.morePaths", paths.Count - Shown) : list;
        }

        /// <summary>
        /// 충돌 파일 하나를 해결됨으로 표시한다. <see cref="GitConflictSide.Ours"/>·<see cref="GitConflictSide.Theirs"/> 는 그쪽 내용으로 파일을 바꾼 뒤 표시한다 —
        /// 그쪽에서 지운 파일이면 지운다(삭제/수정 충돌). 바이너리 에셋처럼 손으로 고칠 수 없는 파일을 창 안에서 푸는 길이다.
        /// </summary>
        public static async Task<GitResult> ResolveAsync(GitStatusEntry entry, GitConflictSide side)
        {
            string path = entry.RepoPath;
            if (side != GitConflictSide.AsIs)
            {
                // 짧은 status 의 두 글자 중 앞(X)은 우리 쪽, 뒤(Y)는 상대 쪽: DU = 우리가 지움, UD = 상대가 지움.
                char code = side == GitConflictSide.Ours ? entry.IndexCode : entry.WorkTreeCode;
                if (code == 'D')
                {
                    return await GitRepository.RunAsync("rm", "--quiet", "--ignore-unmatch", "--", path);
                }
                GitResult checkout = await GitRepository.RunAsync("checkout", side == GitConflictSide.Ours ? "--ours" : "--theirs", "--", path);
                if (checkout.Success == false)
                {
                    return checkout;
                }
            }
            // -A: 사용자가 파일을 지워서 푼 경우도 해결로 올린다.
            return await GitRepository.RunAsync("add", "-A", "--", path);
        }

        /// <summary>
        /// 진행 중인 작업을 이어간다. 끝까지 가면 실행 취소 목록에 한 번 남기고(시작 전 커밋으로 돌아가는 기록), 다음 커밋에서 또 충돌하면 그렇다고 알린다.
        /// </summary>
        public static async Task<GitResult> ContinueAsync()
        {
            return await AdvanceAsync(ContinueCommand, requireReady: true);
        }

        /// <summary>rebase 에서 지금 멈춘 커밋을 빼고 다음으로 간다.</summary>
        public static async Task<GitResult> SkipAsync()
        {
            GitOperationKind kind = GitRepository.InProgressOperation;
            if (CanSkip(kind) == false)
            {
                return new GitResult(1, string.Empty, GitText.Get("inprogress.cannotSkip"));
            }
            return await AdvanceAsync(SkipCommand, requireReady: false);
        }

        /// <summary>작업을 취소해 시작 전 상태로 돌린다(rebase 의 autostash 한 변경도 돌아온다).</summary>
        public static async Task<GitResult> AbortAsync()
        {
            GitOperationKind kind = GitRepository.InProgressOperation;
            if (kind == GitOperationKind.None)
            {
                return new GitResult(1, string.Empty, GitText.Get("inprogress.nothing"));
            }
            return await GitCommand.RunAsync(GitRepository.RepoRoot, AbortCommand(kind), GitUndo.NoEditor);
        }

        private static async Task<GitResult> AdvanceAsync(Func<GitOperationKind, string[]> command, bool requireReady)
        {
            GitOperationKind kind = GitRepository.InProgressOperation;
            if (kind == GitOperationKind.None)
            {
                return new GitResult(1, string.Empty, GitText.Get("inprogress.nothing"));
            }
            if (requireReady)
            {
                List<string> unmerged = await UnmergedPathsAsync();
                if (unmerged.Count > 0)
                {
                    return new GitResult(1, string.Empty, GitText.Format("inprogress.unresolved", ListPaths(unmerged)));
                }
            }

            // 끝났을 때 되돌릴 곳: rebase 는 시작 전 브랜치 끝(orig-head), 나머지는 아직 움직이지 않은 지금 HEAD.
            string before = kind == GitOperationKind.Rebase ? RebaseOrigHead(GitRepository.GitDir) : await RevParseAsync("HEAD");
            string stashBefore = await GitUndo.StashTopAsync();
            GitResult result = await GitCommand.RunAsync(GitRepository.RepoRoot, command(kind), GitUndo.NoEditor);

            GitOperationKind after = GitRepository.InProgressOperation;
            if (after != GitOperationKind.None)
            {
                // 다음 커밋에서 또 충돌했거나(rebase·여러 커밋 cherry-pick), git 이 거부했다. 어느 쪽이든 배너가 그대로 남는다.
                return result.Success
                    ? result
                    : new GitResult(result.ExitCode, result.Output, GitText.Format("inprogress.stoppedAgain", DisplayName(kind), result.Message));
            }
            if (result.Success == false)
            {
                return result;
            }

            string head = await RevParseAsync("HEAD");
            if (string.IsNullOrEmpty(before) == false && string.IsNullOrEmpty(head) == false && head != before)
            {
                GitUndo.Journal.Record(new GitUndoEntry
                {
                    Kind = GitUndoKind.MoveHead,
                    Title = GitText.Format("inprogress.undoTitle", DisplayName(kind)),
                    HeadBefore = before,
                    HeadAfter = head,
                });
            }
            return kind == GitOperationKind.Rebase && await GitUndo.AutostashConflicted(stashBefore)
                ? new GitResult(0, result.Output, GitText.Get("rebase.autostashConflict"))
                : new GitResult(0, result.Output, string.Empty);
        }

        /// <summary>rebase 를 시작하기 전 브랜치 끝 커밋. 읽지 못하면 null.</summary>
        internal static string RebaseOrigHead(string gitDir)
        {
            if (string.IsNullOrEmpty(gitDir))
            {
                return null;
            }
            foreach (string folder in new[] { "rebase-merge", "rebase-apply" })
            {
                string file = Path.Combine(gitDir, folder, "orig-head");
                if (File.Exists(file))
                {
                    string hash = ReadFirstLine(file);
                    return hash.Length > 0 ? hash : null;
                }
            }
            return null;
        }

        /// <summary>
        /// git 이 충돌로 멈추며 준비해 둔 커밋 메시지(MERGE_MSG). '#' 안내 줄은 뺀다. 없으면 null.
        /// 병합·cherry-pick·revert 를 커밋으로 끝낼 때 입력란을 채우는 데 쓴다.
        /// </summary>
        public static string ReadPreparedMessage(string gitDir)
        {
            if (string.IsNullOrEmpty(gitDir))
            {
                return null;
            }
            string file = Path.Combine(gitDir, "MERGE_MSG");
            try
            {
                if (File.Exists(file) == false)
                {
                    return null;
                }
                IEnumerable<string> lines = File.ReadAllText(file).Replace("\r\n", "\n").Split('\n').Where(line => line.StartsWith("#", StringComparison.Ordinal) == false);
                string message = string.Join("\n", lines).Trim();
                return message.Length > 0 ? message : null;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private static string ReadFirstLine(string file)
        {
            try
            {
                using StreamReader reader = new(file);
                return (reader.ReadLine() ?? string.Empty).Trim();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return string.Empty;
            }
        }

        private static async Task<string> RevParseAsync(string revision)
        {
            GitResult result = await GitRepository.RunAsync("rev-parse", "--verify", "--quiet", revision);
            return result.Success ? result.Output.Trim() : null;
        }
    }
}
