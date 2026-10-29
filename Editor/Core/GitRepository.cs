using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 현재 Unity 프로젝트가 속한 git 저장소의 상태 캐시. Git 창에서 서브모듈을 열면(<see cref="OpenRepository"/>) 그 저장소를 대신 본다.
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

        /// <summary>
        /// 연 저장소가 Unity 프로젝트 안의 하위 폴더일 때(서브모듈) 프로젝트에서 그 저장소까지의 경로("Packages/x/"). 프로젝트 저장소면 "".
        /// 프로젝트 밖의 저장소면 null — 파일에 프로젝트 경로가 없다.
        /// </summary>
        public static string RepoInProject { get; private set; } = string.Empty;

        /// <summary>Unity 프로젝트가 속한 저장소의 루트. 서브모듈을 연 동안에도 그대로라, 돌아갈 곳과 서브모듈 목록을 여기서 찾는다.</summary>
        public static string MainRepoRoot { get; private set; }

        /// <summary>
        /// 프로젝트 저장소 대신 연 저장소(서브모듈)의 루트. null 이면 프로젝트 저장소를 보고 있다(<see cref="OpenRepository"/>).
        /// </summary>
        public static string OpenedRoot => s_openedRoot;

        /// <summary>
        /// 연 저장소가 바뀌었을 때(<see cref="OpenRepository"/>). 창은 탭을 다시 만든다 — 지난 저장소의 목록·선택이 남지 않게.
        /// </summary>
        public static event Action RepositorySwitched;

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

        /// <summary>등록된 원격이 하나라도 있는지(마지막 전체 새로고침 기준).</summary>
        public static bool HasRemote { get; private set; }

        /// <summary>
        /// 모든 참조의 지문(마지막 전체 새로고침 기준). 그래프가 stash·태그·다른 브랜치 변화를 알아 다시 그리게 한다.
        /// </summary>
        public static string RefsFingerprint { get; private set; }

        /// <summary>새로고침이 한 번에 읽는 저장소 설정: 작성자 이름·이메일과 원격 주소들.</summary>
        public const string RepoConfigPattern = @"^(user\.(name|email)|remote\..*\.url)$";

        /// <summary>
        /// <c>git config -z --get-regexp</c> 출력("키\n값\0" 반복)에서 이름·이메일·원격 유무. 같은 키가 여럿이면 git 처럼 마지막 값. 순수 함수라 테스트로 확인한다.
        /// </summary>
        public static (string userName, string userEmail, bool hasRemote) ParseRepoConfig(string output)
        {
            string name = string.Empty;
            string email = string.Empty;
            bool hasRemote = false;
            foreach (string record in (output ?? string.Empty).Split('\0'))
            {
                int newline = record.IndexOf('\n');
                string key = (newline < 0 ? record : record.Substring(0, newline)).Trim().ToLowerInvariant();
                string value = newline < 0 ? string.Empty : record.Substring(newline + 1).Trim();
                if (key == "user.name")
                {
                    name = value;
                }
                else if (key == "user.email")
                {
                    email = value;
                }
                else if (key.StartsWith("remote.", StringComparison.Ordinal) && key.EndsWith(".url", StringComparison.Ordinal))
                {
                    hasRemote = true;
                }
            }
            return (name, email, hasRemote);
        }

        /// <summary><c>for-each-ref --format="%(refname) %(objectname)"</c> 출력에서 그 참조의 커밋. 없으면 null.</summary>
        public static string FindRef(string refsOutput, string refName)
        {
            foreach (string line in (refsOutput ?? string.Empty).Split('\n'))
            {
                int space = line.LastIndexOf(' ');
                if (space == refName.Length && line.StartsWith(refName, StringComparison.Ordinal))
                {
                    return line.Substring(space + 1).Trim();
                }
            }
            return null;
        }

        private static string Fingerprint(string text)
        {
            unchecked
            {
                // 내용 비교용이라 충돌에 강할 필요는 없다(FNV-1a 64비트).
                ulong hash = 14695981039346656037UL;
                foreach (char c in text ?? string.Empty)
                {
                    hash = (hash ^ c) * 1099511628211UL;
                }
                return hash.ToString("x16");
            }
        }

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

        /// <summary>연 서브모듈. 스크립트를 다시 컴파일해도 그대로 보도록 에디터 세션에 둔다(에디터를 다시 켜면 프로젝트 저장소로).</summary>
        private const string OpenedRootKey = "GitVersionControl.OpenedRepository";

        private static string s_repoRoot;
        private static string s_gitDir;
        private static string s_openedRoot;
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
            string opened = SessionState.GetString(OpenedRootKey, string.Empty);
            s_openedRoot = opened.Length > 0 ? opened : null;
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
            Apply(false, null, string.Empty, string.Empty, null, new List<GitStatusEntry>(), null);
            CompleteRefreshes(int.MaxValue);
        }

        /// <summary>
        /// Git 창이 볼 저장소를 바꾼다(Fork 처럼 서브모듈을 연다). null 이나 프로젝트 저장소 루트면 프로젝트 저장소로 돌아간다.
        /// 지난 저장소의 상태를 비우고 곧바로 새로 읽는다. 모든 git 작업·파일 감시·자동 fetch 가 연 저장소를 따른다.
        /// </summary>
        public static void OpenRepository(string root)
        {
            string normalized = string.IsNullOrEmpty(root) ? null : root.Replace('\\', '/').TrimEnd('/');
            if (normalized != null && string.Equals(normalized, MainRepoRoot, StringComparison.OrdinalIgnoreCase))
            {
                normalized = null;
            }
            if (string.Equals(normalized, s_openedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            s_openedRoot = normalized;
            SessionState.SetString(OpenedRootKey, normalized ?? string.Empty);
            HeadCommit = null;
            UpstreamCommit = null;
            GitDir = null;
            Apply(false, null, string.Empty, string.Empty, null, new List<GitStatusEntry>(), null);
            RequestRefresh(immediate: true);
            RepositorySwitched?.Invoke();
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
            if (immediate)
            {
                // 다른 새로고침이 돌고 있지 않으면 다음 틱을 기다리지 않고 바로 시작한다(입력 없는 에디터는 틱이 약 0.1초 간격이라
                // 커밋·전환 뒤 화면이 그만큼 늦게 바뀐다). 돌고 있으면 끝난 뒤 틱에서 이어 한다.
                TryStartPendingRefresh();
                // 에디터가 뒤에 있으면 업데이트 틱이 몇 초씩 늦어진다. 바로 한 번 돌게 깨워, 누른 Refresh 가 기다리지 않게 한다.
                EditorApplication.QueuePlayerLoopUpdate();
            }
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
            TryStartPendingRefresh();
        }

        private static void TryStartPendingRefresh()
        {
            if (s_testScope != null || s_refreshPending == false || IsRefreshing || EditorApplication.timeSinceStartup < s_refreshDueTime)
            {
                return;
            }
            s_refreshPending = false;
            // 아직 저장소 루트를 모르면(첫 새로고침·저장소가 아닐 때) 작업 트리만 읽을 수 없다.
            bool full = s_fullRefreshPending || IsRepository == false || RepoRoot == null;
            s_fullRefreshPending = false;
            RefreshAsync(full, s_requestedGeneration);
        }

        /// <param name="generation">시작할 때까지 들어온 요청 번호. 끝나면(실패해도) 이 번호까지 반영된 것으로 알린다.</param>
        private static async void RefreshAsync(bool full, int generation)
        {
            IsRefreshing = true;
            string gitPath = GitVcsSettings.instance.GitExecutable;
            string projectRoot = ProjectRoot;
            string repoRoot = RepoRoot;
            string prefix = ProjectPrefix;
            string repoInProject = RepoInProject;
            string openedRoot = s_openedRoot;

            try
            {
                // git 호출을 한 번의 백그라운드 작업으로 묶는다. await 뒤는 Unity 동기화 컨텍스트 덕에 메인 스레드다.
                RefreshResult result = await Task.Run(() => full
                    ? Query(gitPath, projectRoot, openedRoot)
                    : QueryWorkingTree(gitPath, projectRoot, openedRoot, repoRoot, prefix, repoInProject));
                // 읽는 사이에 다른 저장소를 열었으면 지난 저장소의 결과다. 버린다(새 저장소의 새로고침이 이미 예약돼 있다).
                if (IsRunning == false || string.Equals(openedRoot, s_openedRoot, StringComparison.OrdinalIgnoreCase) == false)
                {
                    return;
                }
                if (result.OpenedRootMissing)
                {
                    // 연 서브모듈 폴더가 없어졌다(지웠거나 init 전). 프로젝트 저장소로 돌아간다.
                    OpenRepository(null);
                    return;
                }
                if (result.MainRepoRoot != null)
                {
                    MainRepoRoot = result.MainRepoRoot;
                }
                LastRefreshWasWorkingTreeOnly = result.WorkingTreeOnly;
                if (result.WorkingTreeOnly == false)
                {
                    UserName = result.UserName;
                    UserEmail = result.UserEmail;
                    GitDir = result.GitDir;
                    HeadCommit = result.HeadCommit;
                    UpstreamCommit = result.UpstreamCommit;
                    HasRemote = result.HasRemote;
                    RefsFingerprint = result.RefsFingerprint;
                }
                Apply(result.IsRepository, result.RepoRoot, result.Prefix, result.RepoInProject, result.Branch, result.Entries, result.Error);
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
            public string RepoInProject = string.Empty;

            /// <summary>프로젝트가 속한 저장소의 루트. 작업 트리만 읽었으면 null(지난 값 그대로).</summary>
            public string MainRepoRoot;

            /// <summary>연 서브모듈이 git 저장소로 읽히지 않는다(폴더가 없거나 init 전).</summary>
            public bool OpenedRootMissing;
            public GitBranchInfo Branch;
            public List<GitStatusEntry> Entries = new();
            public string UserName = string.Empty;
            public string UserEmail = string.Empty;
            public string GitDir;
            public string HeadCommit;
            public string UpstreamCommit;

            /// <summary>등록된 원격이 하나라도 있는지. 작업 트리만 읽었으면 지난 값 그대로.</summary>
            public bool HasRemote;

            /// <summary>모든 참조(브랜치·태그·stash·원격)의 지문. 하나라도 움직이면 바뀐다.</summary>
            public string RefsFingerprint;
            public string Error;
        }

        /// <param name="openedRoot">연 서브모듈의 루트. null 이면 프로젝트가 속한 저장소를 읽는다.</param>
        private static RefreshResult Query(string gitPath, string projectRoot, string openedRoot)
        {
            RefreshResult result = new();

            if (openedRoot != null && Directory.Exists(openedRoot) == false)
            {
                result.OpenedRootMissing = true;
                return result;
            }
            GitResult revParse = GitCommand.Run(gitPath, openedRoot ?? projectRoot, new[] { "rev-parse", "--show-toplevel", "--show-prefix", "--absolute-git-dir" });
            if (revParse.Success == false)
            {
                if (openedRoot != null)
                {
                    result.OpenedRootMissing = true;
                    return result;
                }
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
            if (openedRoot != null)
            {
                // 서브모듈은 프로젝트 "안" 에 있다: 프로젝트 경로 = 프로젝트에서 서브모듈까지 + 서브모듈 안 경로.
                result.Prefix = string.Empty;
                result.RepoInProject = RepoInProjectOf(projectRoot, result.RepoRoot);
                // 서브모듈을 연 채로 스크립트를 다시 컴파일하면 프로젝트 저장소 루트를 아직 모른다(돌아갈 곳·서브모듈 목록에 쓴다).
                GitResult main = GitCommand.Run(gitPath, projectRoot, new[] { "rev-parse", "--show-toplevel" });
                result.MainRepoRoot = main.Success ? main.Output.Trim() : null;
            }
            else
            {
                result.MainRepoRoot = result.RepoRoot;
            }

            // Unity(Mono) 안에서는 git 프로세스를 띄우는 것만으로 한 번에 30~50ms 가 들고, 동시에 띄워도 거의 줄을 선다.
            // 그래서 띄우는 수를 줄인다: 이름·이메일·원격 유무는 config 한 번, HEAD·upstream 커밋은 이미 읽는 for-each-ref 에서 꺼낸다.
            // 셋은 서로 기다릴 것이 없어 함께 돌린다(모두 읽기 전용, GIT_OPTIONAL_LOCKS=0).
            string root = result.RepoRoot;
            Task<GitResult> Start(params string[] args) => Task.Run(() => GitCommand.Run(gitPath, root, args));
            Task<GitResult> statusTask = Start("status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all");
            Task<GitResult> configTask = Start("config", "-z", "--get-regexp", RepoConfigPattern);
            Task<GitResult> refsTask = Start("for-each-ref", "--format=%(refname) %(objectname)");
            Task.WaitAll(statusTask, configTask, refsTask);

            GitResult status = statusTask.Result;
            if (status.Success == false)
            {
                result.Error = status.Message;
                return result;
            }

            result.Entries = GitStatusParser.Parse(status.Output, result.Prefix, out result.Branch, result.RepoInProject);
            (result.UserName, result.UserEmail, result.HasRemote) = ParseRepoConfig(configTask.Result.Success ? configTask.Result.Output : string.Empty);
            string refs = refsTask.Result.Output;
            result.RefsFingerprint = Fingerprint(refs);
            GitBranchInfo branch = result.Branch;
            if (branch != null && branch.Detached == false)
            {
                result.HeadCommit = branch.HasCommits ? FindRef(refs, "refs/heads/" + branch.Name) : null;
            }
            else
            {
                // 분리된 HEAD 는 참조 목록에 없다. 드물어 그때만 따로 묻는다.
                GitResult head = GitCommand.Run(gitPath, root, new[] { "rev-parse", "--verify", "--quiet", "HEAD" });
                result.HeadCommit = head.Success ? head.Output.Trim() : null;
            }
            if (branch != null && string.IsNullOrEmpty(branch.Upstream) == false)
            {
                // upstream 은 보통 원격 브랜치("origin/main")지만 로컬 브랜치를 따라갈 수도 있다.
                result.UpstreamCommit = FindRef(refs, "refs/remotes/" + branch.Upstream) ?? FindRef(refs, "refs/heads/" + branch.Upstream);
            }
            return result;
        }

        /// <summary>
        /// 작업 트리만 다시 읽는다(git status 하나). 저장소 루트·접두는 지난 전체 새로고침 값을 쓴다.
        /// status 가 실패하면(저장소가 지워지는 등) 전체 새로고침으로 넘어간다.
        /// </summary>
        private static RefreshResult QueryWorkingTree(string gitPath, string projectRoot, string openedRoot, string repoRoot, string prefix, string repoInProject)
        {
            GitResult status = GitCommand.Run(gitPath, repoRoot,
                new[] { "status", "--porcelain=v1", "-z", "--branch", "--untracked-files=all" });
            if (status.Success == false)
            {
                return Query(gitPath, projectRoot, openedRoot);
            }
            RefreshResult result = new()
            {
                WorkingTreeOnly = true,
                IsRepository = true,
                RepoRoot = repoRoot,
                Prefix = prefix,
                RepoInProject = repoInProject,
            };
            result.Entries = GitStatusParser.Parse(status.Output, prefix, out result.Branch, repoInProject);
            return result;
        }

        /// <summary>
        /// 프로젝트 루트에서 저장소 루트까지("Packages/x/"). 저장소가 프로젝트 밖이면 null.
        /// </summary>
        public static string RepoInProjectOf(string projectRoot, string repoRoot)
        {
            string project = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            string repo = repoRoot.Replace('\\', '/').TrimEnd('/') + "/";
            return repo.StartsWith(project, StringComparison.OrdinalIgnoreCase) ? repo.Substring(project.Length) : null;
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

        private static void Apply(bool isRepository, string repoRoot, string prefix, string repoInProject, GitBranchInfo branch, List<GitStatusEntry> entries, string error)
        {
            IsRepository = isRepository;
            RepoRoot = repoRoot;
            ProjectPrefix = prefix;
            RepoInProject = repoInProject;
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
