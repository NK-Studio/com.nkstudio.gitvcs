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

        // 가지 색. 선·원·이름표·띠가 모두 이 색을 쓴다 — 밝고 선명한 색(GitKraken·IntelliJ 와 같은 결).
        // 순서: 파랑·초록·주황·보라·분홍·청록·노랑·빨강.
        private static readonly Color[] LaneColors =
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

        public static Vector2 WorldPosition(GitGraphNode node)
        {
            return new Vector2(OriginX + node.Column * ColumnWidth, OriginY + node.Lane.Row * RowHeight);
        }

        public static float ColumnCenterX(float column)
        {
            return OriginX + column * ColumnWidth;
        }

        /// <summary>
        /// 지금 있지 않은 브랜치의 이름표에서 커밋까지 잇는 선에 쓰는 가라앉은 가지 색(GitKraken 과 같다): 채도를 낮추고 배경 쪽으로 가라앉힌다. 알파는 그대로.
        /// 어두운 배경(다크 테마)이면 밝기를 낮추고, 밝은 배경(라이트 테마)이면 배경색 쪽으로 섞어 바래게 한다 —
        /// 밝기만 낮추면 라이트 테마에서 선이 거의 검어져 오히려 가장 눈에 띄었다.
        /// </summary>
        /// <param name="background">그 선이 놓이는 바탕색(목록 배경, USS --graph-outline).</param>
        public static Color Muted(Color color, Color background)
        {
            Color.RGBToHSV(color, out float h, out float s, out float v);
            Color muted;
            if (background.grayscale < 0.5f)
            {
                muted = Color.HSVToRGB(h, s * MutedSaturation, v * MutedValue);
            }
            else
            {
                muted = Color.Lerp(Color.HSVToRGB(h, s * MutedSaturation, v), background, MutedLightBlend);
            }
            muted.a = color.a;
            return muted;
        }

        private const float MutedSaturation = 0.45f;

        /// <summary>어두운 배경에서 밝기 배율.</summary>
        private const float MutedValue = 0.15f;

        /// <summary>밝은 배경에서 배경색 쪽으로 섞는 비율.</summary>
        private const float MutedLightBlend = 0.85f;

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
