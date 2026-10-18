using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// 워크스페이스 탐색(Gluon 의 "워크스페이스 탐색")에서 파일·폴더 하나의 상태.
    /// </summary>
    public enum GitWorkspaceState
    {
        /// <summary>
        /// 커밋하지 않은 변경이 있음(수정·추가·삭제·이름 변경·충돌). 폴더는 안에 그런 파일이 있음.
        /// </summary>
        Changed,

        /// <summary>
        /// git 이 추적하고 바뀌지 않음(Gluon 의 "제어됨").
        /// </summary>
        Tracked,

        /// <summary>
        /// 추적하지 않는 새 파일(Gluon 의 "비공개").
        /// </summary>
        Untracked,

        /// <summary>
        /// .gitignore 로 무시함(Library·Temp 등).
        /// </summary>
        Ignored,
    }

    /// <summary>
    /// 저장소 전체 파일의 상태표. git 을 두 번만 불러(<c>ls-files</c> 추적 목록, 무시 목록) 만들고, 변경은 이미 읽어 둔 status 를 쓴다.
    /// 그 뒤 <see cref="Classify"/> 는 git 없이 집합만 본다 — 화면이 폴더를 펼칠 때마다 git 을 부르지 않게.
    /// 경로는 모두 저장소 루트 기준, 슬래시 구분, 폴더는 끝 슬래시 없이.
    /// </summary>
    public sealed class GitWorkspaceIndex
    {
        public static readonly string[] TrackedCommand = { "ls-files", "-z" };

        /// <summary>
        /// 무시 항목. <c>--directory</c> 라 통째로 무시하는 폴더(Library/)는 안의 파일 대신 그 폴더 한 줄만 나온다.
        /// </summary>
        public static readonly string[] IgnoredCommand = { "ls-files", "-z", "--others", "--ignored", "--exclude-standard", "--directory" };

        private readonly HashSet<string> _trackedFiles = new(StringComparer.Ordinal);
        private readonly HashSet<string> _trackedFolders = new(StringComparer.Ordinal);
        private readonly HashSet<string> _ignoredFiles = new(StringComparer.Ordinal);
        private readonly HashSet<string> _ignoredFolders = new(StringComparer.Ordinal);
        private readonly Dictionary<string, GitFileState> _changes = new(StringComparer.Ordinal);
        private readonly HashSet<string> _changedFolders = new(StringComparer.Ordinal);

        public int TrackedCount => _trackedFiles.Count;
        public int ChangedCount => _changes.Count;

        public static GitWorkspaceIndex Build(string trackedOutput, string ignoredOutput, IEnumerable<GitStatusEntry> entries)
        {
            GitWorkspaceIndex index = new();
            foreach (string path in SplitZ(trackedOutput))
            {
                index._trackedFiles.Add(path);
                AddAncestors(path, index._trackedFolders);
            }
            foreach (string path in SplitZ(ignoredOutput))
            {
                if (path.EndsWith("/", StringComparison.Ordinal))
                {
                    index._ignoredFolders.Add(path.TrimEnd('/'));
                }
                else
                {
                    index._ignoredFiles.Add(path);
                }
            }
            foreach (GitStatusEntry entry in entries ?? Array.Empty<GitStatusEntry>())
            {
                // status 의 새 폴더는 "folder/" 한 줄일 수 있다(--untracked-files=normal). 창은 all 로 읽지만 끝 슬래시를 떼어 둔다.
                string path = entry.RepoPath.TrimEnd('/');
                index._changes[path] = entry.State;
                AddAncestors(path, index._changedFolders);
            }
            return index;
        }

        public static async Task<(GitWorkspaceIndex index, string error)> LoadAsync(IEnumerable<GitStatusEntry> entries)
        {
            Task<GitResult> trackedTask = GitRepository.RunAsync(TrackedCommand);
            Task<GitResult> ignoredTask = GitRepository.RunAsync(IgnoredCommand);
            GitResult tracked = await trackedTask;
            GitResult ignored = await ignoredTask;
            string error = tracked.Success ? ignored.Success ? null : ignored.Message : tracked.Message;
            List<GitStatusEntry> snapshot = new(entries ?? Array.Empty<GitStatusEntry>());
            GitWorkspaceIndex index = await Task.Run(() => Build(tracked.Success ? tracked.Output : string.Empty, ignored.Success ? ignored.Output : string.Empty, snapshot));
            return (index, error);
        }

        /// <param name="change">파일이 바뀌었으면 그 종류. 아니면 null.</param>
        public GitWorkspaceState Classify(string repoPath, bool isFolder, out GitFileState? change)
        {
            change = null;
            string path = repoPath.TrimEnd('/');
            if (isFolder)
            {
                if (IsIgnored(path, isFolder: true))
                {
                    return GitWorkspaceState.Ignored;
                }
                if (_changedFolders.Contains(path) || _changes.ContainsKey(path))
                {
                    return GitWorkspaceState.Changed;
                }
                return _trackedFolders.Contains(path) ? GitWorkspaceState.Tracked : GitWorkspaceState.Untracked;
            }

            if (_changes.TryGetValue(path, out GitFileState state))
            {
                change = state;
                return state == GitFileState.Untracked ? GitWorkspaceState.Untracked : GitWorkspaceState.Changed;
            }
            if (_trackedFiles.Contains(path))
            {
                return GitWorkspaceState.Tracked;
            }
            if (IsIgnored(path, isFolder: false))
            {
                return GitWorkspaceState.Ignored;
            }
            // status 에 폴더째 나온 새 파일("NewFolder/" 아래)도 새 파일이다.
            return GitWorkspaceState.Untracked;
        }

        /// <summary>
        /// "변경만 보기" 에서 보일지: 바뀐 파일·새 파일, 또는 그런 것을 담은 폴더.
        /// </summary>
        public bool HasPendingChange(string repoPath, bool isFolder)
        {
            string path = repoPath.TrimEnd('/');
            return isFolder ? _changedFolders.Contains(path) || _changes.ContainsKey(path) : _changes.ContainsKey(path) || IsUnderUntrackedFolder(path);
        }

        private bool IsUnderUntrackedFolder(string path)
        {
            for (int slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
            {
                if (_changes.TryGetValue(path.Substring(0, slash), out GitFileState state) && state == GitFileState.Untracked)
                {
                    return true;
                }
            }
            return false;
        }

        private bool IsIgnored(string path, bool isFolder)
        {
            if (isFolder ? _ignoredFolders.Contains(path) : _ignoredFiles.Contains(path))
            {
                return true;
            }
            // 무시한 폴더 아래는 모두 무시다(ls-files --directory 는 그 폴더 한 줄만 준다).
            for (int slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
            {
                if (_ignoredFolders.Contains(path.Substring(0, slash)))
                {
                    return true;
                }
            }
            return false;
        }

        private static void AddAncestors(string path, HashSet<string> folders)
        {
            for (int slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
            {
                if (folders.Add(path.Substring(0, slash)) == false)
                {
                    // 위쪽 폴더는 이미 들어 있다.
                    return;
                }
            }
        }

        private static IEnumerable<string> SplitZ(string output)
        {
            foreach (string part in (output ?? string.Empty).Split('\0'))
            {
                if (part.Length > 0)
                {
                    yield return part;
                }
            }
        }
    }
}
