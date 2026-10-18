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
        Task<GitResult> RunAsync(string title, Func<Task<GitResult>> operation, bool refreshAssets, bool showErrorDialog = true);

        void SetStatus(string text);
    }

    public sealed class GitOperationRunner : IGitOperationRunner
    {
        private readonly IGitRepositoryState _repository;
        private readonly IDialogService _dialogs;

        public GitOperationRunner(IGitRepositoryState repository, IDialogService dialogs)
        {
            _repository = repository;
            _dialogs = dialogs;
        }

        public bool IsBusy { get; private set; }
        public string StatusText { get; private set; } = string.Empty;
        public event Action StateChanged;

        public async Task<GitResult> RunAsync(string title, Func<Task<GitResult>> operation, bool refreshAssets, bool showErrorDialog = true)
        {
            IsBusy = true;
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
                IsBusy = false;
                _repository.RequestRefresh(immediate: true);
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
