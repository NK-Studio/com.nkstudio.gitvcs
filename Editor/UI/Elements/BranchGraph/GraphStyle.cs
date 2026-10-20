using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 그래프의 크기와 색 규칙. Painter2D 로 그리는 선·원은 USS 로 꾸밀 수 없어서 값을 여기 모은다
    /// (글자는 BranchGraph.uss 가 꾸민다).
    /// </summary>
    public static class GraphStyle
    {
        public const float ColumnWidth = 110f;
        public const float RowHeight = 86f;
        public const float OriginX = 120f;
        public const float OriginY = 96f;
        public const float NodeRadius = 15f;
        public const float HeaderHeight = 24f;

        /// <summary>이보다 작게 축소하면 글자를 숨긴다(읽을 수 없는 크기라).</summary>
        public const float TextZoomThreshold = 0.55f;

        public static readonly Color Background = new(0.17f, 0.17f, 0.17f);
        public static readonly Color DateBand = new(1f, 1f, 1f, 0.06f);
        public static readonly Color Head = new(0.2f, 0.75f, 0.35f);
        public static readonly Color Selection = Color.white;
        public static readonly Color SearchMatch = new(1f, 0.9f, 0.2f);
        public static readonly Color Truncated = new(0.6f, 0.6f, 0.6f, 0.5f);

        // 가지 색. 선·원·이름표·띠가 모두 이 색을 쓴다. 테마마다 따로 둔다 — JetBrains 는 밝고 선명한 색(GitKraken·IntelliJ 와 같은 결),
        // Unity 는 에디터의 차분한 회색 바탕과 강조색(#3b82c4)에 맞춰 채도를 낮춘 색. 순서(파랑·초록·주황·보라·분홍·청록·노랑·빨강)는 같다.

        private static readonly Color[] JetBrainsLaneColors =
        {
            new(0.35f, 0.62f, 1f),
            new(0.35f, 0.8f, 0.45f),
            new(1f, 0.62f, 0.25f),
            new(0.72f, 0.52f, 1f),
            new(1f, 0.45f, 0.65f),
            new(0.3f, 0.8f, 0.8f),
            new(0.95f, 0.8f, 0.3f),
            new(0.95f, 0.4f, 0.35f),
        };

        private static readonly Color[] UnityDarkLaneColors =
        {
            new Color32(0x5b, 0x8f, 0xc9, 0xff),
            new Color32(0x73, 0xa8, 0x6a, 0xff),
            new Color32(0xd1, 0x96, 0x50, 0xff),
            new Color32(0x9c, 0x84, 0xc8, 0xff),
            new Color32(0xc4, 0x7a, 0x98, 0xff),
            new Color32(0x5f, 0xa8, 0xa8, 0xff),
            new Color32(0xc8, 0xb4, 0x5a, 0xff),
            new Color32(0xc9, 0x73, 0x68, 0xff),
        };

        /// <summary>
        /// 밝은 에디터 스킨(#c8c8c8 바탕)에서는 같은 색을 어둡게 해 선·글자 대비를 지킨다.
        /// </summary>
        private static readonly Color[] UnityLightLaneColors =
        {
            new Color32(0x2f, 0x6a, 0xa8, 0xff),
            new Color32(0x3f, 0x7f, 0x37, 0xff),
            new Color32(0xb0, 0x6a, 0x1e, 0xff),
            new Color32(0x6f, 0x54, 0xa6, 0xff),
            new Color32(0xa5, 0x4a, 0x72, 0xff),
            new Color32(0x2f, 0x80, 0x80, 0xff),
            new Color32(0x9a, 0x7f, 0x1a, 0xff),
            new Color32(0xa8, 0x44, 0x3a, 0xff),
        };

        private static Color[] LaneColors => GitTheme.Style == GitThemeStyle.Unity
            ? GitTheme.IsDark ? UnityDarkLaneColors : UnityLightLaneColors
            : JetBrainsLaneColors;

        public static Vector2 WorldPosition(GitGraphNode node)
        {
            return new Vector2(OriginX + node.Column * ColumnWidth, OriginY + node.Lane.Row * RowHeight);
        }

        public static float ColumnCenterX(float column)
        {
            return OriginX + column * ColumnWidth;
        }

        public static Color LaneColor(GitGraphLane lane)
        {
            Color color = LaneColors[lane.ColorIndex % LaneColors.Length];
            return lane.IsAnonymous ? Color.Lerp(color, Color.gray, 0.5f) : color;
        }

        /// <summary>작성자마다 고정된 색. 이름 해시로 색상만 바꾸고 채도·명도는 맞춘다.</summary>
        public static Color AuthorColor(string author)
        {
            int hash = 17;
            foreach (char c in author ?? string.Empty)
            {
                hash = hash * 31 + c;
            }
            float hue = (hash & 0x7fffffff) % 360 / 360f;
            return Color.HSVToRGB(hue, 0.55f, 0.75f);
        }
    }
}
