using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 충돌로 멈춘 rebase·병합·cherry-pick·revert 의 상태 카드(<c>OperationCard.uxml</c>, 터미널에서 시작한 것도).
    /// 창 위쪽이 아니라 충돌 파일 목록 바로 위(Fork·GitKraken 은 충돌 구역, Unity VCS 는 커밋 줄 아래)에 둔다 — 상태와 할 일을 한곳에서 본다.
    /// 카드는 알리기만 한다: 제목(충돌 몇 개) · 설명(무엇을 어디에 합치는 중인지) · 위험 한 줄. 마치기·중단은 커밋 버튼 자리가 맡는다(<see cref="OperationActions"/>).
    /// stash pop·apply 충돌처럼 진행 중인 작업 없이 충돌만 남았으면 개수만 알린다.
    /// 충돌 중 Git 창을 잃을 수 있는 상황(<see cref="GitConflictSafety"/>)은 줄을 늘리지 않고 카드 안 한 줄과 표시 색(노랑 → 빨강)으로만 알린다. 긴 설명은 그 줄의 툴팁에 둔다.
    /// </summary>
    public sealed class OperationCard
    {
        private readonly GitServices _services;
        private readonly Label _badge;
        private readonly ActionGlyph _readyGlyph;
        private readonly Label _title;
        private readonly Label _detail;
        private readonly VisualElement _risk;
        private readonly Label _riskText;

        public VisualElement Root { get; }

        public OperationCard(VisualElement host, GitServices services)
        {
            _services = services;
            Root = GitUiAssets.CloneBlock(GitUiAssets.Load("OperationCard"));
            host.Add(Root);
            _badge = Root.Require<Label>("operation-card-badge");
            // 다 풀었을 때의 체크는 앱 전체와 같은 그린 체크(ActionGlyph) — 글자 ✓ 는 글꼴마다 모양이 다르다.
            _readyGlyph = new ActionGlyph(ActionGlyphKind.Check);
            _readyGlyph.AddToClassList("operation-card__badge-check");
            _badge.Add(_readyGlyph);
            _title = Root.Require<Label>("operation-card-title");
            _detail = Root.Require<Label>("operation-card-detail");
            _risk = Root.Require<VisualElement>("operation-card-risk");
            _riskText = Root.Require<Label>("operation-card-risk-text");
            Root.SetVisible(false);
            // 컴파일이 실패해도 도메인은 그대로라 이 창이 살아 있다. 그때 경고를 바로 갱신한다.
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            Root.RegisterCallback<DetachFromPanelEvent>(_ => CompilationPipeline.compilationFinished -= OnCompilationFinished);
        }

        /// <summary>보일 것이 있는가(진행 중인 작업이나 남은 충돌).</summary>
        public bool IsShown { get; private set; }

        private void OnCompilationFinished(object _)
        {
            Root.schedule.Execute(Render).StartingIn(100);
        }

        public void Render()
        {
            IGitRepositoryState repository = _services.Repository;
            bool active = repository.IsRunning && repository.IsRepository;
            GitOperationKind kind = active ? repository.InProgressOperation : GitOperationKind.None;
            int conflicts = active ? repository.Entries.Count(e => e.State == GitFileState.Conflicted) : 0;
            IsShown = kind != GitOperationKind.None || conflicts > 0;
            Root.SetVisible(IsShown);
            if (IsShown == false)
            {
                return;
            }

            GitConflictRisk risk = GitConflictSafety.Check(repository.Entries, kind != GitOperationKind.None, EditorUtility.scriptCompilationFailed);
            bool risky = risk != GitConflictRisk.None;
            Root.EnableInClassList("operation-card--risk", risky);
            _risk.SetVisible(risky);
            if (risky)
            {
                _riskText.text = GitText.Get("safety.short." + risk);
                _risk.tooltip = GitText.Get("safety." + risk);
            }

            string context = StagingLists.DescribeOperation(kind);
            string blocker = kind == GitOperationKind.None ? null : GitInProgress.ContinueBlocker(kind, repository.Entries);
            bool ready = kind != GitOperationKind.None && blocker == null;
            if (conflicts > 0)
            {
                _title.text = GitText.Format("inprogress.titleConflicts", conflicts);
                ShowDetail(kind == GitOperationKind.None ? GitText.Get("inprogress.detailConflictsNoOp") : context);
            }
            else if (ready)
            {
                // 이제 누를 단추가 어디 있는지(커밋 버튼 자리의 [병합 마치기])까지 한 줄에 적는다.
                _title.text = GitText.Get("inprogress.titleReady");
                ShowDetail(GitText.Format("inprogress.detailReady", context, OperationActions.FinishLabel(kind)));
            }
            else
            {
                _title.text = GitText.Format("inprogress.titleBlocked", GitInProgress.DisplayName(kind));
                ShowDetail(blocker);
            }
            // 다 풀었으면 "!" 대신 초록 체크로 가라앉힌다. 위험 줄이 있으면 빨강이 먼저다.
            Root.EnableInClassList("operation-card--ready", ready && risky == false);
            _badge.text = ready ? string.Empty : "!";
            _readyGlyph.SetVisible(ready);
        }

        private void ShowDetail(string text)
        {
            _detail.text = text ?? string.Empty;
            _detail.SetVisible(string.IsNullOrEmpty(text) == false);
        }
    }
}
