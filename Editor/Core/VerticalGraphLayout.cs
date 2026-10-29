using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// GitKraken 처럼 커밋을 위(최신)에서 아래(과거)로 한 줄씩 놓는 세로 그래프의 배치.
    /// 브랜치 줄 나누기는 가로 탐색기와 같은 <see cref="GitGraph"/> 를 쓰고, 축만 돌린다: 시간 순서 → 줄 번호(y), 브랜치 줄 → 열(x).
    /// 맨 위에 작업 중(WIP) 줄을 둘 수 있다.
    /// </summary>
    public sealed class VerticalGraphLayout
    {
        /// <summary>
        /// 한 줄 높이. GitKraken 처럼 넉넉히 둬서 아바타(지름 24)·HEAD 고리와 위아래 선 사이에 여유를 두고, 13px 메시지가 답답하지 않게 한다.
        /// </summary>
        public const float RowHeight = 34f;

        /// <summary>
        /// 브랜치 열 사이 간격. 아바타를 그릴 때는 원(지름 24 + 테두리)이 옆 열과 겹치지 않게 넓힌다.
        /// </summary>
        public const float LaneSpacing = 18f;
        public const float AvatarLaneSpacing = 30f;

        /// <summary>
        /// 아바타 원의 반지름. 줄 높이(34)에 HEAD 고리까지 들어가는 크기.
        /// </summary>
        public const float AvatarRadius = 12f;

        public const float LeftPadding = 14f;
        public const float AvatarLeftPadding = 20f;
        public const float MinWidth = 48f;

        /// <summary>
        /// 그래프 칸의 최대 폭. 브랜치가 아주 많아도 메시지 칸을 밀어내지 않게 자른다(넘는 열은 가려진다).
        /// </summary>
        public const float MaxWidth = 260f;

        public readonly List<GitGraphNode> NodesNewestFirst;
        public readonly Dictionary<GitGraphNode, int> RowOf = new();
        public readonly bool HasWip;
        public readonly bool HasAvatars;
        public readonly float Width;

        /// <summary>
        /// 가지 열 수(적어도 1).
        /// </summary>
        public readonly int LaneCount;

        /// <summary>
        /// 커밋 줄이 시작하는 번호(WIP 줄이 있으면 1).
        /// </summary>
        public int RowOffset => HasWip ? 1 : 0;

        public int RowCount => NodesNewestFirst.Count + RowOffset;

        private VerticalGraphLayout(GitGraph graph, bool hasWip, bool hasAvatars)
        {
            HasWip = hasWip;
            HasAvatars = hasAvatars;
            NodesNewestFirst = graph == null ? new List<GitGraphNode>() : graph.Nodes.OrderByDescending(node => node.Column).ToList();
            for (int i = 0; i < NodesNewestFirst.Count; i++)
            {
                RowOf[NodesNewestFirst[i]] = i + RowOffset;
            }
            LaneCount = graph == null || graph.Lanes.Count == 0 ? 1 : graph.Lanes.Max(lane => lane.Row) + 1;
            // 기본 폭은 가지 묶음 양옆에 넉넉한 여백(MaxFirstLaneX)을 둔 폭이라 가지가 하나면 가운데에 온다.
            Width = Mathf.Clamp(MaxFirstLaneX * 2f + (LaneCount - 1) * Spacing, MinWidth, MaxWidth);
        }

        /// <summary>
        /// 첫 열의 커밋 원(HEAD 고리까지)이 잘리지 않는 가장 좁은 폭. 첫 열이 왼쪽 여백 자리에 있으므로 여백의 두 배.
        /// </summary>
        public float OneLaneWidth => Padding * 2f;

        /// <summary>
        /// 칸이 넉넉할 때의 열 간격(좁아지면 <see cref="SpacingFor"/> 가 줄인다).
        /// </summary>
        public float Spacing => HasAvatars ? AvatarLaneSpacing : LaneSpacing;

        private float Padding => HasAvatars ? AvatarLeftPadding : LeftPadding;

        /// <param name="hasAvatars">커밋을 아바타 원으로 그리면 true(열 간격이 넓어진다).</param>
        public static VerticalGraphLayout Build(GitGraph graph, bool hasWip, bool hasAvatars = false)
        {
            return new VerticalGraphLayout(graph, hasWip, hasAvatars);
        }

        /// <summary>
        /// 칸이 넉넉할 때 첫 열을 칸 왼쪽에서 띄우는 최대 거리(가지 간격 두 칸, GitKraken 과 같다).
        /// </summary>
        public float MaxFirstLaneX => Mathf.Max(Padding, Spacing * 2f);

        /// <summary>
        /// 커밋 원의 가장 바깥(HEAD 고리) 반지름. <c>CommitGraphList.DrawNodes</c> 의 고리 크기와 맞춘다.
        /// </summary>
        public float NodeOuterRadius => HasAvatars ? AvatarRadius + 4f : 8f;

        /// <summary>
        /// 그래프 칸 폭에 맞춘 열 간격. 가지 묶음이 양옆 여백(<see cref="Padding"/>) 안에 다 들어가면 기본 간격이고,
        /// 그보다 좁으면 간격을 이어서 좁혀 맨 오른쪽 가지도 칸 안에 둔다 — 원이 칸 경계에 반쯤 잘리지 않고,
        /// 가장 좁은 폭(<see cref="OneLaneWidth"/>)에서 0 이 되어 모든 가지가 첫 열에 모인다(그때 한 줄로 접힌다).
        /// </summary>
        /// <param name="contentWidth">그래프 칸에서 오른쪽 가지 색 막대를 뺀 폭.</param>
        /// <param name="laneCount">그릴 열 수.</param>
        public float SpacingFor(float contentWidth, int laneCount)
        {
            if (laneCount <= 1)
            {
                return Spacing;
            }
            return Mathf.Clamp((contentWidth - Padding * 2f) / (laneCount - 1), 0f, Spacing);
        }

        /// <summary>
        /// 그래프 칸 폭에 맞춘 첫 열의 x. 가지 묶음을 칸 가운데에 두되 왼쪽 여백은 <see cref="MaxFirstLaneX"/> 를 넘지 않고,
        /// 칸이 좁아지면 여백부터 줄여 <see cref="Padding"/> 까지 내려간다(그다음은 열 간격이 줄어든다 — <see cref="SpacingFor"/>).
        /// 가지가 하나뿐이면 여백을 줄이지 않고 그 자리에 둔다 — 칸은 원 오른쪽까지만 줄어든다(GitKraken 과 같다).
        /// 폭에 따라 이어서 바뀌므로 칸을 끌어도 원이 튀지 않는다.
        /// </summary>
        /// <param name="contentWidth">그래프 칸에서 오른쪽 가지 색 막대를 뺀 폭.</param>
        /// <param name="laneCount">그릴 열 수.</param>
        public float FirstLaneX(float contentWidth, int laneCount)
        {
            if (laneCount <= 1)
            {
                return MaxFirstLaneX;
            }
            float centered = (contentWidth - (laneCount - 1) * SpacingFor(contentWidth, laneCount)) * 0.5f;
            return Mathf.Clamp(centered, Padding, MaxFirstLaneX);
        }

        public float LaneX(int laneRow, float firstLaneX, float spacing)
        {
            return firstLaneX + laneRow * spacing;
        }

        /// <summary>
        /// 줄 가운데 y. <paramref name="scrollY"/> 는 목록이 내려간 만큼.
        /// </summary>
        public static float RowCenterY(int row, float scrollY)
        {
            return row * RowHeight + RowHeight * 0.5f - scrollY;
        }
    }
}
