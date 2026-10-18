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
        /// 한 줄 높이. 원(지름 10)과 위아래로 지나가는 선 사이에 여유를 두고, 메시지 한 줄이 들어가는 높이.
        /// </summary>
        public const float RowHeight = 26f;

        /// <summary>
        /// 브랜치 열 사이 간격. 아바타를 그릴 때는 원(지름 18 + 테두리)이 옆 열과 겹치지 않게 넓힌다.
        /// </summary>
        public const float LaneSpacing = 14f;
        public const float AvatarLaneSpacing = 22f;

        /// <summary>
        /// 아바타 원의 반지름. 줄 높이(26)에 HEAD 고리까지 들어가는 크기.
        /// </summary>
        public const float AvatarRadius = 9f;

        public const float LeftPadding = 12f;
        public const float AvatarLeftPadding = 14f;
        public const float MinWidth = 40f;

        /// <summary>
        /// 그래프 칸의 최대 폭. 브랜치가 아주 많아도 메시지 칸을 밀어내지 않게 자른다(넘는 열은 가려진다).
        /// </summary>
        public const float MaxWidth = 220f;

        public readonly List<GitGraphNode> NodesNewestFirst;
        public readonly Dictionary<GitGraphNode, int> RowOf = new();
        public readonly bool HasWip;
        public readonly bool HasAvatars;
        public readonly float Width;

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
            int laneCount = graph == null || graph.Lanes.Count == 0 ? 1 : graph.Lanes.Max(lane => lane.Row) + 1;
            Width = Mathf.Clamp(Padding * 2f + (laneCount - 1) * Spacing, MinWidth, MaxWidth);
        }

        private float Spacing => HasAvatars ? AvatarLaneSpacing : LaneSpacing;

        private float Padding => HasAvatars ? AvatarLeftPadding : LeftPadding;

        /// <param name="hasAvatars">커밋을 아바타 원으로 그리면 true(열 간격이 넓어진다).</param>
        public static VerticalGraphLayout Build(GitGraph graph, bool hasWip, bool hasAvatars = false)
        {
            return new VerticalGraphLayout(graph, hasWip, hasAvatars);
        }

        public float LaneX(int laneRow)
        {
            return Padding + laneRow * Spacing;
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
