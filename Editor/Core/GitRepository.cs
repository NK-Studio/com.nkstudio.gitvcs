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
    /// 캐시는 에셋 임포트·에디터 포커스 복귀·커밋 뒤, 그리고 저장소 폴더의 바깥 변경(<see cref="GitFileWatcher"/>)·자동 fetch(<see cref="GitAutoFetch"/>) 때 백그라운드에서 다시 채운다.
    /// 상태는 static 이라 도메인 리로드 때 사라지고, <see cref="GitIntegration"/> 이 다시 <see cref="Start"/> 한다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitRepository
    {
        private const double DebounceSeconds = 0.3;

        public static readonly string ProjectRoot = Path.GetDirectoryName(Application.dataPath)?.Replace('\\', '/');

        public static bool IsRunning { get; private set; }
        public static bool IsRefreshing { get; private set; }
        public static bool IsRepository { get; private set; }

        /// <summary>저장소 루트(슬래시 구분). 저장소가 아니면 null.</summary>
        public static string RepoRoot
        {
            get => s_testScope?.RepoRoot ?? s_repoRoot;
            private set => s_repoRoot = value;
        }

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
        public static string GitDir
        {
            get => s_testScope?.GitDir ?? s_gitDir;
            private set => s_gitDir = value;
        }

        /// <summary>
        /// rebase 가 멈춘 채 진행 중인지(충돌 등). 터미널에서 시작한 것도 포함한다 — 그때 다른 rebase 를 시작하거나 취소하면 안 된다.
        /// </summary>
        public static bool IsRebaseInProgress
        {
            get
            {
                string gitDir = GitDir;
                return string.IsNullOrEmpty(gitDir) == false
                    && (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply")));
            }
        }

        /// <summary>
        /// 충돌 등으로 멈춘 채 진행 중인 작업(rebase·merge·cherry-pick·revert). 터미널에서 시작한 것도 포함한다.
        /// git 폴더의 표시 파일만 보므로 캐시하지 않고 그때그때 읽는다 — 표시 파일이 생기고 사라지면 파일 감시가 새로고침을 부른다.
        /// </summary>
        public static GitOperationKind InProgressOperation => GitInProgress.Detect(GitDir);

        /// <summary>테스트가 git 작업 코드를 임시 저장소로 돌리는 중인지(<see cref="UseRepositoryForTests"/>). 그동안 새로고침·감시·자동 fetch 는 쉰다.</summary>
        public static bool IsOverriddenForTests => s_testScope != null;

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

        /// <summary>
        /// 마지막 새로고침이 작업 트리(git status)만 다시 읽었는지. 그때는 브랜치 전환·커밋이 없었으므로 HEAD·upstream·사용자 설정은 그대로다 —
        /// stash 개수처럼 그런 작업에서만 바뀌는 값은 다시 읽지 않아도 된다.
        /// </summary>
        public static bool LastRefreshWasWorkingTreeOnly { get; private set; }

        /// <summary>새 상태가 반영된 뒤 메인 스레드에서 호출된다.</summary>
        public static event Action Changed;

        private static string s_repoRoot;
        private static string s_gitDir;
        private static TestScope s_testScope;

        private static List<GitStatusEntry> s_entries = new();
        private static Dictionary<string, GitFileState> s_assetStateDict = new(StringComparer.OrdinalIgnoreCase);
        private static HashSet<string> s_dirtyFolders = new(StringComparer.OrdinalIgnoreCase);
        private static bool s_refreshPending;

        /// <summary>
        /// 예약된 요청 중 하나라도 전체 새로고침을 원했는지. 작업 트리만 원한 요청끼리 모였을 때만 git status 하나로 끝낸다.
        /// </summary>
        private static bool s_fullRefreshPending;
        private static double s_refreshDueTime;

        // ── 새로고침 세대 ── 요청마다 번호를 올리고, 새로고침은 시작할 때의 번호를 들고 간다. 그 새로고침이 반영되면 그 번호까지 끝난 것이다.
        // 요청보다 먼저 시작한 새로고침(낡은 결과)은 번호가 작아 그 요청을 기다리는 쪽을 풀지 않는다(WaitForRefreshAsync).
        private static int s_requestedGeneration;
        private static int s_completedGeneration;
        private static readonly List<(int Generation, TaskCompletionSource<bool> Waiter)> s_refreshWaiters = new();

        public static void Start()
        {
            if (IsRunning)
            {
                return;
            }
            IsRunning = true;
            EditorApplication.focusChanged += OnFocusChanged;
            EditorApplication.update += OnUpdate;
            GitAutoFetch.Start();
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
            GitAutoFetch.Stop();
            s_refreshPending = false;
            HeadCommit = null;
            UpstreamCommit = null;
            Apply(false, null, string.Empty, null, new List<GitStatusEntry>(), null);
            CompleteRefreshes(int.MaxValue);
        }

        /// <summary>
        /// 테스트 전용: <see cref="RunAsync"/>·<see cref="RepoRoot"/>·<see cref="GitDir"/> 를 임시 저장소로 돌리고, 실행 취소 기록은 <paramref name="journal"/> 에 모은다.
        /// 그러면 rebase·실행 취소 같은 작업 코드를 실제 프로젝트 저장소를 건드리지 않고 그대로 시험할 수 있다.
        /// 그동안 이 저장소의 새로고침·파일 감시·자동 fetch 는 쉬고, 끝나면 다시 맞춘다. 한 번에 하나만 쓴다.
        /// </summary>
        public static IDisposable UseRepositoryForTests(string repoRoot, string gitDir, GitUndoJournal journal)
        {
            if (s_testScope != null)
            {
                throw new InvalidOperationException("UseRepositoryForTests is already active.");
            }
            s_testScope = new TestScope(repoRoot.Replace('\\', '/'), gitDir.Replace('\\', '/'));
            GitUndo.OverrideJournal(journal);
            return s_testScope;
        }

        private sealed class TestScope : IDisposable
        {
            public readonly string RepoRoot;
            public readonly string GitDir;

            public TestScope(string repoRoot, string gitDir)
            {
                RepoRoot = repoRoot;
                GitDir = gitDir;
            }

            public void Dispose()
            {
                if (s_testScope != this)
                {
                    return;
                }
                s_testScope = null;
                GitUndo.OverrideJournal(null);
                UpdateFileWatcher();
            }
        }

        /// <summary>
        /// 지금까지 요청된 새로고침이 화면(<see cref="Changed"/>)까지 반영되면 끝나는 Task. 이미 반영됐거나 꺼져 있으면 바로 끝난다.
        /// git 작업 뒤 "결과가 보일 때까지" 버튼을 꺼 두는 데 쓴다(<see cref="GitOperationRunner"/>). 새로고침이 실패해도 끝난다.
        /// </summary>
        public static Task WaitForRefreshAsync()
        {
            int target = s_requestedGeneration;
            if (IsRunning == false || s_completedGeneration >= target)
            {
                return Task.CompletedTask;
            }
            TaskCompletionSource<bool> waiter = new();
            s_refreshWaiters.Add((target, waiter));
            return waiter.Task;
        }

        /// <summary>
        /// <paramref name="generation"/> 번까지의 요청이 반영됐다. 그 번호 이하를 기다리던 쪽을 깨운다.
        /// </summary>
        private static void CompleteRefreshes(int generation)
        {
            s_completedGeneration = Math.Max(s_completedGeneration, generation);
            for (int i = s_refreshWaiters.Count - 1; i >= 0; i--)
            {
                if (s_refreshWaiters[i].Generation <= s_completedGeneration)
                {
                    TaskCompletionSource<bool> waiter = s_refreshWaiters[i].Waiter;
                    s_refreshWaiters.RemoveAt(i);
                    waiter.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// 새로고침을 예약한다. 짧은 시간 안의 여러 요청(임포트 연쇄 등)은 한 번의 git status 로 합친다.
        /// </summary>
        /// <param name="workingTreeOnly">
        /// HEAD·브랜치가 바뀌지 않는 작업(Stage·Unstage·파일 저장) 뒤면 true — git status 하나만 돌린다(git 실행 6번 → 1번).
        /// 전체 새로고침이 하나라도 예약돼 있으면 전체로 한다.
        /// </param>
        public static void RequestRefresh(bool immediate = false, bool workingTreeOnly = false)
        {
            if (IsRunning == false)
            {
                return;
            }
            s_refreshPending = true;
            s_requestedGeneration++;
            s_fullRefreshPending |= workingTreeOnly == false;
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
            if (s_testScope != null)
            {
                return;
            }
            if (s_refreshPending && IsRefreshing == false && EditorApplication.timeSinceStartup >= s_refreshDueTime)
            {
                s_refreshPending = false;
                // 아직 저장소 루트를 모르면(첫 새로고침·저장소가 아닐 때) 작업 트리만 읽을 수 없다.
                bool full = s_fullRefreshPending || IsRepository == false || RepoRoot == null;
                s_fullRefreshPending = false;
                RefreshAsync(full, s_requestedGeneration);
            }
        }

        /// <param name="generation">시작할 때까지 들어온 요청 번호. 끝나면(실패해도) 이 번호까지 반영된 것으로 알린다.</param>
        private static async void RefreshAsync(bool full, int generation)
        {
            IsRefreshing = true;
            string gitPath = GitVcsSettings.instance.GitExecutable;
            string projectRoot = ProjectRoot;
            string repoRoot = RepoRoot;
            string prefix = ProjectPrefix;

            try
            {
                // git 호출을 한 번의 백그라운드 작업으로 묶는다. await 뒤는 Unity 동기화 컨텍스트 덕에 메인 스레드다.
                RefreshResult result = await Task.Run(() => full ? Query(gitPath, projectRoot) : QueryWorkingTree(gitPath, projectRoot, repoRoot, prefix));
                if (IsRunning == false)
                {
                    return;
                }
                LastRefreshWasWorkingTreeOnly = result.WorkingTreeOnly;
                if (result.WorkingTreeOnly == false)
                {
                    UserName = result.UserName;
                    UserEmail = result.UserEmail;
                    GitDir = result.GitDir;
                    HeadCommit = result.HeadCommit;
                    UpstreamCommit = result.UpstreamCommit;
                }
                Apply(result.IsRepository, result.RepoRoot, result.Prefix, result.Branch, result.Entries, result.Error);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                IsRefreshing = false;
                CompleteRefreshes(generation);
            }
        }

        private sealed class RefreshResult
        {
            /// <summary>
            /// git status 만 읽었다. 사용자·HEAD·upstream·git 폴더는 비어 있고, 지난 값을 그대로 쓴다.
            /// </summary>
            public bool WorkingTreeOnly;

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

        /// <summary>
        /// 작업 트리만 다시 읽는다(git status 하나). 저장소 루트·접두는 지난 전체 새로고침 값을 쓴다.
        /// status 가 실패하면(저장소가 지워지는 등) 전체 새로고침으로 넘어간다.
        /// </summary>
        private static RefreshResult QueryWorkingTree(string gitPath, string projectRoot, string repoRoot, string prefix)
        {
            GitResult status = GitCommand.Run(gitPath, repoRoot,
                new[] { "status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all" });
            if (status.Success == false)
            {
                return Query(gitPath, projectRoot);
            }
            RefreshResult result = new()
            {
                WorkingTreeOnly = true,
                IsRepository = true,
                RepoRoot = repoRoot,
                Prefix = prefix,
            };
            result.Entries = GitStatusParser.Parse(status.Output, prefix, out result.Branch);
            return result;
        }

        /// <summary>커밋이 없거나 참조가 없으면 null.</summary>
        private static string RevParse(string gitPath, string repoRoot, string revision)
        {
            GitResult result = GitCommand.Run(gitPath, repoRoot, new[] { "rev-parse", "--verify", "--quiet", revision });
            return result.Success ? result.Output.Trim() : null;
        }

        /// <summary>파일 감시를 설정·저장소 상태에 맞춘다(설정 화면에서 켜고 끌 때도 부른다).</summary>
        public static void UpdateFileWatcher()
        {
            if (s_testScope != null)
            {
                return;
            }
            GitFileWatcher.Sync(IsRunning && IsRepository && GitVcsSettings.instance.WatchFileSystem, RepoRoot, GitDir, ProjectPrefix);
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
            UpdateFileWatcher();

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
            // 에셋 저장·임포트는 작업 트리만 바꾼다(HEAD·브랜치는 그대로). 터미널에서 한 커밋·전환은 포커스 복귀 때 전체로 읽는다.
            GitRepository.RequestRefresh(workingTreeOnly: true);
        }
    }
}
