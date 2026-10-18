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
