using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>바뀐 경로가 부르는 새로고침 범위(<see cref="GitFileWatcher.Classify"/>).</summary>
    public enum GitChangeKind
    {
        /// <summary>status 에 영향이 없다(Library·git 객체·잠금 파일 등).</summary>
        None,

        /// <summary>작업 트리·index 만 바뀌었다 — git status 하나로 충분하다.</summary>
        WorkingTree,

        /// <summary>HEAD·브랜치·원격 참조·설정이 바뀌었을 수 있다 — 전부 다시 읽는다.</summary>
        Full,
    }

    /// <summary>
    /// 저장소 폴더를 감시해 바깥(터미널·IDE·다른 도구)에서 생긴 변경을 Unity 로 돌아오지 않아도 바로 <see cref="GitRepository"/> 에 반영한다.
    ///
    /// 포커스 복귀·에셋 임포트 새로고침만으로는 Unity 를 보고 있는 동안 다른 프로그램이 바꾼 파일·커밋·브랜치 전환이 보이지 않는다.
    /// Unity Mono 의 <see cref="FileSystemWatcher"/> 는 OS 알림이 아니라 폴더를 통째로 훑는 폴링 구현이라 쓰지 않는다.
    /// <list type="bullet">
    /// <item>Windows: <see cref="WindowsDirectoryWatcher"/>(ReadDirectoryChangesW)로 작업 트리와 git 폴더의 모든 변경을 받는다.</item>
    /// <item>그 밖: git 폴더의 몇 파일(HEAD·index·packed-refs…)의 수정 시각만 폴링한다 — 커밋·브랜치 전환·stage 는 잡지만 작업 트리 파일 저장은 포커스 복귀 때 읽는다.</item>
    /// </list>
    /// 이벤트는 백그라운드 스레드에서 오므로 플래그만 세우고, 메인 스레드(<see cref="EditorApplication.update"/>)에서 모아 한 번 새로고침한다.
    /// 우리 git status 는 <c>GIT_OPTIONAL_LOCKS=0</c> 이라 index 를 쓰지 않으므로 새로고침이 다시 이벤트를 부르는 고리는 없다.
    /// </summary>
    public static class GitFileWatcher
    {
        /// <summary>
        /// 감시로 시작하는 새로고침 사이의 최소 간격. 빌드·대량 복사처럼 이벤트가 쉬지 않고 와도 git status 가 줄지어 돌지 않게 한다.
        /// </summary>
        private const double MinIntervalSeconds = 1.0;

        private const double PollIntervalSeconds = 1.0;

        /// <summary>
        /// Unity 가 쉬지 않고 쓰는 프로젝트 폴더. 보통 .gitignore 대상이라 status 결과를 바꾸지 않는다.
        /// </summary>
        private static readonly HashSet<string> s_ignoredProjectFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Library", "Temp", "Logs", "obj", "UserSettings", "MemoryCaptures", ".vs", ".gradle",
        };

        /// <summary>
        /// 폴링에서 볼 git 폴더 안 파일·폴더. index 만 작업 트리 변경이고 나머지는 HEAD·브랜치가 움직였다는 뜻이다.
        /// 진행 중 작업의 표시(MERGE_HEAD·CHERRY_PICK_HEAD·REVERT_HEAD·rebase 폴더·sequencer)는 생기고 사라지는 것으로 잡는다 — Git 창 배너가 따라 바뀐다.
        /// </summary>
        private static readonly string[] s_polledGitFiles =
        {
            "index", "HEAD", "packed-refs", "FETCH_HEAD", "logs/HEAD",
            "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "rebase-merge", "rebase-apply", "sequencer",
        };

        private static readonly List<WindowsDirectoryWatcher> s_watchers = new();

        // 감시 기준 경로(슬래시 구분, 끝에 '/'). 이벤트 스레드가 읽으므로 감시를 멈춘 뒤에만 바꾼다.
        private static string s_repoRoot;
        private static string s_gitDir;
        private static string s_projectPrefix;
        private static bool s_active;

        private static int s_workingTreeDirty;
        private static int s_fullDirty;
        private static double s_lastRequestTime;

        private static DateTime[] s_polledTimes;
        private static double s_nextPollTime;

        static GitFileWatcher()
        {
            // 도메인 리로드 전에 감시 스레드와 OS 핸들을 놓는다. 리로드 뒤에는 GitRepository 의 첫 새로고침이 다시 Sync 한다.
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        /// <summary>
        /// 감시 대상을 저장소 상태에 맞춘다. 같은 저장소면 아무것도 하지 않는다. 메인 스레드에서 부른다.
        /// </summary>
        public static void Sync(bool enabled, string repoRoot, string gitDir, string projectPrefix)
        {
            if (enabled == false || string.IsNullOrEmpty(repoRoot) || Directory.Exists(repoRoot) == false)
            {
                Stop();
                return;
            }

            string root = WithSlash(repoRoot);
            string git = string.IsNullOrEmpty(gitDir) ? root + ".git/" : WithSlash(gitDir);
            string prefix = projectPrefix ?? string.Empty;
            if (s_active
                && string.Equals(root, s_repoRoot, StringComparison.OrdinalIgnoreCase)
                && string.Equals(git, s_gitDir, StringComparison.OrdinalIgnoreCase)
                && string.Equals(prefix, s_projectPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            Stop();
            s_repoRoot = root;
            s_gitDir = git;
            s_projectPrefix = prefix;
            s_active = true;

            if (WindowsDirectoryWatcher.IsSupported)
            {
                Watch(root);
                // worktree 의 git 폴더는 작업 트리 밖에 있다.
                if (git.StartsWith(root, StringComparison.OrdinalIgnoreCase) == false && Directory.Exists(git))
                {
                    Watch(git);
                }
            }
            else
            {
                s_polledTimes = ReadPolledTimes();
            }
            EditorApplication.update += OnUpdate;
        }

        public static void Stop()
        {
            EditorApplication.update -= OnUpdate;
            // Dispose 가 감시 스레드를 기다리는 동안 들어온 이벤트도 버린다.
            foreach (WindowsDirectoryWatcher watcher in s_watchers)
            {
                watcher.Dispose();
            }
            s_watchers.Clear();
            s_active = false;
            s_polledTimes = null;
            s_workingTreeDirty = 0;
            s_fullDirty = 0;
        }

        private static void Watch(string path)
        {
            try
            {
                // 버퍼가 넘쳐 이벤트를 잃었다(onOverflow). 무엇이 바뀌었는지 모르니 전부 다시 읽는다.
                s_watchers.Add(new WindowsDirectoryWatcher(path.TrimEnd('/'), Mark, () => Interlocked.Exchange(ref s_fullDirty, 1)));
            }
            catch (Exception e)
            {
                Debug.LogWarning(GitText.Format("watcher.failed", path, e.Message));
            }
        }

        /// <summary>감시 스레드에서 불린다. 경로로 새로고침 범위를 정해 플래그만 세운다.</summary>
        private static void Mark(string fullPath)
        {
            switch (Classify(fullPath, s_repoRoot, s_gitDir, s_projectPrefix))
            {
                case GitChangeKind.WorkingTree:
                    Interlocked.Exchange(ref s_workingTreeDirty, 1);
                    break;
                case GitChangeKind.Full:
                    Interlocked.Exchange(ref s_fullDirty, 1);
                    break;
            }
        }

        /// <summary>
        /// 바뀐 경로가 어떤 새로고침을 부르는지. 경로 구분자(\·/)와 대소문자는 가리지 않는다.
        /// </summary>
        /// <param name="repoRoot">저장소 루트.</param>
        /// <param name="gitDir">git 폴더. worktree 면 작업 트리 밖일 수 있다.</param>
        /// <param name="projectPrefix">저장소 루트에서 Unity 프로젝트까지("" 또는 "client/unity/"). Library 같은 폴더는 이 아래에서만 무시한다.</param>
        public static GitChangeKind Classify(string fullPath, string repoRoot, string gitDir, string projectPrefix)
        {
            if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(repoRoot) || string.IsNullOrEmpty(gitDir))
            {
                return GitChangeKind.None;
            }
            string path = fullPath.Replace('\\', '/');
            repoRoot = WithSlash(repoRoot);
            gitDir = WithSlash(gitDir);

            if (path.StartsWith(gitDir, StringComparison.OrdinalIgnoreCase))
            {
                return ClassifyGitDir(path.Substring(gitDir.Length));
            }
            if (path.StartsWith(repoRoot, StringComparison.OrdinalIgnoreCase) == false)
            {
                return GitChangeKind.None;
            }

            string relative = path.Substring(repoRoot.Length);
            // worktree 의 .git 파일, 또는 하위 모듈의 .git 내부.
            if (relative.Length == 0
                || relative.Equals(".git", StringComparison.OrdinalIgnoreCase)
                || relative.StartsWith(".git/", StringComparison.OrdinalIgnoreCase)
                || relative.IndexOf("/.git/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return GitChangeKind.None;
            }
            string prefix = projectPrefix ?? string.Empty;
            if (relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                string projectRelative = relative.Substring(prefix.Length);
                int slash = projectRelative.IndexOf('/');
                string top = slash < 0 ? projectRelative : projectRelative.Substring(0, slash);
                if (s_ignoredProjectFolders.Contains(top))
                {
                    return GitChangeKind.None;
                }
            }
            return GitChangeKind.WorkingTree;
        }

        /// <param name="relative">git 폴더 안의 상대 경로("HEAD", "refs/heads/main", "objects/ab/…").</param>
        private static GitChangeKind ClassifyGitDir(string relative)
        {
            if (relative.Length == 0
                || relative.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)
                || StartsWithSegment(relative, "objects")
                || StartsWithSegment(relative, "logs")
                || StartsWithSegment(relative, "hooks")
                || StartsWithSegment(relative, "lfs")
                || StartsWithSegment(relative, "modules")
                || StartsWithSegment(relative, "fsmonitor--daemon")
                || relative.Equals("COMMIT_EDITMSG", StringComparison.OrdinalIgnoreCase)
                || relative.Equals("ORIG_HEAD", StringComparison.OrdinalIgnoreCase))
            {
                return GitChangeKind.None;
            }
            // 터미널의 git add·reset 은 index 만 바꾼다 — HEAD·브랜치는 그대로다.
            if (relative.Equals("index", StringComparison.OrdinalIgnoreCase))
            {
                return GitChangeKind.WorkingTree;
            }
            // HEAD·refs·packed-refs·FETCH_HEAD·진행 중 작업 표시(MERGE_HEAD·CHERRY_PICK_HEAD·REVERT_HEAD·rebase-merge·sequencer)·config(사용자 이름) 등.
            return GitChangeKind.Full;
        }

        private static bool StartsWithSegment(string path, string segment)
        {
            return path.StartsWith(segment, StringComparison.OrdinalIgnoreCase)
                && (path.Length == segment.Length || path[segment.Length] == '/');
        }

        private static void OnUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (s_polledTimes != null && now >= s_nextPollTime)
            {
                s_nextPollTime = now + PollIntervalSeconds;
                Poll();
            }

            if (s_workingTreeDirty == 0 && s_fullDirty == 0)
            {
                return;
            }
            if (now - s_lastRequestTime < MinIntervalSeconds)
            {
                return;
            }
            s_lastRequestTime = now;
            bool full = Interlocked.Exchange(ref s_fullDirty, 0) == 1;
            Interlocked.Exchange(ref s_workingTreeDirty, 0);
            GitRepository.RequestRefresh(workingTreeOnly: full == false);
        }

        private static void Poll()
        {
            DateTime[] times = ReadPolledTimes();
            for (int i = 0; i < times.Length; i++)
            {
                if (times[i] != s_polledTimes[i])
                {
                    // logs/HEAD 는 감시 이벤트라면 무시하는 경로라 Classify 를 거치지 않는다.
                    Interlocked.Exchange(ref i == 0 ? ref s_workingTreeDirty : ref s_fullDirty, 1);
                }
            }
            s_polledTimes = times;
        }

        /// <summary>
        /// 없는 파일은 <see cref="DateTime.MinValue"/> — 생기거나 사라지는 것(MERGE_HEAD)도 변경으로 잡힌다.
        /// 폴더(rebase-merge)는 안에 파일이 생기거나 바뀌면 수정 시각이 바뀌므로 rebase 가 한 커밋씩 나아가는 것도 잡힌다.
        /// </summary>
        private static DateTime[] ReadPolledTimes()
        {
            DateTime[] times = new DateTime[s_polledGitFiles.Length];
            for (int i = 0; i < times.Length; i++)
            {
                string path = s_gitDir + s_polledGitFiles[i];
                times[i] = File.Exists(path) ? File.GetLastWriteTimeUtc(path)
                    : Directory.Exists(path) ? Directory.GetLastWriteTimeUtc(path)
                    : DateTime.MinValue;
            }
            return times;
        }

        private static string WithSlash(string path)
        {
            path = path.Replace('\\', '/');
            return path.EndsWith("/") ? path : path + "/";
        }
    }
}
