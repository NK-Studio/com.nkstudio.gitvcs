using System;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;

namespace GitVersionControl
{
    /// <summary>
    /// git 쓰기 작업을 한 번에 하나만 돌리고, 진행 상태를 알리고, 끝나면 저장소를 새로고침한다.
    /// 뷰는 이것만 보고 버튼을 끄고 켠다 — 창 클래스에 의존하지 않는다.
    /// </summary>
    public interface IGitOperationRunner
    {
        bool IsBusy { get; }
        string StatusText { get; }

        /// <summary>IsBusy 나 StatusText 가 바뀔 때.</summary>
        event Action StateChanged;

        /// <param name="refreshAssets">작업 트리 파일이 바뀌는 작업(Pull·전환·되돌리기)이면 끝난 뒤 AssetDatabase 를 새로고침한다.</param>
        /// <param name="workingTreeOnly">HEAD·브랜치·stash 를 바꾸지 않는 작업(Stage·Unstage)이면 true — 끝난 뒤 git status 만 다시 읽는다.</param>
        Task<GitResult> RunAsync(string title, Func<Task<GitResult>> operation, bool refreshAssets, bool showErrorDialog = true, bool workingTreeOnly = false);

        void SetStatus(string text);
    }

#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed class GitOperationRunner : IGitOperationRunner
    {
        /// <summary>
        /// 작업 뒤 새로고침 반영을 기다리는 최대 시간. 넘으면 버튼을 그냥 다시 켠다.
        /// </summary>
        private const int RefreshWaitLimitMs = 10000;

        private readonly IGitRepositoryState _repository;
        private readonly IDialogService _dialogs;

        public GitOperationRunner(IGitRepositoryState repository, IDialogService dialogs)
        {
            _repository = repository;
            _dialogs = dialogs;
        }

        private static int s_activeCount;

        /// <summary>어느 창에서든 git 쓰기 작업이 도는 중인지. 자동 fetch 는 그동안 쉬어 참조 잠금이 겹치지 않게 한다(<see cref="GitAutoFetch"/>).</summary>
        public static bool AnyBusy => s_activeCount > 0;

        public bool IsBusy { get; private set; }
        public string StatusText { get; private set; } = string.Empty;

        /// <summary>
        /// 원격 작업이 인증 문제로 실패했을 때(<see cref="GitAuth.IsAuthFailure"/>) 오류 알림 대신 부른다. 계정을 골라 다시 해야 하면 true.
        /// null 이면 다른 실패처럼 알린다. 창이 <see cref="GitAccountDialog"/> 를 넣는다.
        /// </summary>
        public Func<string, Task<bool>> AuthFailureHandler { get; set; }

        /// <summary>
        /// 실패를 알릴 때(제목, git 오류 글) 부른다. null 이면 <see cref="IDialogService.AlertAsync"/> 로 알린다. 창이 <see cref="GitErrorDialog"/> 를 넣는다.
        /// </summary>
        public Func<string, string, Task> ErrorHandler { get; set; }
        public event Action StateChanged;

        public async Task<GitResult> RunAsync(string title, Func<Task<GitResult>> operation, bool refreshAssets, bool showErrorDialog = true, bool workingTreeOnly = false)
        {
            IsBusy = true;
            s_activeCount++;
            SetStatus(GitText.Format("runner.running", title));
            GitResult result;
            try
            {
                // 작업 트리를 바꾸는 작업 전에 에디터 메모리의 수정을 디스크에 써 둔다. 그러지 않으면 git 이 파일을 되돌린 뒤
                // 에디터가 메모리에 남은 (더러운) 에셋을 다시 저장해 방금 한 일(stash·버리기·전환)을 덮어쓴다.
                if (refreshAssets)
                {
                    AssetDatabase.SaveAssets();
                }
                result = await operation();
                // 앞서 git 이 도중에 끝나 잠금 파일이 남았으면, 지우고 다시 할지 묻는다(Fork·SourceTree 처럼).
                if (result.Success == false && GitLockFile.TryFind(result.Message, GitRepository.GitDir, out string lockPath)
                    && await ConfirmRemoveLockAsync(lockPath))
                {
                    result = await operation();
                }
                // 끝나면 "~ 완료" 를 남기지 않고 비운다(GitKraken 처럼 결과는 화면이 바뀐 것으로 보인다). 실패만 남긴다.
                // 계정이 틀렸거나 로그인이 없어 원격이 거부했으면, 오류 글 대신 어느 계정이 거부됐는지 알리고 계정을 고르게 한다(고르면 한 번 더).
                bool authHandled = false;
                if (result.Success == false && showErrorDialog && AuthFailureHandler != null && GitAuth.IsAuthFailure(result.Message))
                {
                    authHandled = true;
                    if (await AuthFailureHandler(result.Message))
                    {
                        result = await operation();
                        authHandled = false;
                    }
                }
                SetStatus(result.Success ? string.Empty : GitText.Format("runner.failed", title));
                if (result.Success == false)
                {
                    ClearStatusLater(StatusText);
                }
                if (result.Success == false && showErrorDialog && authHandled == false)
                {
                    if (ErrorHandler != null)
                    {
                        await ErrorHandler(GitText.Format("runner.failed", title), result.Message);
                    }
                    else
                    {
                        await _dialogs.AlertAsync(GitText.Format("runner.failed", title), result.Message);
                    }
                }
                if (refreshAssets)
                {
                    // git 이 바꾼 파일을 다시 읽어 메모리를 디스크에 맞춘다.
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                }
            }
            finally
            {
                // 결과가 화면에 반영될 때까지(새로고침 뒤 Changed) 바쁨으로 둔다 — 그 전에 버튼이 다시 켜지면 낡은 목록 위에서
                // 또 누를 수 있다(GitKraken 도 그때까지 버튼을 끈다). 새로고침이 멈춰도 버튼이 영영 꺼져 있지 않게 시간을 둔다.
                _repository.RequestRefresh(immediate: true, workingTreeOnly);
                await Task.WhenAny(_repository.WaitForRefreshAsync(), Task.Delay(RefreshWaitLimitMs));
                IsBusy = false;
                s_activeCount--;
                StateChanged?.Invoke();
            }
            return result;
        }

        /// <summary>
        /// 남은 잠금 파일을 지울지 묻고, 그러라면 지운다. 정말 다른 git 이 돌고 있으면 그쪽이 망가질 수 있어 늘 묻는다.
        /// </summary>
        private async Task<bool> ConfirmRemoveLockAsync(string lockPath)
        {
            if (File.Exists(lockPath) == false)
            {
                // 그 사이 다른 git 이 끝나며 지웠다. 그냥 다시 해 본다.
                return true;
            }
            TimeSpan age = DateTime.Now - File.GetLastWriteTime(lockPath);
            string ageText = age.TotalMinutes >= 1 ? GitText.Format("lock.ageMinutes", (int)age.TotalMinutes) : GitText.Format("lock.ageSeconds", Math.Max(0, (int)age.TotalSeconds));
            if (await _dialogs.ConfirmAsync(GitText.Get("lock.title"), GitText.Format("lock.message", lockPath, ageText), GitText.Get("lock.remove"), danger: true) == false)
            {
                return false;
            }
            try
            {
                File.Delete(lockPath);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                await _dialogs.AlertAsync(GitText.Get("lock.title"), e.Message);
                return false;
            }
        }

        /// <summary>실패 문구가 화면에 남는 시간. 그 뒤(그 사이 다른 문구가 없으면) 지운다.</summary>
        private const int FailureStatusLifetimeMs = 10000;

        private async void ClearStatusLater(string text)
        {
            await Task.Delay(FailureStatusLifetimeMs);
            if (StatusText == text && IsBusy == false)
            {
                SetStatus(string.Empty);
            }
        }

        public void SetStatus(string text)
        {
            StatusText = text ?? string.Empty;
            StateChanged?.Invoke();
        }
    }
}
