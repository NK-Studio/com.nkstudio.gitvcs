using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>자동 fetch 한 번의 결과(<see cref="GitAutoFetch.Run"/>).</summary>
    public enum GitAutoFetchOutcome
    {
        /// <summary>원격이 없어 할 일이 없었다.</summary>
        NoRemote,
        Fetched,
        Failed,
    }

    /// <summary>
    /// 원격을 주기적으로 fetch 한다. 다른 곳(다른 PC·동료·CI)에서 푸시한 커밋은 로컬 파일을 바꾸지 않으므로
    /// <see cref="GitFileWatcher"/> 로는 보이지 않는다 — fetch 해야 원격 추적 브랜치가 움직이고, 그 변경을 감시가 잡아 "behind" 가 보인다.
    ///
    /// 백그라운드라 조용히 돈다: 대화 상자·암호 프롬프트를 띄우지 않고, 실패하면 간격을 늘려 다시 해 본다(경고는 연속 실패의 처음 한 번만).
    /// 마지막 fetch 시각은 FETCH_HEAD 로 읽으므로 손으로 한 Fetch·터미널 fetch 도 타이머를 다시 맞춘다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitAutoFetch
    {
        public const int DefaultIntervalMinutes = 5;
        public const int MinIntervalMinutes = 1;
        public const int MaxIntervalMinutes = 120;

        /// <summary>실패가 이어질 때 늘리는 간격의 상한.</summary>
        private static readonly TimeSpan MaxBackoff = TimeSpan.FromHours(1);

        /// <summary>에디터 시작·스크립트 리로드 직후에는 임포트와 겹치지 않게 잠시 기다린다.</summary>
        private const double StartupDelaySeconds = 10;

        private const double CheckIntervalSeconds = 5;

        /// <summary>네트워크가 막혀도 git 이 오래 붙잡지 않게 수동 Fetch(5분)보다 짧게 끊는다.</summary>
        private const int TimeoutMs = 60_000;

        // 도메인 리로드를 넘어 남겨야 실패한 fetch 를 리로드마다 곧바로 다시 하지 않는다.
        private const string LastAttemptKey = "GitVersionControl.AutoFetch.LastAttempt";
        private const string FailuresKey = "GitVersionControl.AutoFetch.Failures";

        /// <summary>
        /// 백그라운드 fetch 가 Git Credential Manager 로그인 창을 띄우지 않게 한다. 터미널 프롬프트는 <see cref="GitCommand"/> 가 이미 끈다.
        /// 저장된 자격 증명이 없으면 그냥 실패하고, 사용자는 수동 Fetch 로 로그인한다.
        /// </summary>
        private static readonly Dictionary<string, string> s_environment = new()
        {
            ["GCM_INTERACTIVE"] = "never",
        };

        private static bool s_running;
        private static bool s_fetching;
        private static double s_startTime;
        private static double s_nextCheckTime;

        public static bool IsFetching => s_fetching;

        /// <summary>
        /// 마지막 자동 fetch 가 계정 문제(<see cref="GitAuth.IsAuthFailure"/>)로 실패했는지. 도구 막대 Fetch 버튼에 경고를 단다 —
        /// 조용히 실패하면 Pull 숫자가 오래된 값인 줄 모른다. 다음 성공(자동·수동)에서 지운다.
        /// </summary>
        public static bool LastFailureWasAuth { get; private set; }

        public static void ClearAuthFailure()
        {
            LastFailureWasAuth = false;
        }

        /// <summary>자동 fetch 가 끝나 결과가 나왔을 때(메인 스레드).</summary>
        public static event Action<GitAutoFetchOutcome> Completed;

        public static void Start()
        {
            if (s_running)
            {
                return;
            }
            s_running = true;
            s_startTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += OnUpdate;
        }

        public static void Stop()
        {
            s_running = false;
            EditorApplication.update -= OnUpdate;
        }

        /// <summary>
        /// 연속 실패 횟수에 따른 대기 시간. 실패할 때마다 두 배로 늘리되 <see cref="MaxBackoff"/> 를 넘기지 않는다(간격이 그보다 길면 간격 그대로).
        /// </summary>
        public static TimeSpan GetDelay(TimeSpan interval, int failures)
        {
            if (failures <= 0)
            {
                return interval;
            }
            double scaled = interval.TotalMinutes * Math.Pow(2, Math.Min(failures, 10));
            TimeSpan cap = interval > MaxBackoff ? interval : MaxBackoff;
            return scaled >= cap.TotalMinutes ? cap : TimeSpan.FromMinutes(scaled);
        }

        /// <summary>
        /// 지금 fetch 할 때인지. 마지막 fetch(FETCH_HEAD)와 마지막 시도 중 늦은 쪽에서 <paramref name="delay"/> 가 지났으면 true.
        /// 둘 다 없으면(한 번도 fetch 하지 않은 저장소) 바로 한다. 시계가 거꾸로 갔으면(미래 시각) 기다리지 않는다.
        /// </summary>
        public static bool IsDue(DateTime now, DateTime? lastFetch, DateTime? lastAttempt, TimeSpan delay)
        {
            DateTime? reference = lastFetch;
            if (lastAttempt.HasValue && (reference.HasValue == false || lastAttempt.Value > reference.Value))
            {
                reference = lastAttempt;
            }
            if (reference.HasValue == false || reference.Value > now)
            {
                return true;
            }
            return now - reference.Value >= delay;
        }

        /// <summary>
        /// 저장소의 모든 원격을 fetch 한다(백그라운드 스레드에서 부른다). 원격이 없으면 git fetch 를 돌리지 않는다.
        /// </summary>
        public static GitAutoFetchOutcome Run(string gitPath, string repoRoot, out string message)
        {
            message = string.Empty;
            GitResult remotes = GitCommand.Run(gitPath, repoRoot, new[] { "remote" });
            if (remotes.Success == false)
            {
                message = remotes.Message;
                return GitAutoFetchOutcome.Failed;
            }
            if (string.IsNullOrWhiteSpace(remotes.Output))
            {
                return GitAutoFetchOutcome.NoRemote;
            }
            GitResult fetch = GitCommand.Run(gitPath, repoRoot, new[] { "fetch", "--all", "--prune", "--quiet" }, TimeoutMs, s_environment);
            message = fetch.Message;
            return fetch.Success ? GitAutoFetchOutcome.Fetched : GitAutoFetchOutcome.Failed;
        }

        /// <summary>조건을 따지지 않고 지금 한 번 fetch 한다. 이미 도는 중이면 그 결과를 기다리지 않고 끝난다.</summary>
        public static async Task<GitAutoFetchOutcome> FetchNowAsync()
        {
            if (s_fetching || GitRepository.IsRepository == false || string.IsNullOrEmpty(GitRepository.RepoRoot))
            {
                return GitAutoFetchOutcome.NoRemote;
            }
            s_fetching = true;
            SessionState.SetString(LastAttemptKey, DateTime.UtcNow.Ticks.ToString());
            string gitPath = GitVcsSettings.instance.GitExecutable;
            string repoRoot = GitRepository.RepoRoot;
            GitAutoFetchOutcome outcome = GitAutoFetchOutcome.Failed;
            try
            {
                string message = string.Empty;
                outcome = await Task.Run(() => Run(gitPath, repoRoot, out message));
                int failures = SessionState.GetInt(FailuresKey, 0);
                if (outcome == GitAutoFetchOutcome.Failed)
                {
                    if (failures == 0)
                    {
                        Debug.LogWarning(GitText.Format("autoFetch.failed", message));
                    }
                    SessionState.SetInt(FailuresKey, failures + 1);
                }
                else
                {
                    SessionState.SetInt(FailuresKey, 0);
                }
                LastFailureWasAuth = outcome == GitAutoFetchOutcome.Failed && GitAuth.IsAuthFailure(message);
                if (outcome == GitAutoFetchOutcome.Fetched)
                {
                    // 파일 감시가 꺼져 있어도 원격 추적 브랜치 변화가 보이게 한다. 감시가 같은 변경을 잡아도 디바운스로 한 번에 합쳐진다.
                    GitRepository.RequestRefresh();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                s_fetching = false;
            }
            Completed?.Invoke(outcome);
            return outcome;
        }

        private static void OnUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (now < s_nextCheckTime || now - s_startTime < StartupDelaySeconds)
            {
                return;
            }
            s_nextCheckTime = now + CheckIntervalSeconds;

            GitVcsSettings settings = GitVcsSettings.instance;
            if (settings.AutoFetch == false
                || s_fetching
                || GitRepository.IsRunning == false
                || GitRepository.IsRepository == false
                || GitRepository.IsOverriddenForTests
                || GitOperationRunner.AnyBusy
                || EditorApplication.isCompiling
                || EditorApplication.isUpdating)
            {
                return;
            }

            TimeSpan delay = GetDelay(TimeSpan.FromMinutes(settings.AutoFetchMinutes), SessionState.GetInt(FailuresKey, 0));
            DateTime? lastFetch = GitRepository.LastFetchTime?.ToUniversalTime();
            if (IsDue(DateTime.UtcNow, lastFetch, ReadLastAttempt(), delay))
            {
                _ = FetchNowAsync();
            }
        }

        private static DateTime? ReadLastAttempt()
        {
            return long.TryParse(SessionState.GetString(LastAttemptKey, string.Empty), out long ticks)
                ? new DateTime(ticks, DateTimeKind.Utc)
                : null;
        }
    }
}
