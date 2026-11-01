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
        /// 그래프 칸의 최대 폭. 브랜치가 아주 많아도 메시지 칸을 밀어내지 않게 자른다(넘으면 열 간격이 좁아지고 먼 가지 선부터 흐려진다).
        /// 지금 브랜치를 맨 왼쪽에 두면 가지마다 열을 따로 쓰므로, 아바타 간격으로 열 열세 개 남짓이 다 보이게 넉넉히 둔다.
        /// </summary>
        public const float MaxWidth = 480f;

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
            // 기본 폭은 가지 묶음 양옆에 여백(Padding)만 둔 폭 — 맨 오른쪽 원이 벽에 막 닿는 폭이다.
            Width = Mathf.Clamp(Padding * 2f + (LaneCount - 1) * Spacing, MinWidth, MaxWidth);
        }

        /// <summary>
        /// 첫 열의 커밋 원(HEAD 고리까지)이 잘리지 않는 가장 좁은 폭. 첫 열이 왼쪽 여백 자리에 있으므로 여백의 두 배.
        /// </summary>
        public float OneLaneWidth => FirstLaneOffset + WallInset;

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
        /// 첫 열이 칸 왼쪽에서 떨어진 거리. 칸 폭과 상관없이 이 자리다 — 칸이 좁아져도 첫 열이 미리 물러나지 않는다.
        /// </summary>
        public float FirstLaneOffset => Padding;

        /// <summary>
        /// 커밋 원의 가장 바깥(HEAD 고리) 반지름. <c>CommitGraphList.DrawNodes</c> 의 고리 크기와 맞춘다.
        /// </summary>
        public float NodeOuterRadius => HasAvatars ? AvatarRadius + 4f : 8f;

        /// <summary>
        /// 오른쪽 벽이 칸 끝에서 들어온 거리: 원(HEAD 고리까지)의 오른쪽 가장자리가 칸 경계에 닿는 자리에 원 가운데가 온다.
        /// </summary>
        public float WallInset => NodeOuterRadius + 2f;

        /// <summary>
        /// 열 간격의 평균(첫 열에서 벽까지를 고르게 나눈 값, 기본 간격을 넘지 않는다). 0 이면 모든 가지가 첫 열에 모였다 — 한 줄로 접을지 정하는 데 쓴다.
        /// </summary>
        /// <param name="contentWidth">그래프 칸에서 오른쪽 가지 색 막대를 뺀 폭.</param>
        /// <param name="laneCount">그릴 열 수.</param>
        public float SpacingFor(float contentWidth, int laneCount)
        {
            if (laneCount <= 1)
            {
                return Spacing;
            }
            return Mathf.Clamp(WallRoom(contentWidth) / (laneCount - 1), 0f, Spacing);
        }

        /// <summary>첫 열에서 오른쪽 벽까지의 거리. 0 이하면 벽이 첫 열까지 왔다.</summary>
        public float WallRoom(float contentWidth)
        {
            return contentWidth - WallInset - FirstLaneOffset;
        }

        /// <summary>
        /// 열 하나의 x(칸 왼쪽에서). 원은 제자리(첫 열 여백 + 열 번호 × 기본 간격)에 있다가, 칸이 좁아져 오른쪽 벽(<see cref="WallInset"/>)에
        /// 그 원이 닿을 때 비로소 벽에 밀린다 — 원끼리 밀어 주지 않는다. 그래서 벽에 닿은 원들은 벽 자리에 겹쳐 함께 가고,
        /// 아직 닿지 않은 원은 그대로 있다가 벽이 오면 그때 밀린다. 첫 열까지 벽이 오면 모두 한 자리에 모인다.
        /// 폭에 따라 이어서 바뀌므로 칸을 끌어도 원이 튀지 않는다.
        /// </summary>
        /// <param name="lane">열 번호(0 = 첫 열).</param>
        /// <param name="contentWidth">그래프 칸에서 오른쪽 가지 색 막대를 뺀 폭.</param>
        /// <param name="laneCount">그릴 열 수.</param>
        public float LaneOffset(int lane, float contentWidth, int laneCount)
        {
            if (laneCount <= 1)
            {
                return FirstLaneOffset;
            }
            float wall = contentWidth - WallInset;
            return Mathf.Max(FirstLaneOffset, Mathf.Min(FirstLaneOffset + lane * Spacing, wall));
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
