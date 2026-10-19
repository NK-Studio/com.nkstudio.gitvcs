using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Git 창 위쪽 안내 줄: 충돌로 멈춘 rebase·병합·cherry-pick·revert 가 있을 때(터미널에서 시작한 것도) 무엇이 진행 중인지,
    /// 충돌 파일이 몇 개인지 보이고 [이어가기]·[건너뛰기](rebase 만)·[취소] 를 둔다(<see cref="GitInProgress"/>).
    /// 이어가기는 충돌을 모두 풀어 stage 했을 때만 켠다 — 막는 까닭은 글과 툴팁으로 알린다.
    /// </summary>
    public sealed class OperationBanner
    {
        private readonly GitServices _services;
        private readonly VisualElement _root;
        private readonly Label _text;
        private readonly Button _continue;
        private readonly Button _skip;
        private readonly Button _abort;
        private GitOperationKind _kind;

        public OperationBanner(VisualElement layout, GitServices services)
        {
            _services = services;
            _root = layout.Require<VisualElement>("operation-banner");
            _text = layout.Require<Label>("operation-banner-text");
            _continue = layout.Require<Button>("operation-continue-button");
            _skip = layout.Require<Button>("operation-skip-button");
            _abort = layout.Require<Button>("operation-abort-button");
            _continue.clicked += Continue;
            _skip.clicked += Skip;
            _abort.clicked += Abort;
            _root.SetVisible(false);
        }

        public void Render()
        {
            IGitRepositoryState repository = _services.Repository;
            _kind = repository.IsRunning && repository.IsRepository ? repository.InProgressOperation : GitOperationKind.None;
            _root.SetVisible(_kind != GitOperationKind.None);
            if (_kind == GitOperationKind.None)
            {
                return;
            }

            string name = GitInProgress.DisplayName(_kind);
            string verb = GitInProgress.ContinueCommand(_kind)[0];
            int conflicts = repository.Entries.Count(e => e.State == GitFileState.Conflicted);
            string blocker = GitInProgress.ContinueBlocker(_kind, repository.Entries);
            _text.text = conflicts > 0 ? GitText.Format("inprogress.bannerConflicts", name, conflicts)
                : blocker != null ? GitText.Format("inprogress.bannerBlocked", name, blocker)
                : GitText.Format("inprogress.bannerReady", name);

            bool idle = _services.Runner.IsBusy == false;
            _continue.SetEnabled(idle && blocker == null);
            _continue.tooltip = blocker ?? GitText.Format("inprogress.continueTooltip", name, verb);
            _skip.SetVisible(GitInProgress.CanSkip(_kind));
            _skip.SetEnabled(idle);
            _skip.tooltip = GitText.Get("inprogress.skipTooltip");
            _abort.SetEnabled(idle);
            _abort.tooltip = GitText.Format("inprogress.abortTooltip", name, verb);
        }

        private void Continue()
        {
            string title = GitText.Format("inprogress.opContinue", GitInProgress.DisplayName(_kind));
            _ = _services.Runner.RunAsync(title, () => _services.Commands.ContinueOperationAsync(), refreshAssets: true);
        }

        private void Skip()
        {
            string name = GitInProgress.DisplayName(_kind);
            if (_services.Dialogs.Confirm(GitText.Get("inprogress.skip"), GitText.Get("inprogress.skipConfirm"), GitText.Get("inprogress.skip")) == false)
            {
                return;
            }
            _ = _services.Runner.RunAsync(GitText.Format("inprogress.opSkip", name), () => _services.Commands.SkipOperationAsync(), refreshAssets: true);
        }

        private void Abort()
        {
            string name = GitInProgress.DisplayName(_kind);
            if (_services.Dialogs.Confirm(GitText.Get("inprogress.abort"), GitText.Format("inprogress.abortConfirm", name), GitText.Get("inprogress.abort")) == false)
            {
                return;
            }
            _ = _services.Runner.RunAsync(GitText.Format("inprogress.opAbort", name), () => _services.Commands.AbortOperationAsync(), refreshAssets: true);
        }
    }
}
