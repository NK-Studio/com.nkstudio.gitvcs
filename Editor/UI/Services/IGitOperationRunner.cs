using System;
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
        public event Action StateChanged;

        public async Task<GitResult> RunAsync(string title, Func<Task<GitResult>> operation, bool refreshAssets, bool showErrorDialog = true, bool workingTreeOnly = false)
        {
            IsBusy = true;
            s_activeCount++;
            SetStatus(GitText.Format("runner.running", title));
            GitResult result;
            try
            {
                result = await operation();
                SetStatus(GitText.Format(result.Success ? "runner.done" : "runner.failed", title));
                if (result.Success == false && showErrorDialog)
                {
                    _dialogs.Alert(GitText.Format("runner.failed", title), result.Message);
                }
                if (refreshAssets)
                {
                    AssetDatabase.Refresh();
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

        public void SetStatus(string text)
        {
            StatusText = text ?? string.Empty;
            StateChanged?.Invoke();
        }
    }
}
