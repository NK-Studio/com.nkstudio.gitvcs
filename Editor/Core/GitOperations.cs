using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// Git 창이 쓰는 쓰기·네트워크 명령. 모두 저장소 루트에서 실행하고, 경로는 저장소 루트 기준이다.
    /// </summary>
    public static class GitOperations
    {
        private const int NetworkTimeoutMs = 5 * 60_000;

        /// <summary>
        /// 고른 항목만 커밋한다. 사용자가 터미널에서 따로 stage 해 둔 다른 파일은 건드리지 않고 stage 상태로 남는다.
        ///
        /// <c>git commit --only &lt;paths&gt;</c> 는 지정 경로의 작업 트리 내용을 그대로 커밋하고 나머지 index 는 무시한다.
        /// 경로 매칭은 index 와 HEAD 를 겹쳐서 하므로 삭제·이름 변경의 원래 경로도 넘길 수 있지만, untracked 파일은
        /// "git 이 모르는 경로" 라 먼저 add 해야 한다.
        /// 경로 목록은 Windows 명령줄 길이(32K) 제한을 피하려고 NUL 구분 파일로 넘긴다.
        /// </summary>
        public static async Task<GitResult> CommitAsync(IReadOnlyCollection<GitStatusEntry> entries, string message)
        {
            List<string> untracked = entries.Where(e => e.State == GitFileState.Untracked).Select(e => e.RepoPath).ToList();
            List<string> paths = new();
            foreach (GitStatusEntry entry in entries)
            {
                paths.Add(entry.RepoPath);
                if (entry.OriginalRepoPath != null)
                {
                    paths.Add(entry.OriginalRepoPath);
                }
            }

            string untrackedFile = null;
            string pathsFile = WritePathspecFile(paths);
            string messageFile = Path.GetTempFileName();
            try
            {
                File.WriteAllText(messageFile, message, new UTF8Encoding(false));

                if (untracked.Count > 0)
                {
                    untrackedFile = WritePathspecFile(untracked);
                    GitResult add = await GitRepository.RunAsync("add", "--pathspec-from-file=" + untrackedFile, "--pathspec-file-nul");
                    if (add.Success == false)
                    {
                        return add;
                    }
                }

                return await GitRepository.RunAsync("commit", "--file=" + messageFile, "--only", "--pathspec-from-file=" + pathsFile, "--pathspec-file-nul");
            }
            finally
            {
                TryDelete(pathsFile);
                TryDelete(messageFile);
                TryDelete(untrackedFile);
            }
        }

        /// <summary>
        /// 항목을 HEAD 상태로 되돌린다. 새 파일은 지운다.
        /// </summary>
        public static async Task<GitResult> DiscardAsync(IReadOnlyCollection<GitStatusEntry> entries)
        {
            List<string> untracked = new();
            List<string> added = new();
            List<string> tracked = new();
            foreach (GitStatusEntry entry in entries)
            {
                switch (entry.State)
                {
                    case GitFileState.Untracked:
                        untracked.Add(entry.RepoPath);
                        break;
                    case GitFileState.Added:
                        added.Add(entry.RepoPath);
                        break;
                    default:
                        tracked.Add(entry.RepoPath);
                        if (entry.OriginalRepoPath != null)
                        {
                            tracked.Add(entry.OriginalRepoPath);
                        }
                        break;
                }
            }

            GitResult result = new(0, string.Empty, string.Empty);
            if (untracked.Count > 0)
            {
                // git clean 은 --pathspec-from-file 을 받지 않으므로 직접 지운다.
                string error = await Task.Run(() => DeleteUntracked(GitRepository.RepoRoot, untracked));
                if (error != null)
                {
                    return new GitResult(1, string.Empty, error);
                }
            }
            if (added.Count > 0)
            {
                result = await RunWithPaths(new[] { "rm", "-f", "-q" }, added);
                if (result.Success == false)
                {
                    return result;
                }
            }
            if (tracked.Count > 0)
            {
                // HEAD 에 없는 경로(이름 변경의 새 경로)는 restore 가 index·작업 트리에서 지운다.
                result = await RunWithPaths(new[] { "restore", "--source=HEAD", "--staged", "--worktree" }, tracked);
            }
            return result;
        }

        public static Task<GitResult> PullAsync()
        {
            return GitCommand.RunAsync(GitRepository.RepoRoot, new[] { "pull", "--ff-only" }, NetworkTimeoutMs);
        }

        public static Task<GitResult> PushAsync(GitBranchInfo branch)
        {
            // upstream 이 없는 새 브랜치는 origin 의 같은 이름으로 올리고 추적을 건다.
            string[] args = branch == null || string.IsNullOrEmpty(branch.Upstream)
                ? new[] { "push", "-u", "origin", "HEAD" }
                : new[] { "push" };
            return GitCommand.RunAsync(GitRepository.RepoRoot, args, NetworkTimeoutMs);
        }

        public static Task<GitResult> FetchAsync()
        {
            return GitCommand.RunAsync(GitRepository.RepoRoot, new[] { "fetch", "--prune" }, NetworkTimeoutMs);
        }

        /// <summary>
        /// 로컬 브랜치로 전환한다. 원격 브랜치면 같은 이름의 로컬 추적 브랜치를 만들어 전환한다.
        /// 작업 트리 변경과 충돌하면 git 이 거부하고 아무것도 바꾸지 않는다.
        /// </summary>
        public static Task<GitResult> SwitchAsync(GitBranchRef branch)
        {
            string[] args = branch.IsRemote
                ? new[] { "switch", "--track", branch.Name }
                : new[] { "switch", branch.Name };
            return GitRepository.RunAsync(args);
        }

        public static Task<GitResult> CreateBranchAsync(string name, bool switchTo)
        {
            string[] args = switchTo
                ? new[] { "switch", "-c", name }
                : new[] { "branch", name };
            return GitRepository.RunAsync(args);
        }

        /// <summary><paramref name="force"/> 가 아니면 병합되지 않은 브랜치는 git 이 거부한다(-d).</summary>
        public static Task<GitResult> DeleteBranchAsync(string name, bool force)
        {
            return GitRepository.RunAsync("branch", force ? "-D" : "-d", name);
        }

        public static Task<GitResult> ValidateBranchNameAsync(string name)
        {
            return GitRepository.RunAsync("check-ref-format", "--branch", name);
        }

        /// <summary>
        /// 작업 트리의 변경을 인덱스에 올린다(Fork·GitKraken 의 Stage). 삭제도 함께 올리도록 <c>-A</c>.
        /// </summary>
        public static Task<GitResult> StageAsync(IReadOnlyCollection<GitStatusEntry> entries)
        {
            List<string> paths = entries.Select(e => e.RepoPath).ToList();
            return RunWithPaths(new[] { "add", "-A" }, paths);
        }

        /// <summary>
        /// 인덱스에서 내린다(작업 트리는 그대로). HEAD 가 없는 새 저장소는 restore 할 기준이 없어 <c>rm --cached</c> 로 인덱스에서만 뺀다.
        /// 이름 변경은 원래 경로도 같이 내려야 인덱스에 반쪽 기록이 남지 않는다.
        /// </summary>
        public static async Task<GitResult> UnstageAsync(IReadOnlyCollection<GitStatusEntry> entries)
        {
            List<string> paths = new();
            foreach (GitStatusEntry entry in entries)
            {
                paths.Add(entry.RepoPath);
                if (entry.OriginalRepoPath != null)
                {
                    paths.Add(entry.OriginalRepoPath);
                }
            }
            bool hasHead = (await GitRepository.RunAsync("rev-parse", "--verify", "--quiet", "HEAD")).Success;
            return hasHead
                ? await RunWithPaths(new[] { "restore", "--staged" }, paths)
                : await RunWithPaths(new[] { "rm", "--cached", "-r", "--quiet" }, paths);
        }

        public static Task<GitResult> StageAllAsync()
        {
            return GitRepository.RunAsync("add", "-A");
        }

        public static async Task<GitResult> UnstageAllAsync()
        {
            bool hasHead = (await GitRepository.RunAsync("rev-parse", "--verify", "--quiet", "HEAD")).Success;
            return hasHead
                ? await GitRepository.RunAsync("reset", "--quiet")
                : await GitRepository.RunAsync("rm", "--cached", "-r", "--quiet", ".");
        }

        /// <summary>
        /// 작업 트리 쪽 변경만 버린다(인덱스에 올린 내용은 남는다). untracked 파일은 지운다.
        /// </summary>
        public static async Task<GitResult> DiscardWorkTreeAsync(IReadOnlyCollection<GitStatusEntry> entries)
        {
            List<string> untracked = entries.Where(e => e.State == GitFileState.Untracked).Select(e => e.RepoPath).ToList();
            List<string> tracked = entries.Where(e => e.State != GitFileState.Untracked).Select(e => e.RepoPath).ToList();
            if (untracked.Count > 0)
            {
                string error = DeleteUntracked(GitRepository.RepoRoot, untracked);
                if (error != null)
                {
                    return new GitResult(1, string.Empty, error);
                }
            }
            return tracked.Count > 0
                ? await RunWithPaths(new[] { "restore", "--worktree" }, tracked)
                : new GitResult(0, string.Empty, string.Empty);
        }

        /// <summary>
        /// 인덱스에 올린 것만 커밋한다(Fork·GitKraken 방식). <paramref name="amend"/> 면 마지막 커밋을 고쳐 쓴다 —
        /// 메시지를 비우면 원래 메시지를 그대로 둔다.
        /// </summary>
        public static async Task<GitResult> CommitStagedAsync(string message, bool amend)
        {
            List<string> args = new() { "commit" };
            string messageFile = null;
            if (amend)
            {
                args.Add("--amend");
            }
            try
            {
                if (amend && string.IsNullOrWhiteSpace(message))
                {
                    args.Add("--no-edit");
                }
                else
                {
                    messageFile = Path.GetTempFileName();
                    File.WriteAllText(messageFile, message, new UTF8Encoding(false));
                    args.Add("--file=" + messageFile);
                }
                return await GitRepository.RunAsync(args.ToArray());
            }
            finally
            {
                TryDelete(messageFile);
            }
        }

        /// <summary>
        /// 마지막 커밋 메시지(Amend 를 켤 때 입력란을 채운다). 커밋이 없으면 빈 문자열.
        /// </summary>
        public static async Task<string> LastCommitMessageAsync()
        {
            GitResult result = await GitRepository.RunAsync("log", "-1", "--format=%B");
            return result.Success ? result.Output.TrimEnd() : string.Empty;
        }

        /// <summary>
        /// 작업 트리 변경을 stash 에 넣고 작업 트리를 HEAD 로 되돌린다. Unity 프로젝트는 새 에셋(untracked)도 함께 생기므로
        /// <c>--include-untracked</c> 로 같이 넣는다 — 빼면 .meta 짝만 남아 Unity 가 고아 meta 를 지우거나 다시 만든다.
        /// </summary>
        public static Task<GitResult> StashAsync()
        {
            return GitRepository.RunAsync("stash", "push", "--include-untracked");
        }

        /// <summary>
        /// 가장 최근 stash 를 작업 트리에 되돌리고 목록에서 뺀다. 충돌하면 git 이 stash 를 남겨 둔다.
        /// </summary>
        public static Task<GitResult> StashPopAsync()
        {
            return GitRepository.RunAsync("stash", "pop");
        }

        /// <summary>
        /// stash 개수. 실패하면 0 으로 본다(stash 가 없는 새 저장소도 실패가 아니라 빈 출력이다).
        /// </summary>
        public static async Task<int> StashCountAsync()
        {
            GitResult result = await GitRepository.RunAsync("stash", "list", "--format=%gd");
            if (result.Success == false)
            {
                return 0;
            }
            return result.Output.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static async Task<GitResult> RunWithPaths(string[] args, List<string> paths)
        {
            string pathsFile = WritePathspecFile(paths);
            try
            {
                List<string> fullArgs = new(args) { "--pathspec-from-file=" + pathsFile, "--pathspec-file-nul" };
                return await GitRepository.RunAsync(fullArgs.ToArray());
            }
            finally
            {
                TryDelete(pathsFile);
            }
        }

        /// <summary>
        /// untracked 파일을 지우고, 그 때문에 비게 된 폴더도 지운다. 빈 폴더가 남으면 Unity 가 새 폴더.meta 를 만들어
        /// 방금 되돌린 변경이 다시 생긴다.
        /// </summary>
        private static string DeleteUntracked(string repoRoot, List<string> repoPaths)
        {
            string root = Path.GetFullPath(repoRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            StringBuilder errors = new();

            foreach (string repoPath in repoPaths)
            {
                string fullPath = Path.GetFullPath(Path.Combine(root, repoPath));
                try
                {
                    if (File.Exists(fullPath))
                    {
                        File.Delete(fullPath);
                    }

                    string dir = Path.GetDirectoryName(fullPath);
                    while (dir != null && dir.Length > root.Length && Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any() == false)
                    {
                        Directory.Delete(dir);
                        dir = Path.GetDirectoryName(dir);
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    errors.AppendLine($"{repoPath}: {e.Message}");
                }
            }
            return errors.Length == 0 ? null : errors.ToString();
        }

        private static string WritePathspecFile(IEnumerable<string> paths)
        {
            string file = Path.GetTempFileName();
            File.WriteAllText(file, string.Join("\0", paths), new UTF8Encoding(false));
            return file;
        }

        private static void TryDelete(string file)
        {
            if (file == null)
            {
                return;
            }
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // 임시 파일이라 남아도 문제없다.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
