namespace GitVersionControl
{
    /// <summary>
    /// 충돌로 멈춘 작업을 마치기·건너뛰기·중단하기. 버튼은 상태 카드(<see cref="OperationCard"/>)가 아니라 커밋 버튼 자리에 둔다:
    /// 병합·cherry-pick·revert 는 결국 커밋 하나로 끝나니 커밋 버튼이 [병합 마치기] 가 되고(입력란 메시지로 커밋), rebase 는 [rebase 이어가기] 가 된다.
    /// 중단·건너뛰기는 그 옆의 작은 단추다(GitKraken 과 같은 배치).
    /// </summary>
    public static class OperationActions
    {
        /// <summary>커밋 버튼 자리에 보일 글("병합 마치기", "rebase 이어가기").</summary>
        public static string FinishLabel(GitOperationKind kind)
        {
            string name = GitInProgress.DisplayName(kind);
            return GitInProgress.RequiresFullCommit(kind) ? GitText.Format("inprogress.finish", name) : GitText.Format("inprogress.opContinue", name);
        }

        /// <summary>마치기 버튼 툴팁. 막혔으면 그 까닭.</summary>
        public static string FinishTooltip(GitOperationKind kind, string blocker)
        {
            if (blocker != null)
            {
                return blocker;
            }
            string name = GitInProgress.DisplayName(kind);
            return GitInProgress.RequiresFullCommit(kind)
                ? GitText.Format("inprogress.finishTooltip", name)
                : GitText.Format("inprogress.continueTooltip", name, GitInProgress.ContinueCommand(kind)[0]);
        }

        public static string AbortLabel(GitOperationKind kind)
        {
            return GitText.Format("inprogress.abortKind", GitInProgress.DisplayName(kind));
        }

        public static string AbortTooltip(GitOperationKind kind)
        {
            return GitText.Format("inprogress.abortTooltip", GitInProgress.DisplayName(kind), GitInProgress.ContinueCommand(kind)[0]);
        }

        /// <summary>git 이 준비한 메시지로 이어간다(rebase --continue 등). 병합처럼 커밋으로 끝나는 작업은 커밋 버튼이 직접 커밋한다.</summary>
        public static void Continue(GitServices services)
        {
            string title = GitText.Format("inprogress.opContinue", GitInProgress.DisplayName(services.Repository.InProgressOperation));
            _ = services.Runner.RunAsync(title, () => services.Commands.ContinueOperationAsync(), refreshAssets: true);
        }

        public static async void Skip(GitServices services)
        {
            string name = GitInProgress.DisplayName(services.Repository.InProgressOperation);
            if (await services.Dialogs.ConfirmAsync(GitText.Get("inprogress.skip"), GitText.Get("inprogress.skipConfirm"), GitText.Get("inprogress.skip"), danger: true) == false)
            {
                return;
            }
            _ = services.Runner.RunAsync(GitText.Format("inprogress.opSkip", name), () => services.Commands.SkipOperationAsync(), refreshAssets: true);
        }

        public static async void Abort(GitServices services)
        {
            GitOperationKind kind = services.Repository.InProgressOperation;
            string name = GitInProgress.DisplayName(kind);
            if (await services.Dialogs.ConfirmAsync(AbortLabel(kind), GitText.Format("inprogress.abortConfirm", name), GitText.Get("inprogress.abort"), danger: true) == false)
            {
                return;
            }
            _ = services.Runner.RunAsync(GitText.Format("inprogress.opAbort", name), () => services.Commands.AbortOperationAsync(), refreshAssets: true);
        }
    }
}
