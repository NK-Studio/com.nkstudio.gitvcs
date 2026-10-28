using System;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 브랜치 탐색기 탭의 공통 부모. 모양(<see cref="GitExplorerStyle"/>)에 따라 <see cref="BranchExplorerView"/>(브랜치 그래프) 또는
    /// <see cref="CommitHistoryView"/>(Fork 형태 커밋 목록) 가 들어간다. 다른 탭·사이드바는 이 타입으로 찾아 커밋을 보여 달라고 한다.
    /// </summary>
    public abstract class ExplorerView : GitView, ICommitRevealer
    {
        /// <summary>
        /// 한 번에 읽을 커밋 수. 커밋 ~4500·브랜치 3000 저장소에서 읽기~배치 ~0.3초, 그리기 한 장 ~7ms(2026-10-18 실측).
        /// </summary>
        protected const int MaxCommits = 5000;

        protected static readonly string[] RangeKeys = { "explorer.rangeWeek", "explorer.rangeMonth", "explorer.rangeQuarter", "explorer.rangeAll" };

        protected static int AllRangeIndex => RangeKeys.Length - 1;

        public override string Title => GitText.Get("tab.explorer");

        protected ExplorerView(GitServices services, string layoutName) : base(services, layoutName)
        {
            BindStyleMenu(Root.Require<ToolbarMenu>("style-menu"));
        }

        /// <summary>
        /// 도구 막대 오른쪽의 모양 고르기(그래프 · 커밋 목록). GitKraken 배치는 그래프로 고정이라 숨긴다.
        /// 고르면 창이 탭을 다시 만든다(<see cref="GitExplorerStyle.Changed"/>).
        /// </summary>
        private static void BindStyleMenu(ToolbarMenu menu)
        {
            menu.SetVisible(GitExplorerStyle.IsChoosable);
            menu.text = GitText.Get("explorerStyleShort." + GitExplorerStyle.Effective);
            foreach (ExplorerStyle style in (ExplorerStyle[])Enum.GetValues(typeof(ExplorerStyle)))
            {
                ExplorerStyle captured = style;
                menu.menu.AppendAction(GitExplorerStyle.DisplayName(style), _ => GitExplorerStyle.Style = captured,
                    _ => GitExplorerStyle.Style == captured ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }
        }

        /// <summary>
        /// 커밋을 골라 보이는 곳으로 옮긴다(사이드바·다른 탭의 "브랜치 탐색기에서 보기"). 지금 기간에 없으면 기간을 "전체" 로 넓혀 다시 읽는다.
        /// </summary>
        public abstract void Reveal(string hash);

        public bool RevealCommit(string hash)
        {
            Reveal(hash);
            return true;
        }

        /// <summary>
        /// 기간 고르기 번호 → 그 날 이후만 읽는다. 전체면 null.
        /// </summary>
        protected static DateTime? SinceOf(int rangeIndex)
        {
            return rangeIndex switch
            {
                0 => DateTime.Now.AddDays(-7),
                1 => DateTime.Now.AddMonths(-1),
                2 => DateTime.Now.AddMonths(-3),
                _ => null,
            };
        }
    }
}
