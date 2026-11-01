using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Git 창 위쪽 안내 줄: 충돌로 멈춘 rebase·병합·cherry-pick·revert 가 있을 때(터미널에서 시작한 것도) 무엇이 진행 중인지,
    /// 충돌 파일이 몇 개인지 보이고 [이어가기]·[건너뛰기](rebase 만)·[취소] 를 둔다(<see cref="GitInProgress"/>).
    /// 이어가기는 충돌을 모두 풀어 stage 했을 때만 켠다 — 막는 까닭은 글과 툴팁으로 알린다.
    /// stash pop·apply 충돌처럼 진행 중인 작업 없이 충돌만 남았으면 버튼 없이 충돌 개수만 알린다.
    /// 한 장의 카드로 보인다: 제목(무엇이 몇 개) · 설명(다음에 할 일) · 버튼. 충돌 중 Git 창을 잃을 수 있는 상황(<see cref="GitConflictSafety"/>)은
    /// 따로 줄을 늘리지 않고 같은 카드 안 한 줄과 표시 색(노랑 → 빨강)으로만 알린다. 긴 설명은 그 줄의 툴팁에 둔다.
    /// </summary>
    public sealed class OperationBanner
    {
        private readonly GitServices _services;
        private readonly VisualElement _root;
        private readonly Label _badge;
        private readonly Label _text;
        private readonly Label _detail;
        private readonly VisualElement _risk;
        private readonly Label _riskText;
        private readonly Button _continue;
        private readonly Button _skip;
        private readonly Button _abort;
        private GitOperationKind _kind;

        public OperationBanner(VisualElement layout, GitServices services)
        {
            _services = services;
            _root = layout.Require<VisualElement>("operation-banner");
            _badge = layout.Require<Label>("operation-banner-badge");
            _text = layout.Require<Label>("operation-banner-text");
            _detail = layout.Require<Label>("operation-banner-detail");
            _risk = layout.Require<VisualElement>("operation-banner-risk");
            _riskText = layout.Require<Label>("operation-banner-risk-text");
            _continue = layout.Require<Button>("operation-continue-button");
            _skip = layout.Require<Button>("operation-skip-button");
            _abort = layout.Require<Button>("operation-abort-button");
            _continue.clicked += Continue;
            _skip.clicked += Skip;
            _abort.clicked += Abort;
            _root.SetVisible(false);
            // 컴파일이 실패해도 도메인은 그대로라 이 창이 살아 있다. 그때 경고를 바로 갱신한다.
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            _root.RegisterCallback<DetachFromPanelEvent>(_ => CompilationPipeline.compilationFinished -= OnCompilationFinished);
        }

        private void OnCompilationFinished(object _)
        {
            _root.schedule.Execute(Render).StartingIn(100);
        }

        private void RenderSafety(bool active)
        {
            GitConflictRisk risk = active
                ? GitConflictSafety.Check(_services.Repository.Entries, _kind != GitOperationKind.None, EditorUtility.scriptCompilationFailed)
                : GitConflictRisk.None;
            bool risky = risk != GitConflictRisk.None;
            _root.EnableInClassList("operation-banner--risk", risky);
            _risk.SetVisible(risky);
            if (risky)
            {
                _riskText.text = GitText.Get("safety.short." + risk);
                _risk.tooltip = GitText.Get("safety." + risk);
            }
        }

        public void Render()
        {
            IGitRepositoryState repository = _services.Repository;
            bool active = repository.IsRunning && repository.IsRepository;
            _kind = active ? repository.InProgressOperation : GitOperationKind.None;
            RenderSafety(active);
            if (_kind == GitOperationKind.None)
            {
                // stash pop·apply 처럼 진행 중인 작업(MERGE_HEAD 등) 없이 남은 충돌: 개수만 알린다. 이어가기·취소할 작업이 없으니 버튼은 숨긴다.
                int leftover = active ? repository.Entries.Count(e => e.State == GitFileState.Conflicted) : 0;
                _root.SetVisible(leftover > 0);
                _continue.SetVisible(false);
                _skip.SetVisible(false);
                _abort.SetVisible(false);
                SetReady(false);
                if (leftover > 0)
                {
                    _text.text = GitText.Format("inprogress.titleConflictsNoOp", leftover);
                    ShowDetail(GitText.Get("inprogress.detailConflictsNoOp"));
                }
                return;
            }
            _root.SetVisible(true);
            _continue.SetVisible(true);
            _abort.SetVisible(true);

            string name = GitInProgress.DisplayName(_kind);
            string verb = GitInProgress.ContinueCommand(_kind)[0];
            int conflicts = repository.Entries.Count(e => e.State == GitFileState.Conflicted);
            string blocker = GitInProgress.ContinueBlocker(_kind, repository.Entries);
            _text.text = conflicts > 0 ? GitText.Format("inprogress.titleConflicts", name, conflicts)
                : blocker != null ? GitText.Format("inprogress.titleBlocked", name)
                : GitText.Format("inprogress.titleReady", name);
            ShowDetail(conflicts > 0 ? GitText.Get("inprogress.detailConflicts")
                : blocker ?? GitText.Get("inprogress.detailReady"));
            // 이어갈 수 있을 때만 [이어가기] 를 강조색으로 — 지금 누를 단추가 하나로 보이게. 표시도 "!" 대신 초록 체크로 가라앉힌다.
            _continue.EnableInClassList("operation-banner__button--primary", blocker == null);
            SetReady(conflicts == 0 && blocker == null);

            bool idle = _services.Runner.IsBusy == false;
            _continue.SetEnabled(idle && blocker == null);
            _continue.tooltip = blocker ?? GitText.Format("inprogress.continueTooltip", name, verb);
            _skip.SetVisible(GitInProgress.CanSkip(_kind));
            _skip.SetEnabled(idle);
            _skip.tooltip = GitText.Get("inprogress.skipTooltip");
            _abort.SetEnabled(idle);
            _abort.tooltip = GitText.Format("inprogress.abortTooltip", name, verb);
        }

        private void SetReady(bool ready)
        {
            _root.EnableInClassList("operation-banner--ready", ready && _root.ClassListContains("operation-banner--risk") == false);
            _badge.text = ready ? "✓" : "!";
        }

        private void ShowDetail(string text)
        {
            _detail.text = text ?? string.Empty;
            _detail.SetVisible(string.IsNullOrEmpty(text) == false);
        }

        private void Continue()
        {
            string title = GitText.Format("inprogress.opContinue", GitInProgress.DisplayName(_kind));
            _ = _services.Runner.RunAsync(title, () => _services.Commands.ContinueOperationAsync(), refreshAssets: true);
        }

        private async void Skip()
        {
            string name = GitInProgress.DisplayName(_kind);
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("inprogress.skip"), GitText.Get("inprogress.skipConfirm"), GitText.Get("inprogress.skip"), danger: true) == false)
            {
                return;
            }
            _ = _services.Runner.RunAsync(GitText.Format("inprogress.opSkip", name), () => _services.Commands.SkipOperationAsync(), refreshAssets: true);
        }

        private async void Abort()
        {
            string name = GitInProgress.DisplayName(_kind);
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("inprogress.abort"), GitText.Format("inprogress.abortConfirm", name), GitText.Get("inprogress.abort"), danger: true) == false)
            {
                return;
            }
            _ = _services.Runner.RunAsync(GitText.Format("inprogress.opAbort", name), () => _services.Commands.AbortOperationAsync(), refreshAssets: true);
        }
    }
}
