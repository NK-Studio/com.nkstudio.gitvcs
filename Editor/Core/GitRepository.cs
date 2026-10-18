using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 현재 Unity 프로젝트가 속한 git 저장소의 상태 캐시.
    ///
    /// Project 창 오버레이는 매 프레임 수백 번 그려지므로 git 을 직접 부르지 않고 이 캐시만 읽는다.
    /// 캐시는 에셋 임포트·에디터 포커스 복귀·커밋 뒤에 백그라운드에서 다시 채운다.
    /// 상태는 static 이라 도메인 리로드 때 사라지고, <see cref="GitVersionControlObject"/> 가 다시 <see cref="Start"/> 한다.
    /// </summary>
    public static class GitRepository
    {
        private const double DebounceSeconds = 0.3;

        public static readonly string ProjectRoot = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');

        public static bool IsRunning { get; private set; }
        public static bool IsRefreshing { get; private set; }
        public static bool IsRepository { get; private set; }

        /// <summary>저장소 루트(슬래시 구분). 저장소가 아니면 null.</summary>
        public static string RepoRoot { get; private set; }

        /// <summary>저장소 루트에서 Unity 프로젝트 루트까지의 상대 경로("" 또는 "client/unity/").</summary>
        public static string ProjectPrefix { get; private set; } = string.Empty;

        public static GitBranchInfo Branch { get; private set; }

        /// <summary>커밋 작성자. 비어 있으면 커밋이 실패하므로 UI 에서 미리 경고한다.</summary>
        public static string UserName { get; private set; } = string.Empty;
        public static string UserEmail { get; private set; } = string.Empty;

        /// <summary>
        /// HEAD 와 upstream 의 커밋 해시. 히스토리 탭은 이 값이 바뀔 때만 git log 를 다시 읽는다
        /// (파일 저장마다 오는 새로고침에 log 까지 다시 돌리지 않으려고).
        /// </summary>
        public static string HeadCommit { get; private set; }

        /// <summary>.git 폴더의 절대 경로(worktree 면 그 worktree 의 git 폴더).</summary>
        public static string GitDir { get; private set; }

        /// <summary>마지막 fetch 시각. 터미널에서 한 fetch 도 FETCH_HEAD 를 갱신하므로 함께 잡힌다.</summary>
        public static DateTime? LastFetchTime
        {
            get
            {
                if (string.IsNullOrEmpty(GitDir))
                {
                    return null;
                }
                string fetchHead = Path.Combine(GitDir, "FETCH_HEAD");
                return File.Exists(fetchHead) ? File.GetLastWriteTime(fetchHead) : null;
            }
        }

        public static string UpstreamCommit { get; private set; }

        public static IReadOnlyList<GitStatusEntry> Entries => s_entries;
        public static string LastError { get; private set; }
        public static DateTime LastRefreshTime { get; private set; }

        /// <summary>새 상태가 반영된 뒤 메인 스레드에서 호출된다.</summary>
        public static event Action Changed;

        private static List<GitStatusEntry> s_entries = new();
        private static Dictionary<string, GitFileState> s_assetStateDict = new(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> s_dirtyFolders = new(StringComparer.OrdinalIgnoreCase);
        private static bool s_refreshPending;
        private static double s_refreshDueTime;

        public static void Start()
        {
            if (IsRunning)
            {
                return;
            }
            IsRunning = true;
            EditorApplication.focusChanged += OnFocusChanged;
            EditorApplication.update += OnUpdate;
            RequestRefresh(immediate: true);
        }

        public static void Stop()
        {
            if (IsRunning == false)
            {
                return;
            }
            IsRunning = false;
            EditorApplication.focusChanged -= OnFocusChanged;
            EditorApplication.update -= OnUpdate;
            s_refreshPending = false;
            HeadCommit = null;
            UpstreamCommit = null;
            Apply(false, null, string.Empty, null, new List<GitStatusEntry>(), null);
        }

        /// <summary>
        /// 새로고침을 예약한다. 짧은 시간 안의 여러 요청(임포트 연쇄 등)은 한 번의 git status 로 합친다.
        /// </summary>
        public static void RequestRefresh(bool immediate = false)
        {
            if (IsRunning == false)
            {
                return;
            }
            s_refreshPending = true;
            s_refreshDueTime = EditorApplication.timeSinceStartup + (immediate ? 0 : DebounceSeconds);
        }

        public static bool TryGetState(string assetPath, out GitFileState state)
        {
            return s_assetStateDict.TryGetValue(assetPath, out state);
        }

        public static bool FolderHasChanges(string assetPath)
        {
            return s_dirtyFolders.Contains(assetPath);
        }

        /// <summary>저장소 루트에서 git 을 실행한다. 저장소가 아니면 프로젝트 루트에서 실행한다.</summary>
        public static Task<GitResult> RunAsync(params string[] args)
        {
            return GitCommand.RunAsync(RepoRoot ?? ProjectRoot, args);
        }

        private static void OnFocusChanged(bool hasFocus)
        {
            if (hasFocus && GitVcsSettings.instance.RefreshOnFocus)
            {
                RequestRefresh();
            }
        }

        private static void OnUpdate()
        {
            if (s_refreshPending && IsRefreshing == false && EditorApplication.timeSinceStartup >= s_refreshDueTime)
            {
                s_refreshPending = false;
                RefreshAsync();
            }
        }

        private static async void RefreshAsync()
        {
            IsRefreshing = true;
            string gitPath = GitVcsSettings.instance.GitExecutable;
            string projectRoot = ProjectRoot;

            try
            {
                // 두 git 호출을 한 번의 백그라운드 작업으로 묶는다. await 뒤는 Unity 동기화 컨텍스트 덕에 메인 스레드다.
                RefreshResult result = await Task.Run(() => Query(gitPath, projectRoot));
                if (IsRunning == false)
                {
                    return;
                }
                UserName = result.UserName;
                UserEmail = result.UserEmail;
                GitDir = result.GitDir;
                HeadCommit = result.HeadCommit;
                UpstreamCommit = result.UpstreamCommit;
                Apply(result.IsRepository, result.RepoRoot, result.Prefix, result.Branch, result.Entries, result.Error);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        private sealed class RefreshResult
        {
            public bool IsRepository;
            public string RepoRoot;
            public string Prefix = string.Empty;
            public GitBranchInfo Branch;
            public List<GitStatusEntry> Entries = new();
            public string UserName = string.Empty;
            public string UserEmail = string.Empty;
            public string GitDir;
            public string HeadCommit;
            public string UpstreamCommit;
            public string Error;
        }

        private static RefreshResult Query(string gitPath, string projectRoot)
        {
            RefreshResult result = new();

            GitResult revParse = GitCommand.Run(gitPath, projectRoot, new[] { "rev-parse", "--show-toplevel", "--show-prefix", "--absolute-git-dir" });
            if (revParse.Success == false)
            {
                // "not a git repository" 는 오류가 아니라 정상 상태(아직 init 전)다.
                if (revParse.Message.IndexOf("not a git repository", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    result.Error = revParse.Message;
                }
                return result;
            }

            string[] lines = revParse.Output.Replace("\r", string.Empty).Split('\n');
            result.IsRepository = true;
            result.RepoRoot = lines[0].Trim();
            result.Prefix = lines.Length > 1 ? lines[1].Trim() : string.Empty;
            result.GitDir = lines.Length > 2 ? lines[2].Trim() : null;

            GitResult status = GitCommand.Run(gitPath, result.RepoRoot,
                new[] { "status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all" });
            if (status.Success == false)
            {
                result.Error = status.Message;
                return result;
            }

            result.Entries = GitStatusParser.Parse(status.Output, result.Prefix, out result.Branch);
            result.UserName = GitCommand.Run(gitPath, result.RepoRoot, new[] { "config", "--get", "user.name" }).Output.Trim();
            result.UserEmail = GitCommand.Run(gitPath, result.RepoRoot, new[] { "config", "--get", "user.email" }).Output.Trim();
            result.HeadCommit = RevParse(gitPath, result.RepoRoot, "HEAD");
            if (result.Branch != null && string.IsNullOrEmpty(result.Branch.Upstream) == false)
            {
                result.UpstreamCommit = RevParse(gitPath, result.RepoRoot, "@{u}");
            }
            return result;
        }

        /// <summary>커밋이 없거나 참조가 없으면 null.</summary>
        private static string RevParse(string gitPath, string repoRoot, string revision)
        {
            GitResult result = GitCommand.Run(gitPath, repoRoot, new[] { "rev-parse", "--verify", "--quiet", revision });
            return result.Success ? result.Output.Trim() : null;
        }

        private static void Apply(bool isRepository, string repoRoot, string prefix, GitBranchInfo branch, List<GitStatusEntry> entries, string error)
        {
            IsRepository = isRepository;
            RepoRoot = repoRoot;
            ProjectPrefix = prefix;
            Branch = branch;
            LastError = error;
            LastRefreshTime = DateTime.Now;
            s_entries = entries;
            BuildLookup(entries);

            EditorApplication.RepaintProjectWindow();
            Changed?.Invoke();
        }

        /// <summary>
        /// Project 창용 조회표. .meta 의 상태는 짝이 되는 에셋에 합친다 — Project 창에는 .meta 가 보이지 않지만,
        /// 새 폴더(폴더.meta 만 untracked)나 임포트 설정만 바뀐 텍스처(.meta 만 modified)도 표시돼야 한다.
        /// </summary>
        private static void BuildLookup(List<GitStatusEntry> entries)
        {
            Dictionary<string, GitFileState> stateDict = new(StringComparer.OrdinalIgnoreCase);
            HashSet<string> dirtyFolders = new(StringComparer.OrdinalIgnoreCase);

            foreach (GitStatusEntry entry in entries)
            {
                if (entry.ProjectPath == null)
                {
                    continue;
                }

                bool isMeta = entry.ProjectPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
                string assetPath = isMeta ? entry.ProjectPath.Substring(0, entry.ProjectPath.Length - 5) : entry.ProjectPath;

                if (stateDict.TryGetValue(assetPath, out GitFileState existing) == false)
                {
                    stateDict[assetPath] = entry.State;
                }
                else if (isMeta == false || entry.State == GitFileState.Conflicted)
                {
                    // 에셋 본문의 상태가 .meta 보다 우선. 충돌만은 어느 쪽이든 드러낸다.
                    stateDict[assetPath] = existing == GitFileState.Conflicted ? existing : entry.State;
                }

                int slash = assetPath.LastIndexOf('/');
                while (slash > 0)
                {
                    string folder = assetPath.Substring(0, slash);
                    if (dirtyFolders.Add(folder) == false)
                    {
                        break;
                    }
                    slash = folder.LastIndexOf('/');
                }
            }

            s_assetStateDict = stateDict;
            s_dirtyFolders = dirtyFolders;
        }
    }

    internal sealed class GitRepositoryAssetWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            GitRepository.RequestRefresh();
        }
    }
}
